using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DesktopGamepad.Engine
{
    /// <summary>What the engine should do right now. Replaced as a whole whenever settings or the front app change.</summary>
    public sealed class EngineConfig
    {
        /// <summary>False when mouse mode is off, the front app's rule is "off", or it is fullscreen.</summary>
        public bool Active;
        /// <summary>True while Desktop Gamepad's own window is in front: the controller moves between its controls instead.</summary>
        public bool Navigate;
        public Dictionary<string, Mapping> Map = new Dictionary<string, Mapping>();
        public int CursorSpeed = 7;
        public int ScrollSpeed = 8;
        public PadKind Kind = PadKind.Xbox;
    }

    /// <summary>
    /// Turns controller reports into mouse and keyboard input on its own thread: button actions (with the second layer),
    /// the cursor, smooth scrolling, key repeat, and Menu + View to turn mouse mode on or off.
    /// </summary>
    public sealed class InputEngine : IDisposable
    {
        public event Action ToggleMouseModeRequested;
        public event Action ShowKeyboardRequested;
        /// <summary>An input id was pressed (for the setup screen's "try it" step).</summary>
        public event Action<string> InputPressed;
        /// <summary>An input id was let go (so the settings window can follow a held layer button).</summary>
        public event Action<string> InputReleased;
        /// <summary>A navigation step for Desktop Gamepad's own window: up, down, left, right, select, back, prevPage, nextPage or menu.</summary>
        public event Action<string> NavRequested;
        /// <summary>
        /// Called on the engine thread just before and just after a key action is sent (not for its repeats), so a shortcut
        /// that moves focus into a text box, like "/" on YouTube, can bring up the touch keyboard.
        /// </summary>
        public Action BeforeKeys, AfterKeys;

        readonly ConcurrentQueue<PadState> reports = new ConcurrentQueue<PadState>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread thread;
        volatile EngineConfig config = new EngineConfig();
        volatile bool stopping;

        // Engine thread state.
        PadState last = new PadState();
        readonly Dictionary<string, Held> held = new Dictionary<string, Held>();
        bool chordConsumed;
        bool ltDown, rtDown;
        double cursorCarryX, cursorCarryY, wheelCarryX, wheelCarryY;
        bool fastTimer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastTickSeconds;
        long lastWheelAt;
        // Scrolling sends a wheel step at most this often. Browsers (Firefox especially) animate each wheel step, and a
        // steady ~60 steps a second scrolls smoothly, while twice as many, half as big, looked jittery. Matches the
        // TouchKeyboard prototype, whose scrolling felt smooth.
        const int WheelEveryMs = 15;

        const double TriggerDown = 0.5, TriggerUp = 0.35;
        const int RepeatDelayMs = 500, RepeatEveryMs = 60, TickMs = 8; // 500 ms before repeating, like a keyboard

        // Navigation in Desktop Gamepad's own window: the D-pad or left stick, repeating while held.
        string navDir;
        long navNext;
        const int NavRepeatDelayMs = 400, NavRepeatEveryMs = 130;
        const double NavStickPush = 0.5;
        static readonly Dictionary<string, string> NavButtons = new Dictionary<string, string>
        {
            ["a"] = "select", ["b"] = "back", ["lb"] = "prevPage", ["rb"] = "nextPage", ["menu"] = "menu"
        };

        sealed class Held
        {
            public ParsedAction Action;
            public string Name;
            public long Since;
            public long NextRepeat;
        }

        // A high-resolution waitable timer keeps ticks at a steady 8 ms. Plain waits follow the system timer, which Windows 11
        // coarsens to about 15.6 ms for apps with no visible window (the tray), and that makes the cursor and scrolling stutter.
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);
        const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2, TIMER_ALL_ACCESS = 0x1F0003;

        readonly WaitHandle tickTimer;   // null where Windows has no high-resolution timers
        readonly WaitHandle[] tickWaits; // the timer, or new input, whichever comes first

        public InputEngine()
        {
            SafeWaitHandle timer = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            if (!timer.IsInvalid)
            {
                tickTimer = new TimerHandle(timer);
                tickWaits = new[] { wake, tickTimer };
            }
            thread = new Thread(Run) { IsBackground = true, Name = "input engine", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        sealed class TimerHandle : WaitHandle
        {
            public TimerHandle(SafeWaitHandle handle) { SafeWaitHandle = handle; }
        }

        /// <summary>Waits one tick, or less if new controller input arrives.</summary>
        void WaitOneTick()
        {
            long due = -TickMs * 10000L; // relative time, in 100 ns units
            if (tickTimer != null && SetWaitableTimer(tickTimer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                WaitHandle.WaitAny(tickWaits);
            else
                wake.WaitOne(TickMs);
        }

        public void Configure(EngineConfig next)
        {
            var previous = config;
            config = next;
            if (previous.Active && !next.Active) wake.Set(); // release held keys promptly
        }

        public void Submit(PadState state)
        {
            reports.Enqueue(state);
            wake.Set();
        }

        /// <summary>The controller went away: let go of everything it was holding.</summary>
        public void ControllerGone()
        {
            reports.Enqueue(new PadState());
            wake.Set();
        }

        public void Dispose()
        {
            stopping = true;
            wake.Set();
        }

        void Run()
        {
            while (!stopping)
            {
                bool moving = Moving();
                if (moving || held.Count > 0 || navDir != null) WaitOneTick();
                else wake.WaitOne(); // nothing to animate: sleep until the controller sends something
                SetFastTimer(moving);

                var cfg = config;
                while (reports.TryDequeue(out PadState state)) Step(state, cfg);
                if (!cfg.Active) ReleaseAll();

                long now = clock.ElapsedMilliseconds;
                // Exact time since the last tick. Whole milliseconds made every step's size wobble (8 ms, then 9 ms).
                double nowSeconds = clock.Elapsed.TotalSeconds;
                double seconds = Math.Min(0.05, nowSeconds - lastTickSeconds);
                lastTickSeconds = nowSeconds;
                if (cfg.Navigate) UpdateNav(now); else navDir = null;
                if (cfg.Active)
                {
                    MoveCursor(cfg, seconds);
                    ScrollPage(cfg, seconds);
                    RepeatHeldKeys(now);
                }
            }
            ReleaseAll();
            SetFastTimer(false);
        }

        bool Moving()
        {
            var cfg = config;
            if (!cfg.Active) return false;
            string leftAction = ActionFor(cfg, "lstick"), rightAction = ActionFor(cfg, "rstick");
            return (StickAction(leftAction) != ActionKind.None && Magnitude(last.LX, last.LY) > DeadZone(cfg, left: true))
                || (StickAction(rightAction) != ActionKind.None && Magnitude(last.RX, last.RY) > DeadZone(cfg, left: false));
        }

        static ActionKind StickAction(string name)
        {
            var a = ParsedAction.Parse(name);
            return a != null && (a.Kind == ActionKind.MoveCursor || a.Kind == ActionKind.Scroll) ? a.Kind : ActionKind.None;
        }

        void SetFastTimer(bool on)
        {
            if (on == fastTimer) return;
            fastTimer = on;
            if (on) Native.timeBeginPeriod(1); else Native.timeEndPeriod(1);
        }

        // ---- Navigation in Desktop Gamepad's own window ----

        void UpdateNav(long now)
        {
            string dir = NavDirection();
            if (dir != navDir)
            {
                navDir = dir;
                if (dir != null) { NavRequested?.Invoke(dir); navNext = now + NavRepeatDelayMs; }
            }
            else if (dir != null && now >= navNext)
            {
                NavRequested?.Invoke(dir);
                navNext = now + NavRepeatEveryMs;
            }
        }

        string NavDirection()
        {
            if (last.Pressed.Contains("dpadUp")) return "up";
            if (last.Pressed.Contains("dpadDown")) return "down";
            if (last.Pressed.Contains("dpadLeft")) return "left";
            if (last.Pressed.Contains("dpadRight")) return "right";
            double x = last.LX, y = last.LY; // stick down is positive
            if (Magnitude(x, y) < NavStickPush) return null;
            return Math.Abs(x) > Math.Abs(y) ? (x > 0 ? "right" : "left") : (y > 0 ? "down" : "up");
        }

        // ---- Buttons ----

        void Step(PadState state, EngineConfig cfg)
        {
            var before = new HashSet<string>(last.Pressed);
            if (ltDown) before.Add("lt");
            if (rtDown) before.Add("rt");

            // Triggers count as pressed past halfway, and stay pressed until they drop back below 35%.
            ltDown = ltDown ? state.LT > TriggerUp : state.LT >= TriggerDown;
            rtDown = rtDown ? state.RT > TriggerUp : state.RT >= TriggerDown;
            var now = new HashSet<string>(state.Pressed);
            if (ltDown) now.Add("lt");
            if (rtDown) now.Add("rt");

            foreach (var id in now) if (!before.Contains(id)) InputPressed?.Invoke(id);
            foreach (var id in before) if (!now.Contains(id)) InputReleased?.Invoke(id);
            if (cfg.Navigate)
                foreach (var id in now)
                    if (!before.Contains(id) && NavButtons.TryGetValue(id, out string navKey)) NavRequested?.Invoke(navKey);

            // Menu + View together always turns mouse mode on or off, even while it is off.
            if (now.Contains("menu") && now.Contains("view") && !(before.Contains("menu") && before.Contains("view")))
            {
                chordConsumed = true;
                Release("menu"); Release("view");
                ToggleMouseModeRequested?.Invoke();
            }
            if (chordConsumed && !now.Contains("menu") && !now.Contains("view")) chordConsumed = false;

            if (cfg.Active)
            {
                foreach (var id in before) if (!now.Contains(id)) Release(id);
                foreach (var id in now)
                {
                    if (before.Contains(id) || held.ContainsKey(id)) continue;
                    if (chordConsumed && (id == "menu" || id == "view")) continue;
                    Press(id, cfg);
                }
            }
            last = state;
        }

        bool LayerHeld(EngineConfig cfg)
        {
            foreach (var pair in held) if (pair.Value.Action.Kind == ActionKind.Layer) return true;
            return false;
        }

        /// <summary>The action for an input: its second-layer action while the layer is held, falling back to its normal one.</summary>
        string ActionFor(EngineConfig cfg, string id, bool? layer = null)
        {
            if (!cfg.Map.TryGetValue(id, out Mapping m) || m == null) return null;
            bool useLayer = layer ?? LayerHeld(cfg);
            if (useLayer && m.Layer != null && m.Layer.TryGetValue("tap", out string layered) && !string.IsNullOrEmpty(layered)) return layered;
            return m.Base != null && m.Base.TryGetValue("tap", out string normal) ? normal : null;
        }

        void Press(string id, EngineConfig cfg)
        {
            // The layer button's own action is read from the normal layer, so holding it never changes what it does.
            string name = ActionFor(cfg, id, layer: IsLayerButton(cfg, id) ? false : (bool?)null);
            var action = ParsedAction.Parse(name);
            if (action == null || action.Kind == ActionKind.None || action.Kind == ActionKind.MoveCursor || action.Kind == ActionKind.Scroll) return;

            long now = clock.ElapsedMilliseconds;
            held[id] = new Held { Action = action, Name = name, Since = now, NextRepeat = now + RepeatDelayMs };
            switch (action.Kind)
            {
                case ActionKind.MouseButton: Output.MouseButton(action.Button, true); break;
                case ActionKind.Keys:
                    BeforeKeys?.Invoke();
                    Output.Keys(action, name, true);
                    AfterKeys?.Invoke();
                    break;
                case ActionKind.ShowKeyboard: ShowKeyboardRequested?.Invoke(); break;
                case ActionKind.ToggleMouseMode: ToggleMouseModeRequested?.Invoke(); break;
            }
        }

        bool IsLayerButton(EngineConfig cfg, string id) =>
            cfg.Map.TryGetValue(id, out Mapping m) && m?.Base != null && m.Base.TryGetValue("tap", out string a) && a == Defaults.LayerAction;

        void Release(string id)
        {
            if (!held.TryGetValue(id, out Held h)) return;
            held.Remove(id);
            if (h.Action.Kind == ActionKind.MouseButton) Output.MouseButton(h.Action.Button, false);
            else if (h.Action.Kind == ActionKind.Keys) Output.Keys(h.Action, h.Name, false);
        }

        void ReleaseAll()
        {
            foreach (var id in new List<string>(held.Keys)) Release(id);
        }

        void RepeatHeldKeys(long now)
        {
            foreach (var h in held.Values)
            {
                if (h.Action.Kind != ActionKind.Keys || !h.Action.Repeats || now < h.NextRepeat) continue;
                Output.RepeatMainKey(h.Action, h.Name);
                h.NextRepeat = now + RepeatEveryMs;
            }
        }

        // ---- Cursor and scrolling ----

        static double Magnitude(double x, double y) => Math.Sqrt(x * x + y * y);

        /// <summary>Built-in dead zones per controller model (BUILD_NOTES §3). There is no setting for these.</summary>
        static double DeadZone(EngineConfig cfg, bool left)
        {
            switch (cfg.Kind)
            {
                case PadKind.Xbox: return left ? 0.24 : 0.27;
                case PadKind.PlayStation: return 0.10;
                default: return 0.12;
            }
        }

        /// <summary>0 inside the dead zone, rising to 1 at a full push.</summary>
        static double Push(double magnitude, double deadZone) =>
            magnitude <= deadZone ? 0 : Math.Min(1, (magnitude - deadZone) / (1 - deadZone));

        void MoveCursor(EngineConfig cfg, double seconds)
        {
            if (!StickFor(cfg, ActionKind.MoveCursor, out double x, out double y, out bool left)) { cursorCarryX = cursorCarryY = 0; return; }
            double mag = Magnitude(x, y), push = Push(mag, DeadZone(cfg, left));
            if (push <= 0) { cursorCarryX = cursorCarryY = 0; return; }

            // Speed 1..10 maps to 600..4200 pixels per second at a full push. The curve (power 1.6) keeps small pushes
            // controllable without making medium pushes feel slow; a half push gives about a third of full speed.
            // Tuned against Steam's joystick mouse, which the first version (300..2550 px/s, squared) felt slower than.
            double maxSpeed = 600 + (cfg.CursorSpeed - 1) * 400;
            bool precise = false;
            foreach (var h in held.Values) if (h.Action.Kind == ActionKind.Precision) precise = true;
            double speed = maxSpeed * Math.Pow(push, 1.6) * (precise ? 0.25 : 1);

            cursorCarryX += x / mag * speed * seconds;
            cursorCarryY += y / mag * speed * seconds;
            int dx = (int)cursorCarryX, dy = (int)cursorCarryY;
            if (dx == 0 && dy == 0) return;
            cursorCarryX -= dx; cursorCarryY -= dy;

            if (!Native.GetCursorPos(out Native.POINT p)) return;
            int vx = Native.GetSystemMetrics(76), vy = Native.GetSystemMetrics(77), vw = Native.GetSystemMetrics(78), vh = Native.GetSystemMetrics(79);
            int nx = Math.Max(vx, Math.Min(vx + vw - 1, p.X + dx));
            int ny = Math.Max(vy, Math.Min(vy + vh - 1, p.Y + dy));
            Output.MoveTo(nx, ny);
        }

        void ScrollPage(EngineConfig cfg, double seconds)
        {
            if (!StickFor(cfg, ActionKind.Scroll, out double x, out double y, out _)) { wheelCarryX = wheelCarryY = 0; return; }
            const double ScrollDeadZone = 0.15, Slowest = 40;
            double fastest = 400 + (cfg.ScrollSpeed - 1) * (2400 - 400) / 9.0; // speed 1..10 -> 400..2400 wheel units per second

            double Rate(double v)
            {
                double push = Push(Math.Abs(v), ScrollDeadZone);
                return push <= 0 ? 0 : Math.Sign(v) * (Slowest + (fastest - Slowest) * push * push);
            }

            // Mostly-vertical pushes only scroll vertically, and the other way round.
            if (Math.Abs(x) < Math.Abs(y) * 0.5) x = 0;
            else if (Math.Abs(y) < Math.Abs(x) * 0.5) y = 0;

            wheelCarryY += -Rate(y) * seconds; // stick down is positive, wheel down is negative
            wheelCarryX += Rate(x) * seconds;
            long nowMs = clock.ElapsedMilliseconds;
            if (nowMs - lastWheelAt >= WheelEveryMs)
            {
                int stepY = (int)wheelCarryY, stepX = (int)wheelCarryX;
                if (stepY != 0) { Output.Wheel(stepY, false); wheelCarryY -= stepY; }
                if (stepX != 0) { Output.Wheel(stepX, true); wheelCarryX -= stepX; }
                if (stepY != 0 || stepX != 0) lastWheelAt = nowMs;
            }
            if (Rate(y) == 0) wheelCarryY = 0;
            if (Rate(x) == 0) wheelCarryX = 0;
        }

        /// <summary>Finds the stick whose action is the given kind, and its current position.</summary>
        bool StickFor(EngineConfig cfg, ActionKind kind, out double x, out double y, out bool left)
        {
            x = y = 0; left = true;
            if (StickAction(ActionFor(cfg, "lstick")) == kind) { x = last.LX; y = last.LY; left = true; return true; }
            if (StickAction(ActionFor(cfg, "rstick")) == kind) { x = last.RX; y = last.RY; left = false; return true; }
            return false;
        }
    }
}
