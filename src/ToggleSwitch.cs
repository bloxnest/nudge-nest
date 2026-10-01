using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NudgeNest
{
    /// <summary>An ON/OFF switch in the dark theme. The big one on the main screen also shows ON/OFF.</summary>
    internal sealed class ToggleSwitch : ThemedControl
    {
        private bool on;

        /// <summary>Raised when you flip the switch (not when the program sets On).</summary>
        public event EventHandler Toggled;

        /// <summary>Show "ON"/"OFF" inside the track (for the big switch).</summary>
        public bool ShowText;

        public ToggleSwitch()
        {
            AccessibleRole = AccessibleRole.CheckButton;
            Font = Theme.Text(10f, FontStyle.Bold);
        }

        public bool On
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                AccessibleDescription = on ? "On" : "Off";
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Smooth(e);
            var track = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
            float radius = track.Height / 2;
            Color fill = on ? (Hover ? Theme.AccentHover : Theme.Accent) : (Hover ? Theme.SurfaceHover : Theme.Surface);
            using (GraphicsPath pill = Theme.Rounded(track, radius))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, pill);
                if (!on)
                    using (var pen = new Pen(Theme.Line, 1.2f)) g.DrawPath(pen, pill);
            }

            float pad = Math.Max(3f, track.Height * 0.12f);
            float knob = track.Height - 2 * pad;
            float knobX = on ? track.Right - pad - knob : track.Left + pad;
            using (var brush = new SolidBrush(on ? Theme.OnAccent : Theme.Muted))
                g.FillEllipse(brush, knobX, track.Top + pad, knob, knob);

            if (ShowText)
            {
                var label = on
                    ? new RectangleF(track.Left + pad, track.Top, track.Width - knob - 3 * pad, track.Height)
                    : new RectangleF(track.Left + knob + 2 * pad, track.Top, track.Width - knob - 3 * pad, track.Height);
                DrawCentered(g, on ? "ON" : "OFF", Font, on ? Theme.OnAccent : Theme.Body, label);
            }
            FocusRing(g, track, radius);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Flip();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Flip();
                e.Handled = true;
            }
        }

        private void Flip()
        {
            On = !on;
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }
    }
}
