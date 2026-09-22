using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using DesktopGamepad.Engine;

namespace DesktopGamepad
{
    /// <summary>
    /// Owns the running app: settings, the controller engine, tray icon and settings window, and answers the window's
    /// messages. Everything in this class runs on the UI thread; engine events are posted back to it.
    /// </summary>
    sealed class AppHost : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        readonly NotifyIcon tray;
        readonly SynchronizationContext ui;
        readonly EventWaitHandle showSignal;
        readonly ControllerReader reader;
        readonly InputEngine engine;
        KeyboardAssist keyboard; // only while a controller is connected
        readonly CursorHider cursor;
        readonly WinEventProc foregroundProc;
        readonly IntPtr foregroundHook;
        readonly System.Windows.Forms.Timer recheck;
        readonly System.Threading.Timer batteryTimer;

        Settings settings;
        SettingsWindow window;
        ControllerInfo controller;
        int? battery;
        bool warned20, warned10;
        bool steamConflict;
        /// <summary>Clicks were actually seen landing twice. Stays set until the user dismisses the warning.</summary>
        bool doubleInputSeen;
        string liveProfile;
        bool adminWindow;
        volatile bool buttonKeyboard; // A opens the keyboard in the app in front (see Reconfigure)
        IntPtr buttonKeyboardWindow;
        string lastStatusJson;
        AppIdentity browserApp, playerApp;
        DateTime appsReadAt = DateTime.MinValue;

        public AppHost(bool openWindow)
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            settings = SettingsStore.Load();
            // Keep the registry entry in step with the setting, but only once first-run setup has been finished.
            if (settings.FirstRunDone) ApplyStartWithWindows(settings.StartWithWindows);

            tray = new NotifyIcon { Icon = AppIcon.Create(16), Text = "Desktop Gamepad: controller mouse and keyboard", Visible = true, ContextMenuStrip = BuildTrayMenu() };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

            // Another launch of DesktopGamepad.exe asks this copy to show its window.
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowWindowEventName);
            new Thread(() => { while (true) { showSignal.WaitOne(); ui.Post(_ => ShowWindow(), null); } }) { IsBackground = true, Name = "show signal" }.Start();
            // "DesktopGamepad.exe --quit" (the installer and uninstaller) asks this copy to close cleanly.
            var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
            new Thread(() => { quitSignal.WaitOne(); ui.Post(_ => Quit(), null); }) { IsBackground = true, Name = "quit signal" }.Start();

            // ---- Controller engine ----
            engine = new InputEngine();
            engine.ToggleMouseModeRequested += () => ui.Post(_ => ToggleMouseMode(), null);
            engine.ShowKeyboardRequested += () => ThreadPool.QueueUserWorkItem(_ => TouchKeyboard.Toggle());
            engine.InputPressed += id => ui.Post(_ => window?.Post(JsonSerializer.Serialize(new { type = "input", id }, SettingsStore.Json)), null);
            engine.BeforeKeys = () => keyboard?.BeforeKeyAction();
            engine.AfterKeys = () => keyboard?.AfterKeyAction();
            engine.InputPressed += id =>
            {
                if (!buttonKeyboard || keyboard == null) return;
                if (id == "a") keyboard.ButtonA();
                else if (id == "b") keyboard.ButtonB();
            };
            engine.InputReleased += id => ui.Post(_ => window?.Post(JsonSerializer.Serialize(new { type = "inputUp", id }, SettingsStore.Json)), null);
            engine.NavRequested += key => ui.Post(_ =>
            {
                if (window == null) return;
                window.FocusPage();
                window.Post(JsonSerializer.Serialize(new { type = "nav", key }, SettingsStore.Json));
            }, null);

            cursor = new CursorHider();

            reader = new ControllerReader();
            reader.Report += (info, state) => { engine.Submit(state); cursor.OnControllerUsed(); };
            reader.Connected += info => ui.Post(_ => OnControllerConnected(info), null);
            reader.Disconnected += () => { engine.ControllerGone(); ui.Post(_ => OnControllerDisconnected(), null); };

            // Re-evaluate when the front window changes, and once a second for fullscreen and elevation changes.
            foregroundProc = (h, evt, hwnd, idObject, idChild, thread, time) => Reconfigure();
            foregroundHook = SetWinEventHook(3 /* EVENT_SYSTEM_FOREGROUND */, 3, IntPtr.Zero, foregroundProc, 0, 0, 0);
            recheck = new System.Windows.Forms.Timer { Interval = 1000 };
            recheck.Tick += (s, e) => { if (controller != null) Reconfigure(); };
            recheck.Start();

            batteryTimer = new System.Threading.Timer(_ => CheckBattery(), null, Timeout.Infinite, Timeout.Infinite);

            Reconfigure();
            if (openWindow || !settings.FirstRunDone) ShowWindow();
            else TrimMemorySoon();
        }

        ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();
            var open = new ToolStripMenuItem("Open Desktop Gamepad", null, (s, e) => ShowWindow()) { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
            var mouse = new ToolStripMenuItem("Mouse mode", null, (s, e) => ToggleMouseMode());
            var profiles = new ToolStripMenuItem("Default profile");
            var quit = new ToolStripMenuItem("Quit Desktop Gamepad", null, (s, e) => Quit());
            menu.Items.AddRange(new ToolStripItem[] { open, new ToolStripSeparator(), mouse, profiles, new ToolStripSeparator(), quit });
            menu.Opening += (s, e) =>
            {
                mouse.Checked = settings.MouseMode == "on";
                profiles.DropDownItems.Clear();
                foreach (var name in settings.ProfileOrder)
                {
                    string p = name;
                    profiles.DropDownItems.Add(new ToolStripMenuItem(p, null, (a, b) => { settings.ActiveProfile = p; SaveAndApply(); }) { Checked = settings.ActiveProfile == p });
                }
            };
            return menu;
        }

        void ShowWindow()
        {
            if (window == null || window.IsDisposed)
            {
                window = new SettingsWindow(OnPageMessage);
                window.PageReady += PushState;
                window.FormClosed += (s, e) => { window = null; lastStatusJson = null; TrimMemorySoon(); };
            }
            window.BringToFront(restore: true);
        }

        // ---- Memory ----

        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

        /// <summary>
        /// Desktop Gamepad spends nearly all its time in the tray, so memory used only for starting up or for the window is handed
        /// back: once shortly after start, and each time the window closes (after its browser control has shut down).
        /// </summary>
        void TrimMemorySoon()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += (s, e) =>
            {
                timer.Dispose();
                if (window != null) return; // the window was opened again meanwhile
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                // Lets Windows take back pages Desktop Gamepad is not using; anything still needed comes back on first use.
                SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            };
            timer.Start();
        }

        void Quit()
        {
            cursor.Dispose(); // put the user's cursor back before anything else
            tray.Visible = false;
            window?.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                recheck.Dispose();
                batteryTimer.Dispose();
                if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
                reader.Dispose();
                engine.Dispose();
                keyboard?.Dispose();
                cursor.Dispose();
                tray.Dispose();
                showSignal.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---- Settings ----

        void SaveAndApply()
        {
            try { SettingsStore.Save(settings); }
            catch (Exception ex) { Log.Write("settings could not be saved: " + ex.Message); }
            Reconfigure();
            PushState();
        }

        void ToggleMouseMode()
        {
            settings.MouseMode = settings.MouseMode == "on" ? "off" : "on";
            SaveAndApply();
            Notifications.Show(tray, settings.MouseMode == "on" ? "Mouse mode on" : "Mouse mode off",
                settings.MouseMode == "on" ? "The controller moves the cursor and clicks." : "Turn it back on from the tray icon, or press Menu + View on the controller.",
                settings.Notify.Sound);
        }

        static void ApplyStartWithWindows(bool on)
        {
#if DEBUG
            // Test builds never register themselves to start with Windows.
            Log.Write("start with Windows would be set to " + on + " (skipped in a Debug build)");
            return;
#else
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) key.SetValue("DesktopGamepad", "\"" + Application.ExecutablePath + "\" --background");
                    else if (key.GetValue("DesktopGamepad") != null) key.DeleteValue("DesktopGamepad");
                }
            }
            catch (Exception ex) { Log.Write("start with Windows could not be changed: " + ex.Message); }
#endif
        }

        // ---- Controller ----

        /// <summary>
        /// Text box detection and the mouse watcher only matter while a controller is connected, and they load large
        /// Windows accessibility libraries, so they start with the controller and stop when it goes.
        /// </summary>
        void StartControllerHelpers()
        {
            if (keyboard != null) return;
            keyboard = new KeyboardAssist();
            keyboard.DoubleInputDetected += () => ui.Post(_ => OnDoubleInput(), null);
            keyboard.RealMouseMove = cursor.OnRealMouseMove;
        }

        void StopControllerHelpers()
        {
            if (keyboard == null) return;
            keyboard.Dispose();
            keyboard = null;
            if (window == null) TrimMemorySoon();
        }

        void OnControllerConnected(ControllerInfo info)
        {
            controller = info;
            StartControllerHelpers();
            battery = null;
            batteryTimer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(5)); // the level appears a moment after connecting
            CheckSteamConflict();
            Reconfigure();
            PushStatus();
        }

        void OnControllerDisconnected()
        {
            controller = null;
            battery = null;
            batteryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Reconfigure(); // puts the cursor back first if it was hidden
            StopControllerHelpers();
            PushStatus();
        }

        void CheckBattery()
        {
            int? level = Battery.Read();
            ui.Post(_ =>
            {
                if (controller == null) return;
                CheckSteamConflict(); // Steam's layout can change while Desktop Gamepad runs
                battery = level;
                if (level.HasValue)
                {
                    int v = level.Value;
                    if (v > 30) warned20 = warned10 = false;
                    if (settings.Notify.Battery)
                    {
                        if (v <= 10 && !warned10) { warned10 = warned20 = true; Notifications.Show(tray, "Controller battery very low", "Your controller is at " + v + "%. Charge it or change the batteries soon.", settings.Notify.Sound); }
                        else if (v <= 20 && !warned20) { warned20 = true; Notifications.Show(tray, "Controller battery low", "Your controller is at " + v + "%.", settings.Notify.Sound); }
                    }
                }
                PushStatus();
            }, null);
        }

        void OnDoubleInput()
        {
            Log.Write("double input detected: another app is also acting on the controller");
            doubleInputSeen = true;
            SetConflict(true);
        }

        /// <summary>Turns the conflict warning on or off. Each time it turns on, a notification is shown once.</summary>
        void SetConflict(bool on)
        {
            if (on == steamConflict) return;
            steamConflict = on;
            // Kept vague on purpose: Desktop Gamepad cannot tell which app it is, or whether it clicks, moves the cursor or both.
            if (on) Notifications.Show(tray, "Controller input conflict", "Another application is also responding to your controller. Open Desktop Gamepad for details.", settings.Notify.Sound);
            PushStatus();
        }

        /// <summary>
        /// Steam running with a Desktop Layout that moves the mouse or clicks: both apps would act on the same press.
        /// Checked before anything clicks, so the warning can name Steam (BUILD_NOTES §6, signal 1). Clears itself again
        /// once Steam no longer controls the mouse, unless clicks were actually seen landing twice.
        /// </summary>
        void CheckSteamConflict()
        {
            bool steamControlsMouse = SteamDesktopLayoutControlsMouse();
            SetConflict(steamControlsMouse || doubleInputSeen);
        }

        /// <summary>
        /// Which Desktop Layouts (app 413080) Steam actually loaded last, read from Steam's own controller log. Layout files
        /// on disk are not enough: a user who switches the layout off still has the old file, while Steam loads empty.vdf.
        /// Steam can also list one controller several times (Controller 0, Controller 1, ...), each slot with its own
        /// layout, and every slot acts on the desktop. Removing the template for one slot can leave another one scrolling.
        /// </summary>
        static bool SteamDesktopLayoutControlsMouse()
        {
            try
            {
                var steamProcesses = System.Diagnostics.Process.GetProcessesByName("steam");
                if (steamProcesses.Length == 0) return false;
                string steamExe = SystemInfo.SteamExePath();
                if (steamExe == null) return false;
                string logFile = Path.Combine(Path.GetDirectoryName(steamExe), @"logs\controller_ui.txt");
                if (!File.Exists(logFile)) return false;

                // Only this Steam session counts: slots from before Steam restarted no longer exist, but stay in the log.
                DateTime sessionStart = DateTime.MinValue;
                try { sessionStart = steamProcesses.Min(p => p.StartTime).AddSeconds(-5); } catch { }

                // The layout loaded last for each controller slot.
                var lastBySlot = new System.Collections.Generic.Dictionary<string, string>();
                foreach (var line in ReadTail(logFile, 256 * 1024).Split('\n'))
                {
                    int at = line.IndexOf("Loaded Config for ", StringComparison.Ordinal);
                    if (at < 0) continue;
                    // Lines start with a local timestamp: [2026-09-15 13:36:19]
                    if (line.Length > 21 && line[0] == '[' &&
                        DateTime.TryParseExact(line.Substring(1, 19), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeLocal, out DateTime loggedAt) &&
                        loggedAt < sessionStart) continue;
                    int slot = line.IndexOf("App ID 413080, Controller ", at, StringComparison.Ordinal);
                    if (slot < 0) continue;
                    int colon = line.IndexOf(':', slot);
                    if (colon < 0) continue;
                    lastBySlot[line.Substring(slot, colon - slot)] = line.Substring(colon + 1).Trim();
                }

                foreach (string loaded in lastBySlot.Values)
                {
                    if (loaded.Length == 0 || loaded.EndsWith("empty.vdf", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = loaded.Replace('/', '\\');
                    if (!Path.IsPathRooted(path)) path = Path.Combine(Path.GetDirectoryName(steamExe), path);
                    if (!File.Exists(path)) continue;
                    string text = File.ReadAllText(path);
                    if (text.Contains("joystick_mouse") || text.Contains("mouse_button") || text.Contains("mouse_wheel") || text.Contains("\"scrollwheel\""))
                    {
                        Log.Write("a Steam desktop layout acts as a mouse or scroll wheel: " + path);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Write("Steam check failed: " + ex.Message);
                return false;
            }
        }

        static string ReadTail(string file, int maxBytes)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0, stream.Length - maxBytes);
                stream.Seek(start, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        // ---- Which profile applies now ----

        void RefreshDefaultApps()
        {
            if ((DateTime.UtcNow - appsReadAt).TotalSeconds < 30) return;
            appsReadAt = DateTime.UtcNow;
            browserApp = SystemInfo.DefaultBrowser();
            playerApp = SystemInfo.DefaultMediaPlayer();
        }

        static bool SamePath(string a, string b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>The rule for the app in front: a profile name or "off".</summary>
        /// <summary>The last rule came from a row on the Apps page (the browser, media player, Steam, or an added app).</summary>
        bool ruleFromRow;

        /// <summary>Start and Windows' search window, which move their own highlight with the controller.</summary>
        static bool IsStartMenu(string path) =>
            path.EndsWith(@"\StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
            (path.EndsWith(@"\SearchHost.exe", StringComparison.OrdinalIgnoreCase) && path.IndexOf(@"\SystemApps\", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Task View (Win+Tab). Windows Explorer owns it and moves its highlight with the controller itself.</summary>
        static bool IsTaskView(ForegroundInfo fg) =>
            string.Equals(fg.WindowClass, "XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fg.Title, "Task View", StringComparison.OrdinalIgnoreCase);

        string RuleFor(ForegroundInfo fg, out bool leaveCursorAlone)
        {
            ruleFromRow = false;
            leaveCursorAlone = false;
            string path = fg.Path;
            if (path == null) return settings.ActiveProfile;
            // Windows apps with their own controller support: the Xbox app and the Microsoft Store.
            if (path.IndexOf(@"\Microsoft.GamingApp_", StringComparison.OrdinalIgnoreCase) >= 0) return "off";
            if (path.IndexOf(@"\Microsoft.WindowsStore_", StringComparison.OrdinalIgnoreCase) >= 0) return "off";
            if (path.EndsWith(@"\ImmersiveControlPanel\SystemSettings.exe", StringComparison.OrdinalIgnoreCase)) return "off"; // Windows Settings
            // Start, its search and Task View move their own highlight with the controller, so Windows drives them. In Start
            // that means no search with the controller: Windows' navigation never reaches the search box, and once anything
            // puts the focus there (a real mouse click included) Windows stops navigating Start at all. Letting the cursor
            // work there instead was worse: A then opened the tile under the pointer and the highlighted tile at once.
            if (IsStartMenu(path) || IsTaskView(fg)) return "off";
            string file = Path.GetFileName(path);
            // Steam Big Picture Mode has its own controller support, windowed or fullscreen, and hides the cursor itself.
            bool steamWindow = file.Equals("steam.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase);
            if (steamWindow && fg.Title != null && fg.Title.IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                leaveCursorAlone = true;
                return "off";
            }
            RefreshDefaultApps();
            foreach (var app in settings.Apps)
            {
                bool match =
                    (app.Kind == "browser" && SamePath(browserApp?.Path, path)) ||
                    (app.Kind == "player" && SamePath(playerApp?.Path, path)) ||
                    (app.Kind == "steam" && (file.Equals("steam.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase))) ||
                    (app.Kind == "custom" && (SamePath(app.Path, path) || string.Equals(Path.GetFileName(app.Path ?? ""), file, StringComparison.OrdinalIgnoreCase)));
                if (match)
                {
                    ruleFromRow = true;
                    return app.Rule == "own" ? "off" : app.Rule;
                }
            }
            // Games have their own controller support in a window as much as fullscreen, and handle the cursor themselves.
            // A row on the Apps page (above) still wins, for anything detected by mistake.
            if (Games.IsGame(path, fg.ProcessId))
            {
                leaveCursorAlone = true;
                return "off";
            }
            return settings.ActiveProfile;
        }

        void Reconfigure()
        {
            var fg = Foreground.Read();
            // Only the settings window itself counts. Windows' own dialogs that Desktop Gamepad opens (the file picker behind
            // "Add an app") are ordinary windows, so the controller works there as a mouse and keyboard.
            bool ownWindow = window != null && !window.IsDisposed && window.IsHandleCreated && Native.GetForegroundWindow() == window.Handle;
            bool ownProcess = fg.Path != null && SamePath(fg.Path, Application.ExecutablePath);
            bool leaveCursorAlone = false;
            string rule = ownProcess ? settings.ActiveProfile : RuleFor(fg, out leaveCursorAlone);
            string profile = settings.Profiles.ContainsKey(rule) ? rule : settings.ActiveProfile;

            // In Desktop Gamepad's own window the controller moves between its controls instead of moving the cursor.
            // Full screen steps aside for apps without a row of their own (an unknown fullscreen app is probably a game).
            // Apps with a row keep their profile: a browser playing a video full screen is not a game, and buttons like
            // Enter for SponsorBlock's skip must keep working there.
            if (ownProcess) ruleFromRow = false;
            bool fullscreenStepsAside = fg.Fullscreen && !ruleFromRow;
            bool active = controller != null && settings.MouseMode == "on" && !fullscreenStepsAside && rule != "off" && !ownWindow;
            engine.Configure(new EngineConfig
            {
                Active = active,
                Navigate = controller != null && ownWindow,
                Map = settings.Profiles[profile],
                CursorSpeed = settings.CursorSpeed,
                ScrollSpeed = settings.ScrollSpeed,
                Kind = controller?.Kind ?? PadKind.Xbox
            });
            if (keyboard != null) keyboard.Enabled = active && settings.Keyboard;
            // Apps set to Off that navigate with the controller themselves (Stremio, TV-style web apps) never ask Windows for
            // the keyboard, so there A on a highlighted text box opens it. Not in the Xbox app, Big Picture or games: they
            // bring up their own keyboard.
            bool xboxApp = fg.Path != null &&
                (fg.Path.IndexOf(@"\Microsoft.GamingApp_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fg.Path.IndexOf(@"\Microsoft.WindowsStore_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 IsStartMenu(fg.Path)) || IsTaskView(fg); // these bring up their own keyboard, or have no text to type in
            buttonKeyboard = controller != null && settings.Keyboard && rule == "off" && !ownProcess && !leaveCursorAlone && !xboxApp;
            if (keyboard != null && fg.Window != buttonKeyboardWindow)
            {
                buttonKeyboardWindow = fg.Window;
                keyboard.ForgetButtonKeyboard();
            }
            // In apps set to Off and in Desktop Gamepad's own window, the cursor gets out of the way once the controller is used
            // there. Full screen or not: the Xbox app on a TV fills the screen and still shows the cursor. Games and Big
            // Picture Mode (leaveCursorAlone) hide it themselves, so they are left alone.
            cursor.SetWanted(controller != null && ((rule == "off" && !leaveCursorAlone) || (ownWindow && !fg.Fullscreen)));

            liveProfile = profile;
            adminWindow = active && fg.Elevated;
            PushStatus();
        }

        // ---- Messages between the page and the app ----

        object Status() => new
        {
            controller = controller == null ? null : new { connected = true, name = controller.Name, connection = controller.Connection, battery, kind = controller.KindId },
            steamConflict,
            adminWindow,
            liveProfile
        };

        void PushStatus()
        {
            if (window == null) return;
            string json = JsonSerializer.Serialize(new { type = "status", status = Status() }, SettingsStore.Json);
            if (json == lastStatusJson) return;
            lastStatusJson = json;
            window.Post(json);
        }

        /// <summary>Sends everything the page shows: the saved settings, facts about this PC, and the live status.</summary>
        void PushState()
        {
            if (window == null) return;
            appsReadAt = DateTime.MinValue;
            RefreshDefaultApps();
            var message = new
            {
                type = "state",
                settings,
                system = new { defaultBrowser = browserApp, defaultPlayer = playerApp },
                status = Status()
            };
            window.Post(JsonSerializer.Serialize(message, SettingsStore.Json));
        }

        void OnPageMessage(string json)
        {
            JsonNode msg;
            try { msg = JsonNode.Parse(json); } catch { return; }
            switch ((string)msg?["type"])
            {
                case "ready":
                    PushState();
                    break;

                case "saveSettings":
                    try
                    {
                        var incoming = msg["settings"].Deserialize<Settings>(SettingsStore.Json);
                        bool startupChanged = incoming.StartWithWindows != settings.StartWithWindows;
                        bool setupFinished = incoming.FirstRunDone && !settings.FirstRunDone;
                        settings = SettingsStore.Normalize(incoming);
                        try { SettingsStore.Save(settings); } catch (Exception ex) { Log.Write("settings could not be saved: " + ex.Message); }
                        if (settings.FirstRunDone && (startupChanged || setupFinished)) ApplyStartWithWindows(settings.StartWithWindows);
                        Reconfigure();
                    }
                    catch (Exception ex) { Log.Write("page sent settings that could not be read: " + ex.Message); }
                    break;

                case "resetProfiles":
                {
                    // Settings > Reset profiles to default: the built-in profiles and mappings come back; other settings stay.
                    var fresh = Defaults.Create();
                    settings.Profiles = fresh.Profiles;
                    settings.ProfileOrder = fresh.ProfileOrder;
                    settings.ActiveProfile = fresh.ActiveProfile;
                    foreach (var rule in settings.Apps)
                        if (rule.Rule != "off")
                            rule.Rule = rule.Kind == "browser" ? "Browser" : rule.Kind == "player" ? "Media" : fresh.ProfileOrder[0];
                    settings = SettingsStore.Normalize(settings);
                    Log.Write("profiles reset to default");
                    SaveAndApply();
                    break;
                }

                case "pickApp":
                    PickApp();
                    break;

                case "showKeyboard": // a text box was chosen with the controller
                    ThreadPool.QueueUserWorkItem(_ => TouchKeyboard.Open());
                    break;

                case "hideKeyboard":
                    ThreadPool.QueueUserWorkItem(_ => TouchKeyboard.Close());
                    break;

                case "dismissConflict":
                    doubleInputSeen = false;
                    steamConflict = false;
                    PushStatus();
                    break;
            }
        }

        /// <summary>The Windows open dialog, filtered to apps and shortcuts (BUILD_NOTES §1).</summary>
        void PickApp()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Choose an app",
                Filter = "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk",
                DereferenceLinks = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            })
            {
                if (dialog.ShowDialog(window) != DialogResult.OK) return;
                var picked = SystemInfo.FromPickedFile(dialog.FileName);
                window?.Post(JsonSerializer.Serialize(new { type = "appPicked", name = picked.Name, path = picked.Path }, SettingsStore.Json));
            }
        }
    }
}
