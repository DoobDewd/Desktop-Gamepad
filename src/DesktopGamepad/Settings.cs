using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DesktopGamepad
{
    /// <summary>What one controller input does: its action on the base layer and while the layer button is held.</summary>
    public sealed class Mapping
    {
        // Keyed by press type. Only "tap" exists (see BUILD_NOTES §5), kept as a map to match the UI's data shape.
        public Dictionary<string, string> Base { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Layer { get; set; } = new Dictionary<string, string>();

        public Mapping Clone() => new Mapping
        {
            Base = new Dictionary<string, string>(Base),
            Layer = new Dictionary<string, string>(Layer)
        };
    }

    /// <summary>A row on the App profiles page.</summary>
    public sealed class AppRule
    {
        /// <summary>"browser", "player", "xbox", "steam" or a generated id for apps the user added.</summary>
        public string Id { get; set; }
        /// <summary>"browser", "player", "xbox", "steam" or "custom".</summary>
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        /// <summary>A profile name or "off". (Older settings may say "own", which now means "off".)</summary>
        public string Rule { get; set; }
    }

    public sealed class NotifySettings
    {
        public bool Battery { get; set; } = true;
        public bool Sound { get; set; } = true;
    }

    public sealed class Settings
    {
        public int Version { get; set; } = 1;
        public bool FirstRunDone { get; set; }
        public bool StartWithWindows { get; set; } = true;
        /// <summary>"on" or "off".</summary>
        public string MouseMode { get; set; } = "on";
        public bool Keyboard { get; set; } = true;
        public int CursorSpeed { get; set; } = 7;
        public int ScrollSpeed { get; set; } = 8;
        public string ActiveProfile { get; set; } = "Desktop";
        public List<string> ProfileOrder { get; set; } = new List<string>();
        public Dictionary<string, Dictionary<string, Mapping>> Profiles { get; set; } = new Dictionary<string, Dictionary<string, Mapping>>();
        public List<AppRule> Apps { get; set; } = new List<AppRule>();
        public NotifySettings Notify { get; set; } = new NotifySettings();
    }

    /// <summary>The built-in profiles and their seeded mappings (BUILD_NOTES §4).</summary>
    public static class Defaults
    {
        public static readonly string[] InputIds =
        {
            "lt", "lb", "rt", "rb", "lstick", "lclick", "rstick", "rclick",
            "dpadUp", "dpadDown", "dpadLeft", "dpadRight", "view", "menu", "y", "x", "b", "a"
        };

        public const string LayerAction = "Hold for second layer";

        static Dictionary<string, Mapping> Common()
        {
            var m = InputIds.ToDictionary(id => id, id => new Mapping());
            void Set(string id, string baseAction, string layerAction = null)
            {
                if (baseAction != null) m[id].Base["tap"] = baseAction;
                if (layerAction != null) m[id].Layer["tap"] = layerAction;
            }
            Set("lstick", "Move cursor");
            Set("rstick", "Scroll");
            Set("a", "Left click");
            Set("view", "Right click");
            Set("rclick", "Middle click");
            Set("b", "Esc", "Ctrl+W");
            Set("lt", "Precision cursor");
            Set("rt", LayerAction);
            Set("dpadUp", "Volume up");
            Set("dpadDown", "Volume down");
            Set("dpadLeft", "Left arrow");
            Set("dpadRight", "Right arrow");
            return m;
        }

        public static Dictionary<string, Mapping> Profile(string name)
        {
            var m = Common();
            if (name == "Browser")
            {
                m["lb"].Base["tap"] = "Ctrl+Shift+Tab";
                m["rb"].Base["tap"] = "Ctrl+Tab";
                // View and Menu swap roles here: Menu gives the right-click menu, and View and Y follow the shortcuts video
                // sites use, YouTube above all: F full screen (F11 for the whole window) and "/" for search.
                m["menu"].Base["tap"] = "Right click";
                m["view"].Base["tap"] = "F";
                m["view"].Layer["tap"] = "F11";
                m["y"].Base["tap"] = "/";
            }
            return m;
        }

        public static Settings Create()
        {
            var s = new Settings { ProfileOrder = new List<string> { "Desktop", "Browser", "Media" } };
            foreach (var name in s.ProfileOrder) s.Profiles[name] = Profile(name);
            s.Apps.Add(new AppRule { Id = "browser", Kind = "browser", Rule = "Browser" });
            s.Apps.Add(new AppRule { Id = "player", Kind = "player", Rule = "Media" });
            return s;
        }
    }

    /// <summary>Loads and saves settings.json in %APPDATA%\DesktopGamepad.</summary>
    public static class SettingsStore
    {
        public static readonly string Folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopGamepad");
        public static readonly string FilePath = System.IO.Path.Combine(Folder, "settings.json");

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static Settings Load()
        {
            Settings s = null;
            try
            {
                if (File.Exists(FilePath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json);
            }
            catch (Exception ex)
            {
                // A broken file is kept aside rather than silently lost.
                Log.Write("settings could not be read, starting fresh: " + ex.Message);
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
            }
            return Normalize(s ?? Defaults.Create());
        }

        public static void Save(Settings s)
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(s, Json));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }

        /// <summary>Repairs anything missing or out of range, and keeps the fixed app rows in line with this PC.</summary>
        public static Settings Normalize(Settings s)
        {
            if (s.Profiles == null || s.Profiles.Count == 0) { var d = Defaults.Create(); s.Profiles = d.Profiles; s.ProfileOrder = d.ProfileOrder; }
            s.ProfileOrder = (s.ProfileOrder ?? new List<string>()).Where(p => s.Profiles.ContainsKey(p)).Distinct().ToList();
            foreach (var name in s.Profiles.Keys) if (!s.ProfileOrder.Contains(name)) s.ProfileOrder.Add(name);
            foreach (var map in s.Profiles.Values)
                foreach (var id in Defaults.InputIds)
                    if (!map.ContainsKey(id) || map[id] == null) map[id] = new Mapping();
                    else { map[id].Base = map[id].Base ?? new Dictionary<string, string>(); map[id].Layer = map[id].Layer ?? new Dictionary<string, string>(); }
            if (!s.Profiles.ContainsKey(s.ActiveProfile ?? "")) s.ActiveProfile = s.ProfileOrder[0];

            s.CursorSpeed = Math.Max(1, Math.Min(10, s.CursorSpeed));
            s.ScrollSpeed = Math.Max(1, Math.Min(10, s.ScrollSpeed));
            if (s.MouseMode != "on" && s.MouseMode != "off") s.MouseMode = "on";
            s.Notify = s.Notify ?? new NotifySettings();

            s.Apps = (s.Apps ?? new List<AppRule>()).Where(a => a != null && !string.IsNullOrEmpty(a.Id)).ToList();
            // "Let this app use the controller" was merged into "off" (decided 2026-09-15).
            string ValidRule(string rule, string fallback) =>
                rule == "own" || rule == "off" ? "off" : rule != null && s.Profiles.ContainsKey(rule) ? rule : fallback;

            EnsureRow(s, "browser", "browser", "Browser", 0);
            EnsureRow(s, "player", "player", "Media", 1);

            // Windows' own controller apps (Xbox, Store, Settings, Start, search, Task View) are handled in AppHost.RuleFor.
            // They used to take a locked row each; one line under the list says it now. Older settings drop those rows here.
            s.Apps.RemoveAll(a => a.Kind == "xbox" || a.Kind == "store" || a.Kind == "wsettings");

            // Steam: only when installed. The Steam window uses the main profile by default; Big Picture Mode is always
            // off (it has its own controller support), which the app rules handle by window.
            string steam = SystemInfo.SteamExePath();
            var steamRow = s.Apps.FirstOrDefault(a => a.Kind == "steam");
            if (steam == null) s.Apps.RemoveAll(a => a.Kind == "steam");
            else if (steamRow == null) s.Apps.Add(new AppRule { Id = "steam", Kind = "steam", Name = "Steam", Path = steam, Rule = s.ProfileOrder[0] });
            else steamRow.Path = steam;

            foreach (var a in s.Apps)
            {
                if (a.Kind == "xbox" || a.Kind == "store" || a.Kind == "wsettings") continue;
                string fallback = a.Kind == "browser" ? "Browser" : a.Kind == "player" ? "Media" : s.ProfileOrder[0];
                a.Rule = ValidRule(a.Rule, s.Profiles.ContainsKey(fallback) || fallback == "off" ? fallback : s.ProfileOrder[0]);
            }
            return s;
        }

        static void EnsureRow(Settings s, string id, string kind, string rule, int index)
        {
            if (s.Apps.Any(a => a.Kind == kind)) return;
            s.Apps.Insert(Math.Min(index, s.Apps.Count), new AppRule { Id = id, Kind = kind, Rule = rule });
        }
    }
}
