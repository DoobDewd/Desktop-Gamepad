using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace DesktopGamepad
{
    /// <summary>
    /// Tells whether a program is a game, wherever the user installed it, so Desktop Gamepad steps aside in windowed games the
    /// same way it does in fullscreen ones. Nothing here depends on fixed folders: game stores leave markers inside each
    /// game's own install folder, and Windows keeps its own list of the games it has recognised, with their full paths.
    /// </summary>
    static class Games
    {
        // Markers stores put in a game's install folder, looked for from the program's folder upwards. Only markers the
        // stores' own launchers do not carry are listed, so a launcher window is not mistaken for a game.
        static readonly (string Name, bool Folder, string Store)[] Markers =
        {
            (".egstore", true, "Epic Games Store"),
            ("MicrosoftGame.config", false, "Xbox app"),
        };
        const int LevelsUp = 6;

        static readonly object gate = new object();
        static readonly Dictionary<string, string> storeGames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static HashSet<string> windowsList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static DateTime windowsListReadAt = DateTime.MinValue;

        public static bool IsGame(string exePath, uint processId)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            lock (gate)
            {
                // Windows adds games to its list as they are played, so it is read again now and then.
                if ((DateTime.UtcNow - windowsListReadAt).TotalSeconds >= 60)
                {
                    windowsList = ReadWindowsList();
                    windowsListReadAt = DateTime.UtcNow;
                }
                if (!storeGames.TryGetValue(exePath, out string store))
                {
                    store = FindStore(exePath);
                    storeGames[exePath] = store;
                    if (store != null) Log.Write("game detected (" + store + "): " + exePath);
                }
                // A game steps Desktop Gamepad aside only when it can use the controller itself, so it must look like a game
                // (from a game store, or on Windows' list of games, which also holds guesses such as Aseprite) and have loaded
                // controller support. Looking like a game matters too: File Explorer and Steam load controller support as well.
                if (store == null && !windowsList.Contains(exePath)) return false;
                return ReadsControllers(exePath, processId);
            }
        }

        // Windows' controller interfaces and the libraries most games reach controllers through.
        static readonly HashSet<string> ControllerModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll", "xinputuap.dll",
            "dinput.dll", "dinput8.dll", "gameinput.dll", "gameinputredist.dll", "windows.gaming.input.dll", "sdl2.dll", "sdl3.dll"
        };

        // A yes holds for the life of the process. A no is checked again after a while, since games load their
        // controller support some time after they start.
        static readonly Dictionary<uint, (string Path, bool Reads, DateTime At)> controllerChecks = new Dictionary<uint, (string, bool, DateTime)>();

        static bool ReadsControllers(string exePath, uint processId)
        {
            if (processId == 0) return true;
            if (controllerChecks.TryGetValue(processId, out var seen) && string.Equals(seen.Path, exePath, StringComparison.OrdinalIgnoreCase)
                && (seen.Reads || (DateTime.UtcNow - seen.At).TotalSeconds < 2))
                return seen.Reads;

            bool reads = LoadedModules(processId, out var modules) ? modules.Overlaps(ControllerModules) : true; // cannot look: assume a game
            if (seen.Path == null || seen.Reads != reads)
                Log.Write((reads ? "game reads the controller itself: " : "game or program without controller support, mouse stays on: ") + exePath);
            if (controllerChecks.Count > 200) controllerChecks.Clear();
            controllerChecks[processId] = (exePath, reads, DateTime.UtcNow);
            return reads;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [System.Runtime.InteropServices.DllImport("psapi.dll", SetLastError = true)]
        static extern bool EnumProcessModulesEx(IntPtr process, [System.Runtime.InteropServices.Out] IntPtr[] modules, int size, out int needed, uint filter);
        [System.Runtime.InteropServices.DllImport("psapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetModuleBaseName(IntPtr process, IntPtr module, System.Text.StringBuilder name, int size);

        /// <summary>The file names of the libraries a process has loaded; false when Windows does not let us look.</summary>
        static bool LoadedModules(uint processId, out HashSet<string> names)
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr h = OpenProcess(0x0400 | 0x0010 /* QUERY_INFORMATION | VM_READ */, false, processId);
            if (h == IntPtr.Zero) return false;
            try
            {
                var modules = new IntPtr[1024];
                int size = IntPtr.Size * modules.Length;
                if (!EnumProcessModulesEx(h, modules, size, out int needed, 0x03 /* LIST_MODULES_ALL */)) return false;
                if (needed > size)
                {
                    modules = new IntPtr[needed / IntPtr.Size];
                    size = needed;
                    if (!EnumProcessModulesEx(h, modules, size, out needed, 0x03)) return false;
                }
                var sb = new System.Text.StringBuilder(260);
                for (int i = 0; i < Math.Min(modules.Length, needed / IntPtr.Size); i++)
                {
                    sb.Clear();
                    if (GetModuleBaseName(h, modules[i], sb, sb.Capacity) > 0) names.Add(sb.ToString());
                }
                return true;
            }
            finally { CloseHandle(h); }
        }

        /// <summary>The store a program was installed by, from the markers in its install folder, or null.</summary>
        static string FindStore(string exePath)
        {
            try
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (exePath.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase)) return null;

                // Every Steam library, on any drive, keeps its games in a "steamapps\common" folder. Steam also sells programs
                // (Wallpaper Engine, DisplayFusion…), which go there too, so Steam's own record of the item's type decides.
                int common = exePath.IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);
                if (common >= 0)
                {
                    string type = SteamApps.TypeOf(exePath, common);
                    if (type != null && !SteamApps.IsGameType(type))
                    {
                        Log.Write("Steam " + type + ", not a game: " + exePath);
                        return null;
                    }
                    return "Steam";
                }

                string dir = Path.GetDirectoryName(exePath);
                for (int i = 0; i < LevelsUp && !string.IsNullOrEmpty(dir); i++)
                {
                    if (string.Equals(Path.GetPathRoot(dir), dir, StringComparison.OrdinalIgnoreCase)) break; // never a drive root
                    foreach (var m in Markers)
                        if (m.Folder ? Directory.Exists(Path.Combine(dir, m.Name)) : File.Exists(Path.Combine(dir, m.Name))) return m.Store;
                    if (Directory.EnumerateFiles(dir, "goggame-*.info").Any()) return "GOG";
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (Exception ex) { Log.Write("game check failed for " + exePath + ": " + ex.Message); }
            return null;
        }

        /// <summary>Windows' own list of recognised games (the one Game Bar uses), as full program paths.</summary>
        static HashSet<string> ReadWindowsList()
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children"))
                {
                    if (key == null) return list;
                    foreach (string name in key.GetSubKeyNames())
                        using (var child = key.OpenSubKey(name))
                            if (child?.GetValue("MatchedExeFullPath") is string path && path.Length > 0) list.Add(path);
                }
            }
            catch (Exception ex) { Log.Write("Windows game list could not be read: " + ex.Message); }
            return list;
        }
    }
}
