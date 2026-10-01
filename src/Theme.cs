using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NudgeNest
{
    /// <summary>The app's dark look: colors, fonts, and a dark Windows title bar. Same family as BloxNest.</summary>
    internal static class Theme
    {
        // GPT-6 Astra's palette: BloxNest's dark base with NudgeNest's mint accent
        public static readonly Color Window = Color.FromArgb(0x0B, 0x0F, 0x0B);
        public static readonly Color Surface = Color.FromArgb(0x13, 0x1A, 0x12);
        public static readonly Color SurfaceHover = Color.FromArgb(0x1B, 0x24, 0x1A);
        public static readonly Color Line = Color.FromArgb(0x2A, 0x35, 0x31);
        public static readonly Color Heading = Color.FromArgb(0xF3, 0xF5, 0xEE);
        public static readonly Color Body = Color.FromArgb(0xC4, 0xCA, 0xBF);
        public static readonly Color Muted = Color.FromArgb(0xA0, 0xA9, 0x9E);
        public static readonly Color Selected = Color.FromArgb(0xE8, 0xEC, 0xE4);
        public static readonly Color Accent = Color.FromArgb(0x86, 0xE3, 0xCE);
        public static readonly Color AccentHover = Color.FromArgb(0xA3, 0xED, 0xDD);
        public static readonly Color OnAccent = Color.FromArgb(0x10, 0x28, 0x21);
        public static readonly Color Ok = Color.FromArgb(0x7D, 0xDA, 0x9B);
        public static readonly Color Warn = Color.FromArgb(0xE6, 0xC4, 0x7A);
        public static readonly Color Danger = Color.FromArgb(0xF0, 0x9A, 0x9A);
        public static readonly Color Off = Color.FromArgb(0x8C, 0x95, 0x8A);

        private const string TextFace = "Segoe UI Variable Text";
        private const string DisplayFace = "Segoe UI Variable Display";

        public static Font Text(float size, FontStyle style = FontStyle.Regular)
        {
            return Make(TextFace, size, style);
        }

        public static Font Display(float size, FontStyle style = FontStyle.Bold)
        {
            return Make(DisplayFace, size, style);
        }

        private static Font Make(string face, float size, FontStyle style)
        {
            var font = new Font(face, size, style);
            // older Windows 10 builds don't have the Variable fonts: fall back to Segoe UI
            return font.Name == face ? font : new Font("Segoe UI", size, style);
        }

        /// <summary>Dark title bar; on Windows 11 also the same color as the window, so they blend.</summary>
        public static void DarkTitleBar(Form form)
        {
            int on = 1;
            NativeMethods.DwmSetWindowAttribute(form.Handle, 20, ref on, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
            int caption = ColorRef(Window);
            NativeMethods.DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);   // DWMWA_CAPTION_COLOR
            int text = ColorRef(Body);
            NativeMethods.DwmSetWindowAttribute(form.Handle, 36, ref text, 4);   // DWMWA_TEXT_COLOR
        }

        private static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>Base for the owner-drawn controls: double-buffered, hover tracking, keyboard focus cue.</summary>
    internal abstract class ThemedControl : Control
    {
        protected bool Hover, Pressed;

        protected ThemedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hover = false; Pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pressed = true; Focus(); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected void FocusRing(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues) return;
            using (GraphicsPath path = Theme.Rounded(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), radius))
            using (var pen = new Pen(Theme.Accent, 2f))
                g.DrawPath(pen, path);
        }

        protected static Graphics Smooth(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            return e.Graphics;
        }

        protected static void DrawCentered(Graphics g, string text, Font font, Color color, RectangleF r)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, font, brush, r, format);
            }
        }
    }

    /// <summary>A rounded button: the accent "primary" style, or a dark "secondary" style.</summary>
    internal sealed class FlatButton : ThemedControl
    {
        public bool Primary = true;
        public Icons.Kind Icon = Icons.Kind.None;

        public FlatButton()
        {
            Font = Theme.Text(10.5f, FontStyle.Bold);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color fill = Primary ? (Hover ? Theme.AccentHover : Theme.Accent) : (Hover ? Theme.SurfaceHover : Theme.Surface);
            if (!Enabled) fill = Theme.Surface;
            Color ink = !Enabled ? Theme.Muted : Primary ? Theme.OnAccent : Theme.Heading;
            using (GraphicsPath path = Theme.Rounded(r, 8))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (!Primary)
                    using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            SizeF size = g.MeasureString(Text, Font);
            float iconSize = Icon == Icons.Kind.None ? 0 : 16;
            float gap = iconSize > 0 ? 8 : 0;
            float x = (Width - (size.Width + iconSize + gap)) / 2;
            if (iconSize > 0)
                Icons.Draw(g, Icon, new RectangleF(x, (Height - iconSize) / 2, iconSize, iconSize), ink, 1.8f);
            DrawCentered(g, Text, Font, ink, new RectangleF(x + iconSize + gap, 0, size.Width + 2, Height));
            FocusRing(g, r, 8);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        }
    }

    /// <summary>A row of choices where exactly one is picked (like BloxNest's 1-6 window picker).</summary>
    internal sealed class Segmented : ThemedControl
    {
        private string[] items = new string[0];
        private int selected = -1, hovered = -1;

        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            Font = Theme.Text(10f, FontStyle.Regular);
            AccessibleRole = AccessibleRole.PageTabList;
        }

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = Theme.Rounded(r, 9))
            {
                using (var brush = new SolidBrush(Theme.Surface)) g.FillPath(brush, path);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
            }
            if (items.Length == 0) return;
            float w = (Width - 8f) / items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                var cell = new RectangleF(4 + i * w + 2, 4, w - 4, Height - 8);
                if (i == selected || i == hovered)
                {
                    using (GraphicsPath pill = Theme.Rounded(cell, 7))
                    using (var brush = new SolidBrush(i == selected ? Theme.Selected : Theme.SurfaceHover))
                        g.FillPath(brush, pill);
                }
                bool bold = i == selected;
                using (Font f = bold ? new Font(Font, FontStyle.Bold) : (Font)Font.Clone())
                    DrawCentered(g, items[i], f, i == selected ? Theme.Window : Theme.Body, cell);
            }
            FocusRing(g, r, 9);
        }

        private int IndexAt(int x)
        {
            if (items.Length == 0) return -1;
            int i = (int)((x - 4) / ((Width - 8f) / items.Length));
            return i < 0 || i >= items.Length ? -1 : i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.X);
            if (i != hovered) { hovered = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hovered = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Choose(IndexAt(e.X));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Choose(Math.Max(0, selected - 1));
            else if (e.KeyCode == Keys.Right) Choose(Math.Min(items.Length - 1, selected + 1));
        }

        private void Choose(int i)
        {
            if (i < 0 || i == selected) return;
            SelectedIndex = i;
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>A small round icon button (settings, about, back, log).</summary>
    internal sealed class IconButton : ThemedControl
    {
        public Icons.Kind Icon;
        private readonly ToolTip tip = new ToolTip();

        public IconButton(Icons.Kind icon, string name)
        {
            Icon = icon;
            AccessibleName = name;
            AccessibleRole = AccessibleRole.PushButton;
            tip.SetToolTip(this, name);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            if (Hover || (Focused && ShowFocusCues))
                using (GraphicsPath path = Theme.Rounded(r, 8))
                using (var brush = new SolidBrush(Theme.SurfaceHover))
                    g.FillPath(brush, path);
            float s = Math.Min(Width, Height) * 0.5f;
            Icons.Draw(g, Icon, new RectangleF((Width - s) / 2, (Height - s) / 2, s, s), Hover ? Theme.Heading : Theme.Body, 1.6f);
            FocusRing(g, r, 8);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Line icons drawn with GDI+ in a 24x24 grid (Lucide-style strokes).</summary>
    internal static class Icons
    {
        public enum Kind { None, Settings, Info, Back, Log, Bolt, Github, Globe }

        public static void Draw(Graphics g, Kind kind, RectangleF box, Color color, float stroke)
        {
            if (kind == Kind.None) return;
            GraphicsState state = g.Save();
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(box.Width / 24f, box.Height / 24f);
            using (var pen = new Pen(color, stroke * 24f / box.Width))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case Kind.Settings:
                        // a gear: 8 square teeth around a ring, with a hole in the middle
                        var gear = new List<PointF>();
                        for (int i = 0; i < 8; i++)
                        {
                            double mid = i * Math.PI / 4;
                            foreach (double[] step in new[] { new[] { -0.36, 7.2 }, new[] { -0.22, 9.8 }, new[] { 0.22, 9.8 }, new[] { 0.36, 7.2 } })
                                gear.Add(new PointF(12 + (float)(Math.Cos(mid + step[0]) * step[1]), 12 + (float)(Math.Sin(mid + step[0]) * step[1])));
                        }
                        g.DrawPolygon(pen, gear.ToArray());
                        g.DrawEllipse(pen, 9f, 9f, 6, 6);
                        break;
                    case Kind.Info:
                        g.DrawEllipse(pen, 2.5f, 2.5f, 19, 19);
                        g.DrawLine(pen, 12, 11, 12, 16.5f);
                        g.FillEllipse(brush, 11, 6.6f, 2, 2);
                        break;
                    case Kind.Back:
                        g.DrawLine(pen, 19, 12, 5, 12);
                        g.DrawLines(pen, new[] { new PointF(11, 6), new PointF(5, 12), new PointF(11, 18) });
                        break;
                    case Kind.Log:
                        g.DrawRectangle(pen, 4, 3, 16, 18);
                        g.DrawLine(pen, 8, 8, 16, 8);
                        g.DrawLine(pen, 8, 12, 16, 12);
                        g.DrawLine(pen, 8, 16, 13, 16);
                        break;
                    case Kind.Bolt:
                        g.DrawPolygon(pen, new[] { new PointF(13, 2), new PointF(4, 14), new PointF(11, 14),
                            new PointF(10, 22), new PointF(20, 10), new PointF(13, 10) });
                        break;
                    case Kind.Globe:
                        g.DrawEllipse(pen, 2.5f, 2.5f, 19, 19);
                        g.DrawLine(pen, 2.5f, 12, 21.5f, 12);
                        g.DrawEllipse(pen, 8, 2.5f, 8, 19);
                        break;
                    case Kind.Github:
                        g.DrawEllipse(pen, 2.5f, 2.5f, 19, 19);
                        g.DrawLines(pen, new[] { new PointF(9, 20), new PointF(9, 16.5f), new PointF(8, 14),
                            new PointF(7, 10), new PointF(12, 8.5f), new PointF(17, 10), new PointF(16, 14),
                            new PointF(15, 16.5f), new PointF(15, 20) });
                        break;
                }
            }
            g.Restore(state);
        }
    }
}
