using System;
using System.Runtime.InteropServices;

namespace DesktopGamepad.Engine
{
    /// <summary>
    /// Battery level of a Bluetooth controller, from the same Windows device property that Settings shows.
    /// USB controllers and the Xbox wireless adapter do not report a percentage here (the UI then shows no number).
    /// </summary>
    public static class Battery
    {
        [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll")] static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, IntPtr buffer, uint size, out uint required, uint flags);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        static readonly DEVPROPKEY FriendlyName = new DEVPROPKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
        static readonly DEVPROPKEY BatteryLevel = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };

        /// <summary>Percentage of the first connected Bluetooth game controller, or null.</summary>
        public static int? Read()
        {
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, "BTHLE", IntPtr.Zero, 0x2 /* PRESENT */ | 0x4 /* ALLCLASSES */);
            if (set == new IntPtr(-1)) return null;
            IntPtr buf = Marshal.AllocHGlobal(1024);
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    var nameKey = FriendlyName;
                    if (!SetupDiGetDevicePropertyW(set, ref data, ref nameKey, out _, buf, 1024, out _, 0)) continue;
                    string name = Marshal.PtrToStringUni(buf) ?? "";
                    if (name.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var levelKey = BatteryLevel;
                    if (!SetupDiGetDevicePropertyW(set, ref data, ref levelKey, out _, buf, 1024, out _, 0)) continue;
                    return Marshal.ReadByte(buf);
                }
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
                SetupDiDestroyDeviceInfoList(set);
            }
        }
    }
}
