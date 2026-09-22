using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopGamepad.Engine
{
    public sealed class ForegroundInfo
    {
        public IntPtr Window;
        public string Path;
        /// <summary>The process that owns the window (for Store apps, the app inside the frame).</summary>
        public uint ProcessId;
        public string WindowClass;
        public string Title;
        /// <summary>Covers its whole screen and is not simply maximized: games and fullscreen video.</summary>
        public bool Fullscreen;
        /// <summary>Runs as administrator while Desktop Gamepad does not, so it ignores Desktop Gamepad's input.</summary>
        public bool Elevated;
    }

    /// <summary>Facts about the window in front.</summary>
    public static class Foreground
    {
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int size, out int returned);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

        static readonly bool SelfElevated = IsElevated(GetCurrentProcess());

        public static ForegroundInfo Read()
        {
            var info = new ForegroundInfo { Window = Native.GetForegroundWindow() };
            if (info.Window == IntPtr.Zero) return info;

            var sb = new StringBuilder(256);
            Native.GetClassName(info.Window, sb, sb.Capacity);
            info.WindowClass = sb.ToString();
            sb.Clear();
            Native.GetWindowText(info.Window, sb, sb.Capacity);
            info.Title = sb.ToString();

            Native.GetWindowThreadProcessId(info.Window, out uint pid);
            info.ProcessId = pid;
            info.Path = ExePath(pid, out bool elevated);
            info.Elevated = elevated && !SelfElevated;

            // Store apps sit inside a frame owned by ApplicationFrameHost; the real app owns a child window.
            if (info.Path != null && info.Path.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                uint childPid = 0;
                Native.EnumChildWindows(info.Window, (child, l) =>
                {
                    Native.GetWindowThreadProcessId(child, out uint p);
                    if (p != pid) { childPid = p; return false; }
                    return true;
                }, IntPtr.Zero);
                if (childPid != 0) { info.Path = ExePath(childPid, out _) ?? info.Path; info.ProcessId = childPid; }
            }

            info.Fullscreen = IsFullscreen(info.Window, info.WindowClass);
            return info;
        }

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);

        static bool IsFullscreen(IntPtr hwnd, string cls)
        {
            if (Native.IsZoomed(hwnd) || cls == "Progman" || cls == "WorkerW") return false; // maximized windows and the desktop are not
            // A window with a title bar is an ordinary window, however big it is: on a 1280x720 TV even Windows Settings
            // covers the whole screen. Games and fullscreen video drop the title bar.
            const int GWL_STYLE = -16, WS_CAPTION = 0x00C00000;
            if ((GetWindowLong(hwnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (!Native.GetWindowRect(hwnd, out Native.RECT r) || !Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, 2), ref mi)) return false;
            return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }

        static string ExePath(uint pid, out bool elevated)
        {
            elevated = false;
            IntPtr h = Native.OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                elevated = IsElevated(h);
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(h); }
        }

        static bool IsElevated(IntPtr process)
        {
            if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out IntPtr token))
                return Marshal.GetLastWin32Error() == 5; // access denied: a normal app cannot look into an elevated one
            try { return GetTokenInformation(token, 20 /* TokenElevation */, out int elevatedFlag, 4, out _) && elevatedFlag != 0; }
            finally { Native.CloseHandle(token); }
        }
    }
}
