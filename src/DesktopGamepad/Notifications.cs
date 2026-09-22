using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DesktopGamepad
{
    /// <summary>
    /// Windows notifications with Desktop Gamepad's icon as the picture. Windows 11 only shows a picture when it is passed as the
    /// balloon icon (NIIF_USER), so this adds a short-lived tray entry of its own for each message.
    /// </summary>
    public static class Notifications
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize; public IntPtr hWnd; public int uID; public int uFlags; public int uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState; public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hWnd);

        static int nextId = 100;

        public static void Show(string title, string text, bool sound)
        {
            Log.Write("notification: " + title + " - " + text);
            var thread = new Thread(() =>
            {
                IntPtr owner = CreateWindowEx(0, "Static", "DesktopGamepadNotification", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                using (var icon = AppIcon.Create(64))
                {
                    var data = new NOTIFYICONDATA
                    {
                        cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                        hWnd = owner,
                        uID = Interlocked.Increment(ref nextId),
                        uFlags = 0x2 | 0x4 | 0x10,                          // NIF_ICON | NIF_TIP | NIF_INFO
                        hIcon = icon.Handle,
                        szTip = "Desktop Gamepad",
                        szInfo = text,
                        szInfoTitle = title,
                        dwInfoFlags = 0x4 | 0x20 | (sound ? 0 : 0x10),     // NIIF_USER | NIIF_LARGE_ICON | NIIF_NOSOUND
                        hBalloonIcon = icon.Handle
                    };
                    try
                    {
                        if (!Shell_NotifyIcon(0 /* NIM_ADD */, ref data)) Log.Write("notification could not be shown");
                        Thread.Sleep(12000);
                        Shell_NotifyIcon(2 /* NIM_DELETE */, ref data);
                    }
                    finally { if (owner != IntPtr.Zero) DestroyWindow(owner); }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
