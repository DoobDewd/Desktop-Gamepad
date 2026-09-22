using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace DesktopGamepad.Engine
{
    /// <summary>
    /// Watches Desktop Gamepad's own clicks (made with the controller). Clicking a text box opens the touch keyboard; moving focus
    /// out of a text box closes it again. Also notices a second click from another app landing on top of each of Desktop Gamepad's
    /// clicks, which means another app is acting on the controller too.
    /// </summary>
    public sealed class KeyboardAssist : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }
        delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;

        /// <summary>Set by the app host: keyboard automation is switched on and a controller is connected.</summary>
        public volatile bool Enabled;
        /// <summary>Raised (on a worker thread) when clicks land twice.</summary>
        public event Action DoubleInputDetected;
        /// <summary>Called for every real (not injected) mouse move; returning true swallows the move.</summary>
        public Func<bool> RealMouseMove;

        readonly LowLevelMouseProc hookProc;
        readonly AutoResetEvent clicked = new AutoResetEvent(false);
        readonly Thread hookThread, checkThread;
        IntPtr hook;
        volatile bool stopping;
        volatile int clickX, clickY;
        volatile bool openedByUs;
        /// <summary>How many opens in a row did not bring up a keyboard, which points at a stuck TabTip.exe.</summary>
        int misses;
        volatile int openedAt;
        IntPtr openedForWindow;
        volatile int openedForProcess;
        long lastOwnDown, lastOtherDown;
        int doubleCount;

        public KeyboardAssist()
        {
            hookProc = HookProc;
            hookThread = new Thread(HookLoop) { IsBackground = true, Name = "mouse hook" };
            hookThread.Start();
            checkThread = new Thread(CheckLoop) { IsBackground = true, Name = "text box check" };
            checkThread.Start();
        }

        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        volatile uint hookThreadId;

        /// <summary>Stops the hook and focus tracking and ends both threads (the controller went away, or Desktop Gamepad quits).</summary>
        public void Dispose()
        {
            stopping = true;
            Enabled = false;
            clicked.Set();
            uint thread = hookThreadId;
            if (thread != 0) PostThreadMessage(thread, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        void HookLoop()
        {
            hookThreadId = GetCurrentThreadId();
            hook = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
            while (!stopping && Native.GetMessage(out Native.MSG msg, IntPtr.Zero, 0, 0) > 0) Native.DispatchMessage(ref msg);
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        }

        IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int message = (int)wParam;
                if (message == 0x0200 /* WM_MOUSEMOVE */ && RealMouseMove != null)
                {
                    var move = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    bool injected = (move.Flags & 0x1 /* LLMHF_INJECTED */) != 0;
                    if (!injected && RealMouseMove()) return new IntPtr(1);
                }
                if (message == 0x020A /* WM_MOUSEWHEEL */ || message == 0x020E /* WM_MOUSEHWHEEL */)
                {
                    var wheel = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    NoteWheel(wheel.ExtraInfo == Native.AppMarker, (wheel.Flags & 0x1 /* LLMHF_INJECTED */) != 0);
                }
                if (message == WM_LBUTTONDOWN || message == WM_LBUTTONUP)
                {
                    var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    bool ours = info.ExtraInfo == Native.AppMarker;
                    if (message == WM_LBUTTONDOWN) NoteDown(ours);
                    if (message == WM_LBUTTONUP && ours && Enabled)
                    {
                        clickX = info.X;
                        clickY = info.Y;
                        clicked.Set();
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        /// <summary>Two left-button presses within 60 ms, one Desktop Gamepad's and one not, several times in a row.</summary>
        void NoteDown(bool ours)
        {
            long now = Environment.TickCount;
            if (ours) lastOwnDown = now; else lastOtherDown = now;
            if (Math.Abs(lastOwnDown - lastOtherDown) <= 60 && lastOwnDown != 0 && lastOtherDown != 0)
            {
                if (++doubleCount >= 3)
                {
                    doubleCount = 0;
                    Log.Write("conflict: clicks landing twice (another app clicks within 60 ms of this app's clicks)");
                    ThreadPool.QueueUserWorkItem(_ => DoubleInputDetected?.Invoke());
                }
                lastOwnDown = lastOtherDown = 0;
            }
        }

        long lastOwnWheel;
        int otherWheelCount;
        long otherWheelSince;

        /// <summary>
        /// Scroll steps from another program arriving while Desktop Gamepad is scrolling: another app is turning the stick
        /// into a mouse wheel too, which makes scrolling jump. Only injected steps count, so a real mouse wheel never does.
        /// </summary>
        void NoteWheel(bool ours, bool injected)
        {
            long now = Environment.TickCount;
            if (ours) { lastOwnWheel = now; return; }
            if (!injected || lastOwnWheel == 0 || now - lastOwnWheel > 100) return;
            if (now - otherWheelSince > 3000) { otherWheelSince = now; otherWheelCount = 0; }
            if (++otherWheelCount >= 5)
            {
                otherWheelCount = 0;
                otherWheelSince = 0;
                Log.Write("conflict: another app is scrolling too (5 injected wheel steps within 100 ms of this app's own)");
                ThreadPool.QueueUserWorkItem(_ => DoubleInputDetected?.Invoke());
            }
        }

        void CheckLoop()
        {
            while (!stopping)
            {
                clicked.WaitOne();
                if (stopping) break;
                bool fromButton = buttonRequest; // A pressed in an app with its own controller navigation
                buttonRequest = false;
                bool fromKey = keyRequest;       // a key shortcut was sent
                keyRequest = false;
                int[] before = focusBeforeKey;
                Thread.Sleep(80); // let focus move to what was clicked
                try
                {
                    if ((!Enabled && !fromButton) || TouchKeyboard.IsOpen()) continue;
                    // Focus reaches accessibility clients late in some apps (browsers and Electron apps can take several
                    // hundred milliseconds), so keep looking for a while before deciding the click was not on a text box.
                    AutomationElement box = null;
                    for (int i = 0; i < 10 && box == null && (Enabled || fromButton); i++)
                    {
                        if (i > 0) Thread.Sleep(60);
                        box = fromButton || fromKey ? FocusedTextBox() : ClickedTextBox(clickX, clickY);
                        // A key only counts when it moved focus: a text box that already had focus stays as it is.
                        if (fromKey && box != null && before != null && Automation.Compare(box.GetRuntimeId(), before)) box = null;
                    }
                    if (box == null && (fromButton || fromKey)) continue;
                    if (box == null)
                    {
                        // Recorded so a missed text box can be told apart from a correct "not a text box" decision.
                        var focused = AutomationElement.FocusedElement;
                        if (focused != null && IsTextInput(focused))
                            Log.Write("keyboard not opened: click at " + clickX + "," + clickY + " is outside the focused " + Describe(focused) + " at " + focused.Current.BoundingRectangle);
                        continue;
                    }
                    if ((!Enabled && !fromButton) || TouchKeyboard.IsOpen()) continue;

                    Log.Write("touch keyboard opened for " + Describe(box) + (fromButton ? " (A in an app with its own controller navigation)" : fromKey ? " (a key shortcut moved focus there)" : ""));
                    if (fromButton) buttonOpened = true;
                    openedForWindow = Native.GetForegroundWindow();
                    try { openedForProcess = box.Current.ProcessId; } catch { openedForProcess = 0; }
                    openedAt = Environment.TickCount;
                    TouchKeyboard.Toggle();
                    openedByUs = true;
                    Thread.Sleep(450); // let it slide in, so a quick second click does not toggle it straight back off
                    // Windows' own touch keyboard can get stuck: it then answers every toggle without showing anything,
                    // until TabTip.exe is restarted. Only recorded for now, to learn how often it happens.
                    if (!TouchKeyboard.IsOpen())
                    {
                        misses++;
                        Log.Write("the touch keyboard did not appear (" + misses + " in a row); " + TouchKeyboard.DescribeWindows());
                    }
                    else misses = 0;
                    // Tried and dropped (2026-09-15): checking a strip of the screen to see whether the keyboard really
                    // appeared. On a 1280x720 TV the strip read exactly the same with the keyboard up, so the check would
                    // have "corrected" every open by closing it again.
                }
                catch (Exception ex) { Log.Write("text box check failed: " + ex.Message); }
            }
        }

        /// <summary>
        /// Not used any more, kept out of the way on purpose: closing the keyboard when focus moved looked sensible, but
        /// browsers, Electron apps and Windows' own window-switching all move focus while the keyboard appears, and each
        /// time the keyboard was taken away the moment it arrived. The prototype never closed it either. The user closes
        /// the keyboard, or Windows does.
        /// </summary>
        void UnusedOnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
        {
            if (!openedByUs || !Enabled) return;
            // Opening the keyboard moves focus about, and apps shuffle focus inside their own window while it appears.
            // Closing on that took the keyboard away the moment it arrived, so give it a moment, and only close when
            // focus has really left the window the text box lives in.
            if (Environment.TickCount - openedAt < 2000) return;
            try
            {
                var element = sender as AutomationElement;
                if (element != null && IsTextInput(element)) return;
                // Still in the app the text box belongs to: browsers and Electron apps move focus around their page as the
                // keyboard appears, and those page elements have no window of their own, so the app is the test.
                if (openedForWindow != IntPtr.Zero && Native.GetForegroundWindow() == openedForWindow) return;
                if (element != null && SameApp(element, openedForProcess)) return;
                // Windows' own switching windows flash past whenever the keyboard itself appears; they are not the user
                // leaving the text box. ("ForegroundStaging" showed up every single time the keyboard was taken away.)
                if (element != null && IsShellTransition(element)) return;
                // Focus on the keyboard itself keeps it open.
                if (element != null)
                {
                    int pid = element.Current.ProcessId;
                    string name = System.Diagnostics.Process.GetProcessById(pid).ProcessName;
                    if (name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) || name.Equals("TabTip", StringComparison.OrdinalIgnoreCase)) return;
                }
                openedByUs = false;
                Log.Write("closing the touch keyboard: focus moved to " + (element == null ? "nothing" : Describe(element)));
                TouchKeyboard.Toggle(); // this app opened it, so toggling closes it; asking Windows whether it is open is unreliable here
            }
            catch { }
        }

        static bool SameApp(AutomationElement el, int processId)
        {
            try { return processId != 0 && el.Current.ProcessId == processId; }
            catch { return false; }
        }

        /// <summary>Windows' own window-switching helpers, which take focus for a moment and give it straight back.</summary>
        static bool IsShellTransition(AutomationElement el)
        {
            try
            {
                string cls = el.Current.ClassName ?? "";
                return cls == "ForegroundStaging" || cls == "XamlExplorerHostIslandWindow" || cls == "MultitaskingViewFrame"
                    || cls == "Windows.UI.Core.CoreWindow" || cls == "Shell_TrayWnd";
            }
            catch { return false; }
        }

        static bool IsTextInput(AutomationElement el)
        {
            var cur = el.Current;
            if (!cur.IsEnabled) return false;
            var type = cur.ControlType;
            if (type != ControlType.Edit && type != ControlType.ComboBox && type != ControlType.Document) return false;
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object p)) return !((ValuePattern)p).Current.IsReadOnly;
            return type == ControlType.Edit;
        }

        /// <summary>
        /// The text box the click belongs to. The focused text box counts when the click landed on or near it: focus alone
        /// is not enough, because windows move focus to a text box by themselves (the file dialog focuses "File name" each
        /// time a folder opens). Otherwise the element under the pointer counts, which catches boxes that report something
        /// else as focused, such as the YouTube search box handing focus to its suggestion list.
        /// </summary>
        static AutomationElement ClickedTextBox(int x, int y)
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null && IsTextInput(focused) && Contains(focused, x, y)) return focused;
            return TextInputAt(x, y);
        }

        // ---- Apps with their own controller navigation (set to Off, like Stremio) ----
        // They move a highlight with the controller but never ask Windows for the keyboard, so A on a highlighted text box
        // opens it. Once open, A types on the keyboard, so further A presses are left alone until B (which closes the
        // keyboard) or a different window comes to the front.
        volatile bool buttonRequest, buttonOpened;

        public void ButtonA()
        {
            if (buttonOpened) return;
            buttonRequest = true;
            clicked.Set();
        }

        public void ButtonB() { buttonOpened = false; }

        // ---- Key shortcuts that jump into a text box ("/" on YouTube, Ctrl+L in a browser) ----
        // Only when the key moved focus into a text box: arrow keys pressed while already typing must never toggle it.
        volatile int[] focusBeforeKey;
        volatile bool keyRequest;

        public void BeforeKeyAction()
        {
            if (!Enabled) return;
            try { focusBeforeKey = AutomationElement.FocusedElement?.GetRuntimeId(); } catch { focusBeforeKey = null; }
        }

        public void AfterKeyAction()
        {
            if (!Enabled) return;
            keyRequest = true;
            clicked.Set();
        }

        public void ForgetButtonKeyboard() { buttonOpened = false; }

        static AutomationElement FocusedTextBox()
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                return focused != null && IsTextInput(focused) ? focused : null;
            }
            catch { return null; }
        }

        /// <summary>A text box under this point, looking a little way up the tree from whatever is directly there.</summary>
        static AutomationElement TextInputAt(int x, int y)
        {
            try
            {
                var el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
                for (int i = 0; el != null && i < 3; i++)
                {
                    if (IsTextInput(el)) return el;
                    el = TreeWalker.ControlViewWalker.GetParent(el);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Whether a point is on an element, with a small margin: a text box's frame and padding often sit just outside
        /// the editable area that accessibility reports, and a click there still puts the caret in the box.
        /// </summary>
        static bool Contains(AutomationElement el, int x, int y)
        {
            // Generous, because a text box usually sits inside a bigger clickable area (a search bar, a chat composer),
            // and on a big TV those paddings are many pixels wide.
            const int Margin = 48;
            var r = el.Current.BoundingRectangle;
            return !r.IsEmpty && x >= r.Left - Margin && x < r.Right + Margin && y >= r.Top - Margin && y < r.Bottom + Margin;
        }

        static string Describe(AutomationElement el)
        {
            try
            {
                var c = el.Current;
                return c.ControlType.ProgrammaticName + " \"" + c.Name + "\" class " + c.ClassName;
            }
            catch { return "an element that went away"; }
        }
    }
}
