using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NudgeNest
{
    /// <summary>Tray icons drawn at startup: the logo with a small status dot (or a plain dot if the logo is missing).</summary>
    internal static class TrayIcons
    {
        public static Icon Make(Color status)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    using (Image logo = AppInfo.Logo(32))
                    {
                        if (logo != null)
                        {
                            g.DrawImage(logo, 0, 0, 32, 32);
                            using (var ring = new SolidBrush(Theme.Window)) g.FillEllipse(ring, 17, 17, 15, 15);
                            using (var dot = new SolidBrush(status)) g.FillEllipse(dot, 19.5f, 19.5f, 10, 10);
                        }
                        else
                        {
                            using (var dot = new SolidBrush(status)) g.FillEllipse(dot, 2, 2, 28, 28);
                        }
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
    }

    /// <summary>Dark colors for the tray menu, matching the app.</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Heading : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Body;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = e.ImageRectangle;
            using (var pen = new Pen(Theme.Accent, 2f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
                    new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f),
                });
            }
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.Line; } }
            public override Color MenuItemBorder { get { return Theme.SurfaceHover; } }
            public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
            public override Color SeparatorDark { get { return Theme.Line; } }
            public override Color SeparatorLight { get { return Theme.Line; } }
            public override Color CheckBackground { get { return Theme.Surface; } }
            public override Color CheckSelectedBackground { get { return Theme.SurfaceHover; } }
            public override Color CheckPressedBackground { get { return Theme.SurfaceHover; } }
        }
    }
}
