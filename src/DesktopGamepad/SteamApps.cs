using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DesktopGamepad
{
    /// <summary>
    /// The type Steam gives an installed item ("Game", "Application", "Tool"…), read from Steam's own files, so programs
    /// bought on Steam are not taken for games.
    /// </summary>
    static class SteamApps
    {
        /// <summary>Games, demos and mods; unknown types count as games too, which is the safe side.</summary>
        public static bool IsGameType(string type)
        {
            switch (type.ToLowerInvariant())
            {
                case "application": case "tool": case "video": case "music": case "config": return false;
                default: return true;
            }
        }

        /// <summary>
        /// The Steam type of the item a program under "steamapps\common" belongs to, or null when it cannot be told.
        /// <paramref name="commonAt"/> is where "\steamapps\common\" starts in the path.
        /// </summary>
        public static string TypeOf(string exePath, int commonAt)
        {
            try
            {
                string steamapps = exePath.Substring(0, commonAt) + @"\steamapps";
                string rest = exePath.Substring(commonAt + @"\steamapps\common\".Length);
                int slash = rest.IndexOf('\\');
                if (slash <= 0) return null;
                uint appId = AppIdFor(steamapps, rest.Substring(0, slash));
                return appId == 0 ? null : ReadType(appId);
            }
            catch (Exception ex)
            {
                Log.Write("Steam type check failed for " + exePath + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>The app id whose manifest in this library names the install folder, or 0.</summary>
        static uint AppIdFor(string steamapps, string installDir)
        {
            foreach (string manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                string text = File.ReadAllText(manifest);
                var dir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
                if (!dir.Success || !string.Equals(dir.Groups[1].Value, installDir, StringComparison.OrdinalIgnoreCase)) continue;
                var id = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
                return id.Success ? uint.Parse(id.Groups[1].Value) : 0;
            }
            return 0;
        }

        // ---- Steam's app info cache (appcache\appinfo.vdf), binary, format version 29 ----

        const uint AppInfoV29 = 0x07564429;

        static string ReadType(uint appId)
        {
            string steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
            if (string.IsNullOrEmpty(steam)) return null;
            byte[] data;
            using (var fs = new FileStream(Path.Combine(steam, "appcache", "appinfo.vdf"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                data = new byte[fs.Length];
                int read = 0;
                while (read < data.Length) { int n = fs.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
            }
            if (data.Length < 16 || BitConverter.ToUInt32(data, 0) != AppInfoV29)
            {
                Log.Write("Steam app info has a format Desktop Gamepad does not read; Steam items count as games");
                return null;
            }
            string[] strings = ReadStringTable(data, (int)BitConverter.ToInt64(data, 8));

            int pos = 16;
            while (pos + 8 <= data.Length)
            {
                uint id = BitConverter.ToUInt32(data, pos);
                if (id == 0) break;
                int size = BitConverter.ToInt32(data, pos + 4);
                int next = pos + 8 + size;
                if (id == appId)
                {
                    // State, last update, PICS token, SHA-1, change number and binary SHA-1 come before the key values.
                    int kv = pos + 8 + 4 + 4 + 8 + 20 + 4 + 20;
                    return FindString(data, ref kv, next, strings, new[] { "appinfo", "common", "type" }, 0);
                }
                pos = next;
            }
            return null;
        }

        static string[] ReadStringTable(byte[] data, int at)
        {
            int count = BitConverter.ToInt32(data, at);
            var list = new string[count];
            int pos = at + 4;
            for (int i = 0; i < count; i++) list[i] = ReadCString(data, ref pos);
            return list;
        }

        static string ReadCString(byte[] data, ref int pos)
        {
            int start = pos;
            while (data[pos] != 0) pos++;
            string s = Encoding.UTF8.GetString(data, start, pos - start);
            pos++;
            return s;
        }

        /// <summary>
        /// Walks one level of binary key values, following <paramref name="path"/> down, and returns the string at its end.
        /// Keys are indexes into the string table.
        /// </summary>
        static string FindString(byte[] data, ref int pos, int end, string[] strings, string[] path, int depth)
        {
            while (pos < end)
            {
                byte type = data[pos++];
                if (type == 8 || type == 11) return null; // end of this level
                int keyIndex = BitConverter.ToInt32(data, pos);
                pos += 4;
                string key = keyIndex >= 0 && keyIndex < strings.Length ? strings[keyIndex] : "";
                bool wanted = depth < path.Length && string.Equals(key, path[depth], StringComparison.OrdinalIgnoreCase);
                switch (type)
                {
                    case 0:
                        if (wanted) return FindString(data, ref pos, end, strings, path, depth + 1);
                        SkipLevel(data, ref pos, end);
                        break;
                    case 1:
                        string value = ReadCString(data, ref pos);
                        if (wanted && depth == path.Length - 1) return value;
                        break;
                    case 2: case 3: case 4: case 6: pos += 4; break;
                    case 7: case 10: pos += 8; break;
                    case 5: while (pos + 1 < end && (data[pos] != 0 || data[pos + 1] != 0)) pos += 2; pos += 2; break;
                    default: throw new InvalidDataException("unknown value type " + type); // stop rather than misread
                }
            }
            return null;
        }

        static void SkipLevel(byte[] data, ref int pos, int end)
        {
            FindString(data, ref pos, end, Array.Empty<string>(), Array.Empty<string>(), 0);
        }
    }
}
