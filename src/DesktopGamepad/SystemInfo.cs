using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DesktopGamepad
{
    public sealed class AppIdentity
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    /// <summary>Facts about this PC that the Apps page shows (BUILD_NOTES §1).</summary>
    public static class SystemInfo
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int SHLoadIndirectString(string source, StringBuilder output, int outputSize, IntPtr reserved);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int AssocQueryString(int flags, int what, string assoc, string extra, StringBuilder output, ref int size);
        const int ASSOCSTR_EXECUTABLE = 2, ASSOCSTR_FRIENDLYAPPNAME = 4;

        /// <summary>The app that opens web links.</summary>
        public static AppIdentity DefaultBrowser() =>
            FromAssociation("http") ??
            FromProgId(ReadUserChoice(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"));

        /// <summary>The app that opens videos, judged by one representative extension.</summary>
        public static AppIdentity DefaultMediaPlayer() =>
            FromAssociation(".mp4") ??
            FromProgId(ReadUserChoice(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mp4\UserChoice"));

        /// <summary>
        /// Asks Windows which app really opens a file type or link, the same way it decides when something is opened.
        /// The UserChoice key alone is not enough: it can still name an app that was uninstalled, and Windows then
        /// quietly uses another one.
        /// </summary>
        static AppIdentity FromAssociation(string assoc)
        {
            string exe = QueryAssociation(assoc, ASSOCSTR_EXECUTABLE);
            if (string.IsNullOrEmpty(exe)) return null;
            string name = QueryAssociation(assoc, ASSOCSTR_FRIENDLYAPPNAME);
            if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(exe);
            return new AppIdentity { Name = name.Trim(), Path = exe };
        }

        static string QueryAssociation(string assoc, int what)
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return AssocQueryString(0, what, assoc, null, sb, ref size) == 0 ? sb.ToString() : null;
        }

        public static bool XboxAppInstalled()
        {
            const string packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            using (var key = Registry.CurrentUser.OpenSubKey(packages))
            {
                if (key == null) return false;
                foreach (var name in key.GetSubKeyNames())
                    if (name.StartsWith("Microsoft.GamingApp_", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string SteamExePath()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (!(key?.GetValue("SteamPath") is string folder)) return null;
                string exe = System.IO.Path.Combine(folder.Replace('/', '\\'), "steam.exe");
                return File.Exists(exe) ? exe : null;
            }
        }

        static string ReadUserChoice(string keyPath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                return key?.GetValue("ProgId") as string;
        }

        static AppIdentity FromProgId(string progId)
        {
            if (string.IsNullOrEmpty(progId)) return null;
            using (var root = Registry.ClassesRoot.OpenSubKey(progId))
            {
                if (root == null) return null;
                string exe = null;
                using (var command = root.OpenSubKey(@"shell\open\command"))
                    exe = ExeFromCommand(command?.GetValue(null) as string);

                string name = null;
                using (var app = root.OpenSubKey("Application"))
                    name = ResolveIndirect(app?.GetValue("ApplicationName") as string);
                if (string.IsNullOrEmpty(name) && exe != null && File.Exists(exe))
                    name = FileVersionInfo.GetVersionInfo(exe).FileDescription;
                if (string.IsNullOrEmpty(name)) name = exe != null ? System.IO.Path.GetFileNameWithoutExtension(exe) : progId;
                return new AppIdentity { Name = name.Trim(), Path = exe ?? progId };
            }
        }

        /// <summary>First path in a shell command line such as "C:\...\chrome.exe" --single-argument %1.</summary>
        static string ExeFromCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            command = Environment.ExpandEnvironmentVariables(command.Trim());
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : null;
            }
            int exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exeEnd > 0 ? command.Substring(0, exeEnd + 4) : command.Split(' ')[0];
        }

        /// <summary>Turns "@{Package?ms-resource://...}" or "@dll,-123" names into readable text.</summary>
        static string ResolveIndirect(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("@")) return value;
            var sb = new StringBuilder(512);
            return SHLoadIndirectString(value, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
        }

        /// <summary>Name and target for an app the user picked (.exe, or a .lnk shortcut to one).</summary>
        public static AppIdentity FromPickedFile(string file)
        {
            string target = file;
            if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic link = shell.CreateShortcut(file);
                    string linked = link.TargetPath;
                    if (!string.IsNullOrEmpty(linked)) target = linked;
                    Marshal.FinalReleaseComObject(link);
                    Marshal.FinalReleaseComObject(shell);
                }
                catch { }
            }
            string name = null;
            try { if (File.Exists(target)) name = FileVersionInfo.GetVersionInfo(target).FileDescription; } catch { }
            if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(file);
            return new AppIdentity { Name = name.Trim(), Path = target };
        }
    }
}
