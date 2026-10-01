using System.Drawing;
using System.Windows.Forms;

namespace NudgeNest
{
    /// <summary>"Press the key Anti-AFK should tap": picks the custom nudge key.</summary>
    internal sealed class KeyCaptureForm : Form
    {
        /// <summary>The picker on screen, if any (the tray menu waits while it's open).</summary>
        public static KeyCaptureForm Open;

        private readonly Label problemLabel = new Label();

        public Keys Key { get; private set; }

        public string ProblemText
        {
            get { return problemLabel.Text; }
        }

        /// <summary>Asks for a key, then saves and logs it. False if you cancelled.</summary>
        public static bool AskAndSave(IWin32Window owner, Settings settings)
        {
            using (var form = new KeyCaptureForm())
            {
                Open = form;
                try
                {
                    if (form.ShowDialog(owner) != DialogResult.OK) return false;
                }
                finally
                {
                    Open = null;
                }
                settings.CustomKey = form.Key;
                settings.Save();
                Logger.Write("Custom key set to " + KeyChoice.Name(form.Key));
                return true;
            }
        }

        public KeyCaptureForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.Text(9.5f);
            Text = "Custom key";
            ClientSize = new Size(380, 172);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = Theme.Window;
            ForeColor = Theme.Body;

            var prompt = UI.Label("Press the key " + AppInfo.Name + " should tap in Roblox.",
                Theme.Text(11f, FontStyle.Bold), Theme.Heading, 20, 18, 340, 24);
            var hint = UI.Label("Letters, numbers, F-keys, arrows and punctuation work. Esc cancels.",
                Theme.Text(9f), Theme.Muted, 20, 46, 340, 20);
            problemLabel.ForeColor = Theme.Danger;
            problemLabel.Font = Theme.Text(9f);
            problemLabel.SetBounds(20, 76, 340, 40);
            var cancel = new FlatButton();
            cancel.Primary = false;
            cancel.Font = Theme.Text(9f, FontStyle.Bold);
            cancel.Text = "Cancel";
            cancel.SetBounds(380 - 20 - 96, 124, 96, 34);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };

            Controls.AddRange(new Control[] { prompt, hint, problemLabel, cancel });
            ResumeLayout(false);
        }

        protected override void OnHandleCreated(System.EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        /// <summary>Every key goes through here first, including Tab, arrows and Alt.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            // shortcuts like Alt+F4 keep working; Ctrl or Alt pressed on its own is offered (and refused)
            if ((keyData & (Keys.Control | Keys.Alt)) != 0 && key != Keys.ControlKey && key != Keys.Menu)
                return base.ProcessCmdKey(ref msg, keyData);
            Offer(key);
            return true;
        }

        /// <summary>Accepts the key (closing the window), or shows why it can't be used.</summary>
        internal void Offer(Keys key)
        {
            if (key == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }
            string problem = KeyChoice.Problem(key);
            if (problem != null)
            {
                problemLabel.Text = KeyChoice.Name(key) + " can't be used: " + problem + ".";
                return;
            }
            Key = key;
            DialogResult = DialogResult.OK;
        }
    }
}
