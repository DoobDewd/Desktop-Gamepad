using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DesktopGamepad.Engine
{
    /// <summary>One moment of controller input, in Desktop Gamepad's own terms (input ids match the UI).</summary>
    public sealed class PadState
    {
        public static readonly string[] ButtonIds = { "a", "b", "x", "y", "lb", "rb", "view", "menu", "lclick", "rclick", "dpadUp", "dpadDown", "dpadLeft", "dpadRight" };

        public readonly HashSet<string> Pressed = new HashSet<string>();
        /// <summary>Sticks, -1..1. Right and down are positive.</summary>
        public double LX, LY, RX, RY;
        /// <summary>Triggers, 0..1.</summary>
        public double LT, RT;
    }

    public enum PadKind { Xbox, PlayStation, Generic }

    public sealed class ControllerInfo
    {
        public string Name;
        public string Connection; // "Bluetooth" or "USB"
        public PadKind Kind;
        public string KindId => Kind == PadKind.Xbox ? "xbox" : Kind == PadKind.PlayStation ? "playstation" : "generic";
    }

    /// <summary>
    /// Reads game controllers through Raw Input on its own thread. Raw Input keeps working while other windows are in
    /// front (XInput and the joystick API read zeros there). Reports only arrive while something changes.
    /// </summary>
    public sealed class ControllerReader : IDisposable
    {
        public event Action<ControllerInfo, PadState> Report;
        public event Action<ControllerInfo> Connected;
        public event Action Disconnected;

        readonly Thread thread;
        IntPtr window;
        readonly Dictionary<IntPtr, Device> devices = new Dictionary<IntPtr, Device>();
        IntPtr activeDevice = IntPtr.Zero;

        public ControllerInfo Current { get; private set; }

        public ControllerReader()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "controller reader" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public void Dispose()
        {
            if (window != IntPtr.Zero) Native.PostMessage(window, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        void Run()
        {
            window = Native.CreateWindowEx(0, "Static", "DesktopGamepadControllerReader", 0, 0, 0, 0, 0, Native.HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var wanted = new[]
            {
                new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 4, Flags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY, Target = window }, // joysticks
                new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 5, Flags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY, Target = window }  // gamepads
            };
            bool ok = Native.RegisterRawInputDevices(wanted, (uint)wanted.Length, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
            Log.Write("controller reader started (raw input " + (ok ? "ok" : "failed") + ")");

            while (Native.GetMessage(out Native.MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == Native.WM_INPUT) OnInput(msg.lParam);
                else if (msg.message == Native.WM_INPUT_DEVICE_CHANGE) OnDeviceChange((int)msg.wParam, msg.lParam);
                else if (msg.message == Native.WM_CLOSE) { Native.DestroyWindow(window); break; }
                Native.DispatchMessage(ref msg);
            }
        }

        void OnDeviceChange(int change, IntPtr handle)
        {
            if (change == Native.GIDC_ARRIVAL)
            {
                var dev = Open(handle);
                if (dev != null && activeDevice == IntPtr.Zero) Activate(handle, dev);
            }
            else if (change == Native.GIDC_REMOVAL)
            {
                if (devices.TryGetValue(handle, out Device dev)) { dev.Free(); devices.Remove(handle); }
                if (handle == activeDevice)
                {
                    activeDevice = IntPtr.Zero;
                    Current = null;
                    // Another connected pad takes over, if there is one.
                    foreach (var pair in devices) { Activate(pair.Key, pair.Value); break; }
                    if (activeDevice == IntPtr.Zero) { Log.Write("controller disconnected"); Disconnected?.Invoke(); }
                }
            }
        }

        void Activate(IntPtr handle, Device dev)
        {
            activeDevice = handle;
            Current = dev.Info;
            Log.Write("controller connected: " + dev.Info.Name + " (" + dev.Info.Connection + ", " + dev.Info.KindId + ")");
            Connected?.Invoke(dev.Info);
        }

        void OnInput(IntPtr rawHandle)
        {
            uint header = (uint)(IntPtr.Size * 2 + 8), size = 0;
            Native.GetRawInputData(rawHandle, Native.RID_INPUT, IntPtr.Zero, ref size, header);
            if (size == 0 || size > 8192) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.GetRawInputData(rawHandle, Native.RID_INPUT, buf, ref size, header) != size) return;
                if (Marshal.ReadInt32(buf, 0) != Native.RIM_TYPEHID) return;
                IntPtr handle = Marshal.ReadIntPtr(buf, 8);

                if (!devices.TryGetValue(handle, out Device dev)) dev = Open(handle);
                if (dev == null) return;
                // The first pad that is used becomes the active one.
                if (activeDevice == IntPtr.Zero) Activate(handle, dev);
                if (handle != activeDevice) return;

                int sizeHid = Marshal.ReadInt32(buf, (int)header), count = Marshal.ReadInt32(buf, (int)header + 4);
                for (int i = 0; i < count; i++)
                {
                    IntPtr report = buf + (int)header + 8 + i * sizeHid;
                    var state = dev.Parse(report, (uint)sizeHid);
                    if (state != null) Report?.Invoke(dev.Info, state);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        Device Open(IntPtr handle)
        {
            if (devices.TryGetValue(handle, out Device existing)) return existing;
            var dev = Device.Create(handle);
            if (dev != null) devices[handle] = dev;
            return dev;
        }

        /// <summary>One HID controller: where its inputs are in the report, and how to read them.</summary>
        sealed class Device
        {
            public ControllerInfo Info;
            IntPtr preparsed;
            readonly Dictionary<ushort, Axis> axes = new Dictionary<ushort, Axis>(); // Generic Desktop usage -> axis
            bool sharedTriggerAxis;
            readonly ushort[] usageBuffer = new ushort[64];
            // HID button number -> input id, per layout.
            Dictionary<int, string> buttonMap;

            sealed class Axis { public ushort Link; public long Min, Max; }

            public static Device Create(IntPtr handle)
            {
                uint size = 0;
                Native.GetRawInputDeviceInfo(handle, Native.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
                if (size == 0) return null;
                IntPtr pp = Marshal.AllocHGlobal((int)size);
                if (Native.GetRawInputDeviceInfo(handle, Native.RIDI_PREPARSEDDATA, pp, ref size) != size
                    || Native.HidP_GetCaps(pp, out Native.HIDP_CAPS caps) != Native.HIDP_STATUS_SUCCESS
                    || caps.UsagePage != 1 || (caps.Usage != 4 && caps.Usage != 5))
                {
                    Marshal.FreeHGlobal(pp);
                    return null;
                }

                var dev = new Device { preparsed = pp };
                string path = DeviceName(handle);
                ParseIds(path, out ushort vid, out ushort pid);
                var kind = vid == 0x045E ? PadKind.Xbox : vid == 0x054C ? PadKind.PlayStation : PadKind.Generic;
                string product = ProductString(path);
                dev.Info = new ControllerInfo
                {
                    Kind = kind,
                    Name = !string.IsNullOrWhiteSpace(product) ? product.Trim()
                        : kind == PadKind.Xbox ? "Xbox Wireless Controller"
                        : kind == PadKind.PlayStation ? (pid == 0x0CE6 || pid == 0x0DF2 ? "DualSense Wireless Controller" : "Wireless Controller")
                        : "Game controller",
                    Connection = path.IndexOf("{00001812-", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("BTH", StringComparison.OrdinalIgnoreCase) >= 0 ? "Bluetooth" : "USB"
                };

                ushort n = caps.NumberInputValueCaps;
                IntPtr vc = Marshal.AllocHGlobal(Native.SIZEOF_HIDP_VALUE_CAPS * Math.Max((int)n, 1));
                try
                {
                    if (Native.HidP_GetValueCaps(0, vc, ref n, pp) == Native.HIDP_STATUS_SUCCESS)
                        for (int i = 0; i < n; i++)
                        {
                            IntPtr c = vc + Native.SIZEOF_HIDP_VALUE_CAPS * i;
                            if ((ushort)Marshal.ReadInt16(c, 0) != 1) continue; // Generic Desktop only
                            bool isRange = Marshal.ReadByte(c, 12) != 0;
                            int first = (ushort)Marshal.ReadInt16(c, 56), last = isRange ? (ushort)Marshal.ReadInt16(c, 58) : first;
                            long min = Marshal.ReadInt32(c, 40), max = Marshal.ReadInt32(c, 44);
                            // 16-bit axes often report their maximum as -1: use the bit size instead.
                            int bits = (ushort)Marshal.ReadInt16(c, 18);
                            if (max < min && bits > 0 && bits < 32) { min = 0; max = (1L << bits) - 1; }
                            for (int u = first; u <= last; u++)
                                if (!dev.axes.ContainsKey((ushort)u)) dev.axes[(ushort)u] = new Axis { Link = (ushort)Marshal.ReadInt16(c, 6), Min = min, Max = max };
                        }
                }
                finally { Marshal.FreeHGlobal(vc); }

                dev.buttonMap = kind == PadKind.PlayStation ? PlayStationButtons : kind == PadKind.Xbox ? XboxButtons : GenericButtons;
                // Xbox pads over Bluetooth put both triggers on one Z axis (see BUILD_NOTES §7a).
                dev.sharedTriggerAxis = kind == PadKind.Xbox && dev.axes.ContainsKey(0x32) && !dev.axes.ContainsKey(0x35);
                return dev;
            }

            // Measured on an Xbox Wireless Controller over Bluetooth.
            static readonly Dictionary<int, string> XboxButtons = new Dictionary<int, string>
            { [1] = "a", [2] = "b", [3] = "x", [4] = "y", [5] = "lb", [6] = "rb", [7] = "view", [8] = "menu", [9] = "lclick", [10] = "rclick" };

            // DirectInput layout of DualShock 4 / DualSense (best effort, not yet tested on a real pad).
            static readonly Dictionary<int, string> PlayStationButtons = new Dictionary<int, string>
            { [1] = "x", [2] = "a", [3] = "b", [4] = "y", [5] = "lb", [6] = "rb", [9] = "view", [10] = "menu", [11] = "lclick", [12] = "rclick" };

            static readonly Dictionary<int, string> GenericButtons = new Dictionary<int, string>
            { [1] = "a", [2] = "b", [3] = "x", [4] = "y", [5] = "lb", [6] = "rb", [7] = "view", [8] = "menu", [9] = "lclick", [10] = "rclick" };

            public PadState Parse(IntPtr report, uint length)
            {
                var s = new PadState();
                int n = usageBuffer.Length;
                if (Native.HidP_GetUsages(0, 9, 0, usageBuffer, ref n, preparsed, report, length) == Native.HIDP_STATUS_SUCCESS)
                    for (int i = 0; i < n; i++)
                        if (buttonMap.TryGetValue(usageBuffer[i], out string id)) s.Pressed.Add(id);

                double Stick(ushort usage)
                {
                    if (!axes.TryGetValue(usage, out Axis a) || a.Max <= a.Min) return 0;
                    if (Native.HidP_GetUsageValue(0, 1, a.Link, usage, out uint raw, preparsed, report, length) != Native.HIDP_STATUS_SUCCESS) return 0;
                    return Math.Max(-1, Math.Min(1, 2.0 * (raw - a.Min) / (a.Max - a.Min) - 1));
                }
                double Trigger(ushort usage)
                {
                    if (!axes.TryGetValue(usage, out Axis a) || a.Max <= a.Min) return 0;
                    if (Native.HidP_GetUsageValue(0, 1, a.Link, usage, out uint raw, preparsed, report, length) != Native.HIDP_STATUS_SUCCESS) return 0;
                    return Math.Max(0, Math.Min(1, (double)(raw - a.Min) / (a.Max - a.Min)));
                }

                if (Info.Kind == PadKind.PlayStation)
                {
                    s.LX = Stick(0x30); s.LY = Stick(0x31); s.RX = Stick(0x32); s.RY = Stick(0x35);
                    s.LT = Trigger(0x33); s.RT = Trigger(0x34);
                }
                else
                {
                    s.LX = Stick(0x30); s.LY = Stick(0x31); s.RX = Stick(0x33); s.RY = Stick(0x34);
                    if (sharedTriggerAxis)
                    {
                        double z = Stick(0x32); // LT pulls towards +1, RT towards -1
                        s.LT = Math.Max(0, z);
                        s.RT = Math.Max(0, -z);
                    }
                    else
                    {
                        s.LT = Trigger(0x32);
                        s.RT = Trigger(0x35);
                    }
                }

                if (axes.TryGetValue(0x39, out Axis hat) &&
                    Native.HidP_GetUsageValue(0, 1, hat.Link, 0x39, out uint h, preparsed, report, length) == Native.HIDP_STATUS_SUCCESS)
                {
                    long range = hat.Max - hat.Min + 1, index = (long)h - hat.Min;
                    if (h >= hat.Min && h <= hat.Max && range == 8)
                    {
                        // 0 up, 1 up-right, 2 right, 3 down-right, 4 down, 5 down-left, 6 left, 7 up-left
                        if (index == 7 || index == 0 || index == 1) s.Pressed.Add("dpadUp");
                        if (index >= 1 && index <= 3) s.Pressed.Add("dpadRight");
                        if (index >= 3 && index <= 5) s.Pressed.Add("dpadDown");
                        if (index >= 5 && index <= 7) s.Pressed.Add("dpadLeft");
                    }
                }
                return s;
            }

            public void Free()
            {
                if (preparsed != IntPtr.Zero) { Marshal.FreeHGlobal(preparsed); preparsed = IntPtr.Zero; }
            }

            static string DeviceName(IntPtr handle)
            {
                uint size = 0;
                Native.GetRawInputDeviceName(handle, Native.RIDI_DEVICENAME, null, ref size);
                var sb = new StringBuilder((int)size + 1);
                Native.GetRawInputDeviceName(handle, Native.RIDI_DEVICENAME, sb, ref size);
                return sb.ToString();
            }

            static void ParseIds(string path, out ushort vid, out ushort pid)
            {
                vid = HexAfter(path, "VID_") ?? HexAfter(path, "VID&02") ?? 0;
                pid = HexAfter(path, "PID_") ?? HexAfter(path, "PID&") ?? 0;
            }

            static ushort? HexAfter(string s, string marker)
            {
                int i = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (i < 0 || i + marker.Length + 4 > s.Length) return null;
                return ushort.TryParse(s.Substring(i + marker.Length, 4), System.Globalization.NumberStyles.HexNumber, null, out ushort v) ? v : (ushort?)null;
            }

            static string ProductString(string path)
            {
                IntPtr h = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h == Native.INVALID_HANDLE_VALUE) return null;
                try
                {
                    var sb = new StringBuilder(128);
                    return Native.HidD_GetProductString(h, sb, sb.Capacity * 2) ? sb.ToString() : null;
                }
                finally { Native.CloseHandle(h); }
            }
        }
    }
}
