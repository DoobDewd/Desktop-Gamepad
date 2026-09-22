using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DesktopGamepad
{
    /// <summary>
    /// Desktop Gamepad's own mark: an accent rounded square with a dark dot, as in the design's title bar.
    /// Drawn in code, so no controller brand art ends up in the icon (BUILD_NOTES §8).
    /// </summary>
    public static class AppIcon
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);

        public static readonly Color Accent = Color.FromArgb(0x60, 0xCD, 0xFF);
        static readonly Color Dot = Color.FromArgb(0x06, 0x20, 0x2B);

        /// <summary>Returns an icon the caller owns; dispose it when done.</summary>
        public static Icon Create(int size, bool dimmed = false)
        {
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float pad = size * 0.06f, box = size - pad * 2, radius = size * 0.28f;
                using (var path = RoundedRect(pad, pad, box, box, radius))
                using (var fill = new SolidBrush(dimmed ? Color.FromArgb(0x7A, 0x8A, 0x92) : Accent))
                    g.FillPath(fill, path);
                float dot = size * 0.34f;
                using (var brush = new SolidBrush(Dot))
                    g.FillEllipse(brush, (size - dot) / 2f, (size - dot) / 2f, dot, dot);

                IntPtr handle = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
