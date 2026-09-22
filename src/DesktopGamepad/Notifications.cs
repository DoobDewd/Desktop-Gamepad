using System.Windows.Forms;

namespace DesktopGamepad
{
    /// <summary>
    /// Windows notifications, shown through Desktop Gamepad's own tray icon. An earlier version added a second, short-lived
    /// tray entry for each message, to get a large picture in the notification, but that showed the icon twice in the tray.
    /// </summary>
    public static class Notifications
    {
        public static void Show(NotifyIcon tray, string title, string text, bool sound)
        {
            Log.Write("notification: " + title + " - " + text);
            if (tray == null) return;
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text;
            // The icon type decides the sound: Info plays one, None stays quiet.
            tray.BalloonTipIcon = sound ? ToolTipIcon.Info : ToolTipIcon.None;
            tray.ShowBalloonTip(8000);
        }
    }
}
