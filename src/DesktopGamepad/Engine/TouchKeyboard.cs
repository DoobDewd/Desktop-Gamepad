using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DesktopGamepad.Engine
{
    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITipInvocation { void Toggle(IntPtr hwnd); }

    [ComImport, Guid("5752238B-24F0-495A-82F1-2FD593056796"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFrameworkInputPane
    {
        void Advise([MarshalAs(UnmanagedType.IUnknown)] object window, IntPtr handler, out int cookie);
        void AdviseWithHWND(IntPtr hwnd, IntPtr handler, out int cookie);
        void Unadvise(int cookie);
        void Location(out Native.RECT location);
    }

    /// <summary>Opens, closes and checks the Windows touch keyboard. Its look is Windows' own.</summary>
    public static class TouchKeyboard
    {
        static readonly Guid TipClsid = new Guid("4ce576fa-83dc-4f88-951c-9d0782b4e376");
        static readonly Guid InputPaneClsid = new Guid("D5120AA3-46BA-44C5-822D-CA8092C1FC72");
        const string TabTipPath = @"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe";
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Native.RECT rect);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        /// <summary>
        /// Whether the touch keyboard is on screen. Windows' own "input pane" answer is wrong on some setups (it reported
        /// nothing on a 1280x720 TV as the only screen), so the keyboard's window counts as a second opinion. Getting this
        /// wrong makes every click toggle the keyboard straight back off.
        /// </summary>
        // Both COM objects below only answer properly on an STA thread. Desktop Gamepad's worker threads are MTA, where
        // the input pane reported "closed" with the keyboard plainly on screen, so every click toggled it away again.
        // One long-lived STA thread does this work.
        static readonly object staGate = new object();
        static readonly AutoResetEvent hasWork = new AutoResetEvent(false), workDone = new AutoResetEvent(false);
        static Thread staThread;
        static Func<bool> pendingWork;
        static bool pendingResult;

        static bool OnSta(Func<bool> work)
        {
            lock (staGate)
            {
                if (staThread == null)
                {
                    staThread = new Thread(() =>
                    {
                        while (true)
                        {
                            hasWork.WaitOne();
                            try { pendingResult = pendingWork(); } catch { pendingResult = false; }
                            workDone.Set();
                        }
                    })
                    { IsBackground = true, Name = "touch keyboard" };
                    staThread.SetApartmentState(ApartmentState.STA);
                    staThread.Start();
                }
                pendingWork = work;
                hasWork.Set();
                return workDone.WaitOne(5000) && pendingResult;
            }
        }

        public static bool IsOpen()
        {
            return OnSta(InputPaneShowing) || KeyboardWindowShowing();
        }

        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

        /// <summary>
        /// A rough fingerprint of a band across the lower part of the screen, where the touch keyboard sits. Windows gives
        /// an app with no window of its own no reliable way to ask whether the keyboard is showing, so opening it is
        /// checked by looking: if this fingerprint does not change, the keyboard did not appear.
        /// </summary>
        public static long LowerScreenFingerprint()
        {
            try
            {
                int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
                var area = new System.Drawing.Rectangle(width / 4, height - height / 5, width / 2, 60);
                using (var bmp = new System.Drawing.Bitmap(area.Width, area.Height))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                        g.CopyFromScreen(area.Location, System.Drawing.Point.Empty, area.Size);
                    long sum = 0;
                    for (int y = 0; y < area.Height; y += 6)
                        for (int x = 0; x < area.Width; x += 12)
                        {
                            var c = bmp.GetPixel(x, y);
                            sum += c.R + c.G * 3 + c.B * 7;
                        }
                    return sum;
                }
            }
            catch { return 0; }
        }

        static bool InputPaneShowing()
        {
            try
            {
                var pane = (IFrameworkInputPane)Activator.CreateInstance(Type.GetTypeFromCLSID(InputPaneClsid));
                try
                {
                    pane.Location(out Native.RECT r);
                    return r.Right > r.Left && r.Bottom > r.Top;
                }
                finally { Marshal.ReleaseComObject(pane); }
            }
            catch { return false; }
        }

        delegate bool EnumProc(IntPtr hwnd, IntPtr param);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        /// <summary>Every window of the keyboard's own processes, for the log when the keyboard cannot be found.</summary>
        public static string DescribeWindows()
        {
            var found = new System.Text.StringBuilder();
            try
            {
                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    string owner;
                    try { owner = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
                    if (!owner.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) && !owner.Equals("TabTip", StringComparison.OrdinalIgnoreCase)) return true;

                    var cls = new System.Text.StringBuilder(128); GetClassName(hwnd, cls, cls.Capacity);
                    var title = new System.Text.StringBuilder(128); GetWindowText(hwnd, title, title.Capacity);
                    DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int));
                    GetWindowRect(hwnd, out Native.RECT r);
                    found.Append(string.Format("[{0} class={1} title=\"{2}\" visible={3} cloaked={4} rect={5},{6},{7},{8}] ",
                        owner, cls, title, IsWindowVisible(hwnd), cloaked, r.Left, r.Top, r.Right, r.Bottom));
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { found.Append("(listing failed: " + ex.Message + ")"); }
            return found.Length == 0 ? "no touch keyboard windows at all" : found.ToString();
        }

        static bool KeyboardWindowShowing()
        {
            try
            {
                foreach (var window in new[]
                {
                    new { Class = "Windows.UI.Core.CoreWindow", Title = "Microsoft Text Input Application" }, // Windows 11
                    new { Class = "IPTip_Main_Window", Title = (string)null }                                 // older builds
                })
                {
                    IntPtr hwnd = FindWindow(window.Class, window.Title);
                    if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) continue;
                    // A hidden keyboard stays "visible" but cloaked, so ask the window manager as well.
                    if (DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
                    if (GetWindowRect(hwnd, out Native.RECT r) && r.Right > r.Left && r.Bottom > r.Top) return true;
                }
            }
            catch { }
            return false;
        }

        public static void Open() { if (!IsOpen()) Toggle(); }
        public static void Close() { if (IsOpen()) Toggle(); }

        public static void Toggle()
        {
            OnSta(() =>
            {
                if (TryToggle()) return true;
                // The keyboard's host process is not running yet: start it, then try again for a few seconds.
                try { Process.Start(TabTipPath); } catch { return false; }
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(150);
                    if (TryToggle()) return true;
                }
                return false;
            });
        }

        static bool TryToggle()
        {
            try
            {
                var tip = (ITipInvocation)Activator.CreateInstance(Type.GetTypeFromCLSID(TipClsid));
                try { tip.Toggle(GetDesktopWindow()); }
                finally { Marshal.ReleaseComObject(tip); }
                return true;
            }
            catch { return false; }
        }
    }
}
