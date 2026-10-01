using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NudgeNest
{
    /// <summary>
    /// The hidden-icons icon: a small tile that says AFK in pixel letters (sharp at 16-32 px).
    /// Its colour shows the state: mint = on, amber = standby, grey = off, red = stopped.
    /// Same letters as tools/make_icon.py.
    /// </summary>
    internal static class TrayIcons
    {
        public static readonly Color On = Color.FromArgb(0x86, 0xE3, 0xCE);
        public static readonly Color Standby = Color.FromArgb(0xE6, 0xC4, 0x7A);
        public static readonly Color Off = Color.FromArgb(0x8C, 0x95, 0x8A);
        public static readonly Color Stopped = Color.FromArgb(0xF0, 0x9A, 0x9A);
        private static readonly Color Ink = Color.FromArgb(0x10, 0x28, 0x21);

        private static readonly string[][] Font4x5 =
        {
            new[] { "0110", "1001", "1111", "1001", "1001" },   // A
            new[] { "1111", "1000", "1110", "1000", "1000" },   // F
            new[] { "1001", "1010", "1100", "1010", "1001" },   // K
        };

        private static readonly string[][] Font5x7 =
        {
            new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
            new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
        };

        public static Icon Make(Color fill, int size)
        {
            size = Math.Max(16, size);
            using (var bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float r = Math.Max(2, size / 6f);
                    using (var path = RoundedSquare(size, r))
                    using (var brush = new SolidBrush(fill))
                        g.FillPath(brush, path);

                    g.SmoothingMode = SmoothingMode.None;
                    string[][] font = size >= 24 && size < 32 ? Font5x7 : Font4x5;
                    int scale = size >= 32 ? 2 : 1;
                    int glyphW = font[0][0].Length, glyphH = font[0].Length;
                    int width = (3 * glyphW + 2) * scale, height = glyphH * scale;
                    int x0 = (size - width) / 2, y0 = (size - height) / 2;
                    using (var ink = new SolidBrush(Ink))
                    {
                        for (int letter = 0; letter < 3; letter++)
                            for (int row = 0; row < glyphH; row++)
                                for (int col = 0; col < glyphW; col++)
                                    if (font[letter][row][col] == '1')
                                        g.FillRectangle(ink, x0 + (letter * (glyphW + 1) + col) * scale, y0 + row * scale, scale, scale);
                    }
                }
                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                        return (Icon)temp.Clone();   // the clone owns its own copy of the handle
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedSquare(int size, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2, s = size - 1;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(s - d, 0, d, d, 270, 90);
            path.AddArc(s - d, s - d, d, d, 0, 90);
            path.AddArc(0, s - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
