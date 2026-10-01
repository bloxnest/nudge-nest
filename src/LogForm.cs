using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NudgeNest
{
    /// <summary>The newest log entries, updating live.</summary>
    internal sealed class LogForm : Form
    {
        private const int MaxLines = 300;
        private readonly ListBox list = new ListBox();

        public LogForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.Text(9.5f);
            Text = AppInfo.Name + " activity log";
            BackColor = Theme.Window;
            ForeColor = Theme.Body;
            ClientSize = new Size(620, 340);
            MinimumSize = new Size(380, 220);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (ArgumentException) { }

            list.Dock = DockStyle.Fill;
            list.IntegralHeight = false;
            list.HorizontalScrollbar = true;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Surface;
            list.ForeColor = Theme.Body;
            list.Font = Theme.Text(9.5f);

            var bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 52;
            bar.BackColor = Theme.Window;
            var note = UI.Label("Only the newest entries are kept, so the file stays small.", Theme.Text(9f), Theme.Muted,
                16, 17, 400, 20);
            note.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            var open = new FlatButton();
            open.Primary = false;
            open.Font = Theme.Text(9f, FontStyle.Bold);
            open.Text = "Open log file";
            open.Icon = Icons.Kind.Log;
            open.SetBounds(620 - 16 - 130, 10, 130, 32);
            open.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            open.Click += delegate
            {
                if (File.Exists(Logger.FilePath)) Process.Start(Logger.FilePath);
            };
            bar.Controls.AddRange(new Control[] { note, open });

            Controls.Add(list);
            Controls.Add(bar);
            ResumeLayout(false);

            foreach (string line in Logger.Recent) list.Items.Add(line);
            ScrollToEnd();
            Logger.Written += Add;
            FormClosed += delegate { Logger.Written -= Add; };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        private void Add(string line)
        {
            list.Items.Add(line);
            while (list.Items.Count > MaxLines) list.Items.RemoveAt(0);
            ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            if (list.Items.Count > 0) list.TopIndex = list.Items.Count - 1;
        }
    }
}
