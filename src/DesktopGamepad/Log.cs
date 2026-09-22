using System;
using System.IO;

namespace DesktopGamepad
{
    /// <summary>A small rolling log in %LOCALAPPDATA%\DesktopGamepad\desktopgamepad.log, for troubleshooting.</summary>
    public static class Log
    {
        static readonly object Gate = new object();
        static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopGamepad");
        static readonly string FilePath = Path.Combine(Folder, "desktopgamepad.log");
        const long MaxBytes = 1024 * 1024;

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes) File.Copy(FilePath, FilePath + ".old", true);
                    if (info.Exists && info.Length > MaxBytes) File.WriteAllText(FilePath, "");
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
