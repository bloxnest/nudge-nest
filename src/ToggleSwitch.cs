using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NudgeNest
{
    /// <summary>
    /// An ON/OFF switch in the dark theme. The knob slides and the track fades to mint; while you hold the
    /// mouse button, the knob stretches a little toward where it's going. The big one also shows ON/OFF.
    /// </summary>
    internal sealed class ToggleSwitch : ThemedControl
    {
        private bool on;
        private readonly Tween knobT;

        /// <summary>Raised when you flip the switch (not when the program sets On).</summary>
        public event EventHandler Toggled;

        /// <summary>Show "ON"/"OFF" inside the track (for the big switch).</summary>
        public bool ShowText;

        public ToggleSwitch()
        {
            AccessibleRole = AccessibleRole.CheckButton;
            Font = Theme.Text(10f, FontStyle.Bold);
            knobT = new Tween(0, Invalidate);
        }

        public bool On
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                AccessibleDescription = on ? "On" : "Off";
                if (IsHandleCreated && Visible) knobT.To(on ? 1 : 0, 260);
                else knobT.Snap(on ? 1 : 0);
            }
        }

        /// <summary>0 = fully off, 1 = fully on; in between while the knob slides.</summary>
        internal double KnobPosition
        {
            get { return knobT.Value; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Smooth(e);
            double k = knobT.Value, hover = HoverT.Value;
            var track = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
            float radius = track.Height / 2;

            Color offFill = Theme.Mix(Theme.Surface, Theme.SurfaceHover, hover);
            Color onFill = Theme.Mix(Theme.Accent, Theme.AccentHover, hover);
            using (GraphicsPath pill = Theme.Rounded(track, radius))
            {
                using (var brush = new SolidBrush(Theme.Mix(offFill, onFill, k))) g.FillPath(brush, pill);
                using (var pen = new Pen(Theme.Fade(Theme.Line, 1 - k), 1.2f)) g.DrawPath(pen, pill);
            }

            float pad = Math.Max(3f, track.Height * 0.12f);
            float knob = track.Height - 2 * pad;
            float stretch = (float)(PressT.Value * knob * 0.28);   // pressed: the knob widens toward its next spot
            float travel = track.Width - 2 * pad - knob;
            float knobX = track.Left + pad + (float)(travel * k);
            float knobLeft = on ? knobX - stretch : knobX;
            using (GraphicsPath dot = Theme.Rounded(new RectangleF(knobLeft, track.Top + pad, knob + stretch, knob), knob / 2))
            using (var brush = new SolidBrush(Theme.Mix(Theme.Muted, Theme.OnAccent, k)))
                g.FillPath(brush, dot);

            if (ShowText)
            {
                var onLabel = new RectangleF(track.Left + pad, track.Top, track.Width - knob - 3 * pad, track.Height);
                var offLabel = new RectangleF(track.Left + knob + 2 * pad, track.Top, track.Width - knob - 3 * pad, track.Height);
                if (k > 0.01) DrawCentered(g, "ON", Font, Theme.Fade(Theme.OnAccent, k), onLabel);
                if (k < 0.99) DrawCentered(g, "OFF", Font, Theme.Fade(Theme.Body, 1 - k), offLabel);
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
