using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopGamepad.Engine
{
    /// <summary>Windows functions the engine uses.</summary>
    static class Native
    {
        // ---- Messages and windows ----
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
        public const uint WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_CLOSE = 0x0010;
        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        // ---- Raw Input ----
        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }

        [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll")] public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint command, IntPtr data, ref uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
        public static extern uint GetRawInputDeviceName(IntPtr hDevice, uint command, StringBuilder data, ref uint size);
        public const uint RIDEV_INPUTSINK = 0x100, RIDEV_DEVNOTIFY = 0x2000;
        public const uint RID_INPUT = 0x10000003, RIDI_PREPARSEDDATA = 0x20000005, RIDI_DEVICENAME = 0x20000007;
        public const int RIM_TYPEHID = 2;
        public const int GIDC_ARRIVAL = 1, GIDC_REMOVAL = 2;

        // ---- HID ----
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int reportType, IntPtr valueCaps, ref ushort length, IntPtr preparsed);
        [DllImport("hid.dll")] public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, ushort[] usages, ref int length, IntPtr preparsed, IntPtr report, uint reportLength);
        [DllImport("hid.dll")] public static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value, IntPtr preparsed, IntPtr report, uint reportLength);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] public static extern bool HidD_GetProductString(IntPtr device, StringBuilder buffer, int bufferLength);
        public const int HIDP_STATUS_SUCCESS = 0x00110000;
        public const int SIZEOF_HIDP_VALUE_CAPS = 72;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // ---- Input injection ----
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        public const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10,
            MOUSEEVENTF_MIDDLEDOWN = 0x20, MOUSEEVENTF_MIDDLEUP = 0x40, MOUSEEVENTF_WHEEL = 0x800, MOUSEEVENTF_HWHEEL = 0x1000,
            MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

        /// <summary>Marks every event Desktop Gamepad injects, so its own clicks can be told apart from other apps' clicks.</summary>
        public static readonly IntPtr AppMarker = new IntPtr(0x44475044); // "DGPD"

        // ---- Cursor and screen ----
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        // ---- Timer resolution (only raised while the cursor or page is moving) ----
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);
    }
}
