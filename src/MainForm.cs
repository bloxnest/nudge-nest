using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NudgeNest
{
    internal enum Page { Main, Settings, About }

    /// <summary>
    /// The app window, in three pages like BloxNest: Main (status, ON/OFF, countdown, activity, Nudge now,
    /// session statistics), Settings and About. Also owns the tray icon. Closing or minimizing hides it to the tray.
    /// </summary>
    internal sealed partial class MainForm : Form
    {
        private const string Dash = "—";
        private const int Width0 = 440, Margin0 = 24;

        private readonly Settings settings;
        private readonly AntiAfkEngine engine;
        private readonly EventWaitHandle showSignal, exitSignal;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Icon onIcon = TrayIcons.Make(Theme.Ok);
        private readonly Icon standbyIcon = TrayIcons.Make(Theme.Warn);
        private readonly Icon offIcon = TrayIcons.Make(Theme.Off);
        private readonly Icon haltedIcon = TrayIcons.Make(Theme.Danger);
        private RegisteredWaitHandle showWait, exitWait;
        private bool allowVisible, exiting, toldAboutTray, loading;
        private EngineState? shownState;
        private LogForm logForm;
        private Page page = Page.Main;
        private readonly Dictionary<Page, Panel> pages = new Dictionary<Page, Panel>();
        private readonly Dictionary<Page, int> pageHeights = new Dictionary<Page, int>();   // at 96 dpi
        private float scale = 1f;                                                       // screen dpi / 96
        private readonly PageSlide slide = new PageSlide();
        private readonly Tween heightT, fadeT;

        // main page
        private readonly Card card = new Card();
        private readonly SmoothLabel stateLabel = new SmoothLabel();
        private readonly SmoothLabel robloxLabel = new SmoothLabel();
        private readonly Dot robloxDot = new Dot();
        private readonly ToggleSwitch toggle = new ToggleSwitch();
        private readonly SmoothLabel countdownLabel = new SmoothLabel();
        private readonly SmoothLabel countdownCaption = new SmoothLabel();
        private readonly Segmented activityPicker = new Segmented();
        private readonly SmoothLabel activityHelp = new SmoothLabel();
        private readonly FlatButton nudgeButton = new FlatButton();
        private readonly Label sessionValue = new Label();
        private readonly Label nudgesValue = new Label();
        private readonly Label lastValue = new Label();

        // settings page
        private readonly Segmented intervalPicker = new Segmented();
        private readonly Label keyValue = new Label();
        private readonly ToggleSwitch autoRobloxSwitch = new ToggleSwitch();
        private readonly ToggleSwitch launchSwitch = new ToggleSwitch();
        private readonly ToggleSwitch windowsSwitch = new ToggleSwitch();
        private readonly ToggleSwitch pauseSwitch = new ToggleSwitch();
        private readonly ToggleSwitch hideSwitch = new ToggleSwitch();
        private readonly ToggleSwitch awakeSwitch = new ToggleSwitch();

        // tray menu
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem onItem = new ToolStripMenuItem("Anti-AFK On");
        private readonly ToolStripMenuItem activityItem = new ToolStripMenuItem("Activity Mode");
        private readonly ToolStripMenuItem jumpItem = new ToolStripMenuItem(Copy.ActivityJump);
        private readonly ToolStripMenuItem cameraItem = new ToolStripMenuItem(Copy.ActivityCamera);
        private readonly ToolStripMenuItem customItem = new ToolStripMenuItem();

        public MainForm(bool startHidden, EventWaitHandle showSignal, EventWaitHandle exitSignal)
        {
            this.showSignal = showSignal;
            this.exitSignal = exitSignal;
            allowVisible = !startHidden;
            settings = Settings.Load();
            engine = new AntiAfkEngine(settings);
            // the window is always the page width; only its height eases between pages
            heightT = new Tween(0, delegate { ClientSize = new Size(Px(Width0), (int)Math.Round(heightT.Value)); });
            fadeT = new Tween(1, delegate { Opacity = fadeT.Value; });

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.Text(9.75f);
            Text = AppInfo.Name;
            BackColor = Theme.Window;
            ForeColor = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (ArgumentException) { }

            BuildMainPage();
            BuildSettingsPage();
            BuildAboutPage();
            foreach (Panel p in pages.Values) Controls.Add(p);
            slide.Visible = false;
            Controls.Add(slide);
            ShowPage(Page.Main);
            ResumeLayout(false);
            PerformLayout();
            scale = CurrentAutoScaleDimensions.Height / 96f;

            BuildTray();
            ShowValues();

            engine.Changed += delegate { RefreshStatus(); };
            engine.Halted += delegate (string reason)
            {
                tray.ShowBalloonTip(6000, "Anti-AFK stopped nudging",
                    Capitalize(reason) + ". Open the log for details, then turn Anti-AFK back on.", ToolTipIcon.Warning);
            };
            StartWithWindows.RefreshPath();
            Logger.Write(AppInfo.Name + " " + AppInfo.Version + " opened");
            if (settings.TurnOnAtLaunch) engine.Start();
            RefreshStatus();

            var trimSoon = new System.Windows.Forms.Timer();
            trimSoon.Interval = 5000;
            trimSoon.Tick += delegate
            {
                trimSoon.Stop();
                trimSoon.Dispose();
                if (!Visible) NativeMethods.TrimMemory();
            };
            trimSoon.Start();
        }

        internal AntiAfkEngine Engine { get { return engine; } }
        internal ContextMenuStrip TrayMenu { get { return trayMenu; } }
        internal Page CurrentPage { get { return page; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
            // a second copy of the app signals these instead of starting (see Program)
            if (showWait == null) showWait = OnSignal(showSignal, ShowFromTray);
            if (exitWait == null) exitWait = OnSignal(exitSignal, ExitApp);
        }

        // ---------- pages ----------

        /// <summary>Switch pages: the old one slides out and fades, the new one slides in, the window eases to its height.</summary>
        internal void ShowPage(Page target)
        {
            Page old = page;
            page = target;
            int width = Px(Width0), height = Px(pageHeights[target]);
            Panel to = pages[target];
            to.Size = new Size(width, height);
            if (target == Page.Main) RefreshStatus();
            robloxDot.Pulse = false;

            bool animate = Motion.Enabled && Visible && IsHandleCreated && old != target;
            if (!animate)
            {
                foreach (KeyValuePair<Page, Panel> p in pages) p.Value.Visible = p.Key == target;
                slide.Visible = false;
                heightT.Snap(height);
                if (target == Page.Main) RefreshStatus();
                return;
            }

            // picture of the page that's leaving, shown on top while the new one is prepared underneath
            Panel from = pages[old];
            slide.Bounds = new Rectangle(0, 0, ClientSize.Width, Math.Max(ClientSize.Height, height));
            slide.Prepare(Snapshot(from));
            slide.Visible = true;
            slide.BringToFront();
            slide.Update();
            to.Visible = true;
            Bitmap next = Snapshot(to);
            foreach (Panel p in pages.Values) p.Visible = false;

            slide.Play(next, target == Page.Main ? -1 : 1, 320, delegate
            {
                if (page != target) return;   // another page was picked meanwhile
                to.Visible = true;
                slide.Visible = false;
                slide.Release();
                if (target == Page.Main) RefreshStatus();
            });
            heightT.Snap(ClientSize.Height);
            heightT.Ease = Motion.EaseInOut;
            heightT.To(height, 320);
        }

        private static Bitmap Snapshot(Control c)
        {
            // premultiplied pixels make the per-frame fades much cheaper to draw
            var bitmap = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            c.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            return bitmap;
        }

        /// <summary>A 96-dpi layout size in real pixels on this screen.</summary>
        private int Px(int logical)
        {
            return (int)Math.Round(logical * scale);
        }

        private Panel NewPage(Page which)
        {
            var p = new Panel();
            p.BackColor = Theme.Window;
            p.Location = Point.Empty;
            p.Size = new Size(Width0, 600);
            pages[which] = p;
            return p;
        }

        private void BuildMainPage()
        {
            Panel p = NewPage(Page.Main);
            int right = Width0 - Margin0;

            var logo = new PictureBox();
            logo.Image = AppInfo.Logo(64);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.SetBounds(Margin0, 20, 30, 30);
            var name = UI.Label(AppInfo.Name, Theme.Display(16f), Theme.Heading, 62, 18, 250, 34);
            var settingsButton = new IconButton(Icons.Kind.Settings, "Settings");
            settingsButton.SetBounds(right - 76, 18, 34, 34);
            settingsButton.Click += delegate { ShowPage(Page.Settings); };
            var aboutButton = new IconButton(Icons.Kind.Info, "About");
            aboutButton.SetBounds(right - 34, 18, 34, 34);
            aboutButton.Click += delegate { ShowPage(Page.About); };
            var tagline = UI.Label(Copy.Tagline, Theme.Text(9.75f), Theme.Muted, Margin0, 60, 392, 22);

            card.SetBounds(Margin0, 96, 392, 120);
            card.Controls.Add(UI.Caption("ANTI-AFK", 18, 16, 200, Theme.Surface));
            stateLabel.Font = Theme.Display(24f);
            stateLabel.BackColor = Theme.Surface;
            stateLabel.Behind = Theme.Surface;
            stateLabel.Rise = 6;
            stateLabel.SetBounds(15, 32, 230, 46);
            toggle.ShowText = true;
            toggle.SetBounds(392 - 18 - 92, 36, 92, 40);
            toggle.Toggled += delegate
            {
                if (toggle.On) engine.Start();
                else engine.Stop();
            };
            robloxDot.BackColor = Theme.Surface;
            robloxDot.SetBounds(12, 83, 22, 22);
            robloxLabel.Font = Theme.Text(9.5f);
            robloxLabel.ForeColor = Theme.Body;
            robloxLabel.BackColor = Theme.Surface;
            robloxLabel.Behind = Theme.Surface;
            robloxLabel.SetBounds(34, 83, 340, 22);
            card.Controls.AddRange(new Control[] { stateLabel, toggle, robloxDot, robloxLabel });

            var nextCaption = UI.Caption("NEXT NUDGE", Margin0, 236, 200, Theme.Window);
            countdownLabel.Font = Theme.Display(30f, FontStyle.Regular);
            countdownLabel.ForeColor = Theme.Heading;
            countdownLabel.SetBounds(Margin0 - 4, 252, 200, 52);
            countdownCaption.ForeColor = Theme.Muted;
            countdownCaption.SetBounds(Margin0, 302, 392, 20);

            var activityCaption = UI.Caption("ACTIVITY", Margin0, 340, 200, Theme.Window);
            activityPicker.SetBounds(Margin0, 360, 392, 42);
            activityPicker.SelectedIndexChanged += delegate
            {
                if (!loading) ChooseActivity((NudgeAction)activityPicker.SelectedIndex);
            };
            activityHelp.ForeColor = Theme.Muted;
            activityHelp.Font = Theme.Text(9f);
            activityHelp.SetBounds(Margin0, 408, 392, 20);

            nudgeButton.Text = "Nudge now";
            nudgeButton.Icon = Icons.Kind.Bolt;
            nudgeButton.SetBounds(Margin0, 440, 392, 46);
            nudgeButton.Click += delegate
            {
                engine.NudgeNow();
                if (engine.RobloxCount == 0) nudgeButton.Flash("Roblox isn't running", Icons.Kind.None, 1600);
                else if (engine.LastNudgeWorked) nudgeButton.Flash("Nudged", Icons.Kind.Check, 1400);
                else nudgeButton.Flash("Didn't go through. See the log", Icons.Kind.None, 2200);
            };

            var line = UI.Line(Margin0, 506, 392);
            var stats = new Control[]
            {
                UI.Caption("SESSION", Margin0, 520, 110, Theme.Window),
                UI.Caption("NUDGES", Margin0 + 128, 520, 120, Theme.Window),
                UI.Caption("LAST NUDGE", Margin0 + 266, 520, 126, Theme.Window),
            };
            UI.Value(sessionValue, Margin0, 538, 120);
            UI.Value(nudgesValue, Margin0 + 128, 538, 130);
            UI.Value(lastValue, Margin0 + 266, 538, 126);
            var hint = UI.Label(Copy.TrayHint, Theme.Text(9f), Theme.Muted, Margin0, 578, 260, 20);
            var logLink = new LinkLabel();
            logLink.Text = "Activity log";
            logLink.Font = Theme.Text(9f);
            logLink.LinkColor = logLink.ActiveLinkColor = Theme.Accent;
            logLink.VisitedLinkColor = Theme.Accent;
            logLink.TextAlign = ContentAlignment.TopRight;
            logLink.SetBounds(Width0 - Margin0 - 120, 578, 120, 20);
            logLink.LinkClicked += delegate { OpenLog(); };

            p.Controls.AddRange(new Control[] { logo, name, settingsButton, aboutButton, tagline, card, nextCaption,
                countdownLabel, countdownCaption, activityCaption, activityPicker, activityHelp, nudgeButton, line });
            p.Controls.AddRange(stats);
            p.Controls.AddRange(new Control[] { sessionValue, nudgesValue, lastValue, hint, logLink });
            pageHeights[Page.Main] = 612;
        }

        private void BuildSettingsPage()
        {
            Panel p = NewPage(Page.Settings);
            AddPageHeader(p, "OPTIONS");
            int y = 72;

            p.Controls.Add(UI.Label("Nudge every", Theme.Text(10f, FontStyle.Bold), Theme.Heading, Margin0, y, 300, 22));
            p.Controls.Add(UI.Label(Copy.IntervalHelp, Theme.Text(9f), Theme.Muted, Margin0, y + 22, 392, 20));
            var choices = new string[Settings.IntervalChoices.Length];
            for (int i = 0; i < choices.Length; i++) choices[i] = Settings.IntervalChoices[i] + " min";
            intervalPicker.Items = choices;
            intervalPicker.SetBounds(Margin0, y + 50, 392, 40);
            intervalPicker.SelectedIndexChanged += delegate
            {
                if (loading) return;
                settings.IntervalMinutes = Settings.IntervalChoices[intervalPicker.SelectedIndex];
                settings.Save();
                Logger.Write("Nudge interval: " + AntiAfkEngine.Minutes(settings.IntervalMinutes));
                RefreshStatus();
            };
            p.Controls.Add(intervalPicker);
            y += 106;
            p.Controls.Add(UI.Line(Margin0, y, 392));
            y += 14;

            p.Controls.Add(UI.Label("Custom key", Theme.Text(10f, FontStyle.Bold), Theme.Heading, Margin0, y, 200, 22));
            p.Controls.Add(UI.Label(Copy.CustomKeyHelp, Theme.Text(9f), Theme.Muted, Margin0, y + 22, 210, 20));
            keyValue.Font = Theme.Text(10f, FontStyle.Bold);
            keyValue.ForeColor = Theme.Heading;
            keyValue.TextAlign = ContentAlignment.MiddleRight;
            keyValue.SetBounds(Margin0 + 206, y + 6, 86, 24);
            var change = new FlatButton();
            change.Primary = false;
            change.Font = Theme.Text(9f, FontStyle.Bold);
            change.Text = "Change";
            change.SetBounds(Width0 - Margin0 - 86, y + 4, 86, 32);
            change.Click += delegate
            {
                if (KeyCaptureForm.AskAndSave(this, settings) && !IsDisposed) ShowValues();
            };
            p.Controls.AddRange(new Control[] { keyValue, change });
            y += 52;

            p.Controls.Add(UI.Caption("AUTOMATION", Margin0, y + 14, 200, Theme.Window));
            y += 30;
            y = AddSwitchRow(p, y, autoRobloxSwitch, "Turn on automatically when Roblox starts", Copy.AutoStartHelp,
                on => { settings.AutoStartWithRoblox = on; return "Turn on automatically when Roblox starts: " + (on ? "yes" : "no"); });
            y = AddSwitchRow(p, y, launchSwitch, "Turn on when this app opens", Copy.LaunchHelp,
                on => { settings.TurnOnAtLaunch = on; return null; });
            y = AddSwitchRow(p, y, windowsSwitch, "Start with Windows", Copy.WindowsHelp, on =>
            {
                if (!StartWithWindows.Set(on))
                {
                    Logger.Write("Couldn't change the Start with Windows setting");
                    BeginInvoke((MethodInvoker)ShowValues);
                }
                return null;
            });
            p.Controls.Add(UI.Caption("WHILE NUDGING", Margin0, y + 14, 200, Theme.Window));
            y += 30;
            y = AddSwitchRow(p, y, pauseSwitch, "Wait until I stop typing or clicking", Copy.PauseHelp,
                on => { settings.WaitForPause = on; return null; });
            y = AddSwitchRow(p, y, hideSwitch, "Keep Roblox invisible during a nudge", Copy.HideHelp,
                on => { settings.HideRoblox = on; return null; });
            y = AddSwitchRow(p, y, awakeSwitch, "Keep the PC from sleeping", Copy.AwakeHelp,
                on => { settings.KeepPcAwake = on; return null; });

            p.Controls.Add(UI.Line(Margin0, y + 8, 392));
            y += 8;
            p.Controls.Add(UI.Label("Activity log", Theme.Text(10f, FontStyle.Bold), Theme.Heading, Margin0, y + 10, 250, 22));
            p.Controls.Add(UI.Label(Copy.LogHelp, Theme.Text(9f), Theme.Muted, Margin0, y + 32, 290, 20));
            var view = new FlatButton();
            view.Primary = false;
            view.Font = Theme.Text(9f, FontStyle.Bold);
            view.Text = "View";
            view.Icon = Icons.Kind.Log;
            view.SetBounds(Width0 - Margin0 - 86, y + 14, 86, 32);
            view.Click += delegate { OpenLog(); };
            p.Controls.Add(view);
            pageHeights[Page.Settings] = y + 72;
        }

        private int AddSwitchRow(Panel p, int y, ToggleSwitch sw, string title, string help, Func<bool, string> apply)
        {
            p.Controls.Add(UI.Line(Margin0, y, 392));
            p.Controls.Add(UI.Label(title, Theme.Text(10f, FontStyle.Bold), Theme.Heading, Margin0, y + 9, 330, 22));
            Label helpLabel = UI.Label(help, Theme.Text(9f), Theme.Muted, Margin0, y + 31, 330, 20);
            p.Controls.Add(helpLabel);
            sw.SetBounds(Width0 - Margin0 - 46, y + 16, 46, 26);
            sw.AccessibleName = title;
            sw.Toggled += delegate
            {
                if (loading) return;
                string log = apply(sw.On);
                settings.Save();
                if (log != null) Logger.Write(log);
                RefreshStatus();
            };
            p.Controls.Add(sw);
            return y + 58;
        }

        private void BuildAboutPage()
        {
            Panel p = NewPage(Page.About);
            AddPageHeader(p, "ABOUT + PRIVACY");
            var logo = new PictureBox();
            logo.Image = AppInfo.Logo(64);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.SetBounds(Margin0, 76, 64, 64);
            p.Controls.Add(logo);
            p.Controls.Add(UI.Label(AppInfo.Name, Theme.Display(18f), Theme.Heading, Margin0 + 80, 72, 300, 34));
            p.Controls.Add(UI.Label("Made by " + AppInfo.Developer, Theme.Text(10.5f), Theme.Body, Margin0 + 82, 106, 300, 22));
            p.Controls.Add(UI.Label("Version " + AppInfo.Version, Theme.Text(9f), Theme.Muted, Margin0 + 82, 128, 300, 20));

            int y = 158;
            y = UI.Paragraph(p, Copy.AboutIntro, Theme.Text(9.75f), Theme.Body, Margin0, y, 392) + 14;
            foreach (string[] section in Copy.AboutSections)
            {
                p.Controls.Add(UI.Line(Margin0, y, 392));
                y += 14;
                p.Controls.Add(UI.Label(section[0], Theme.Text(11f, FontStyle.Bold), Theme.Heading, Margin0, y, 392, 24));
                y += 28;
                y = UI.Paragraph(p, section[1], Theme.Text(9.5f), Theme.Body, Margin0, y, 392) + 14;
            }
            p.Controls.Add(UI.Line(Margin0, y, 392));
            y += 16;
            var site = new FlatButton();
            site.Primary = false;
            site.Font = Theme.Text(9f, FontStyle.Bold);
            site.Text = "Website";
            site.Icon = Icons.Kind.Globe;
            site.SetBounds(Margin0, y, 120, 34);
            site.Click += delegate { AppInfo.Open(AppInfo.Website); };
            var source = new FlatButton();
            source.Primary = false;
            source.Font = Theme.Text(9f, FontStyle.Bold);
            source.Text = "Source code";
            source.Icon = Icons.Kind.Github;
            source.SetBounds(Margin0 + 130, y, 140, 34);
            source.Click += delegate { AppInfo.Open(AppInfo.Source); };
            p.Controls.AddRange(new Control[] { site, source });
            pageHeights[Page.About] = y + 58;
        }

        private void AddPageHeader(Panel p, string label)
        {
            var back = new FlatButton();
            back.Primary = false;
            back.Font = Theme.Text(9f, FontStyle.Bold);
            back.Text = "Back";
            back.Icon = Icons.Kind.Back;
            back.SetBounds(Margin0, 18, 88, 34);
            back.Click += delegate { ShowPage(Page.Main); };
            Label title = UI.Caption(label, Width0 - Margin0 - 200, 27, 200, Theme.Window);
            title.TextAlign = ContentAlignment.TopRight;
            p.Controls.AddRange(new Control[] { back, title });
        }

        // ---------- tray ----------

        private void BuildTray()
        {
            onItem.Click += delegate { Toggle(); };
            var nudgeItem = new ToolStripMenuItem("Nudge Now", null, delegate { engine.NudgeNow(); });
            jumpItem.Click += delegate { ChooseActivity(NudgeAction.Jump); };
            cameraItem.Click += delegate { ChooseActivity(NudgeAction.CameraNudge); };
            customItem.Click += delegate { ChooseActivity(NudgeAction.CustomKey); };
            activityItem.DropDownItems.AddRange(new ToolStripItem[] { jumpItem, cameraItem, customItem });
            var openItem = new ToolStripMenuItem("Open " + AppInfo.Name, null, delegate { ShowFromTray(); });
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            var exitItem = new ToolStripMenuItem("Exit", null, delegate { ExitApp(); });
            trayMenu.Items.AddRange(new ToolStripItem[]
            {
                onItem, nudgeItem, activityItem, new ToolStripSeparator(), openItem, new ToolStripSeparator(), exitItem,
            });
            trayMenu.Renderer = new DarkMenuRenderer();
            // while the custom-key picker is open, finish that first
            trayMenu.Opening += delegate
            {
                foreach (ToolStripItem item in trayMenu.Items) item.Enabled = KeyCaptureForm.Open == null;
            };

            tray.ContextMenuStrip = trayMenu;
            tray.Icon = offIcon;
            tray.Text = AppInfo.Name;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
            tray.Visible = true;
        }

        // ---------- activity mode and settings values ----------

        private string CustomText()
        {
            return settings.CustomKey == Keys.None ? "Custom key" : "Custom (" + KeyChoice.Name(settings.CustomKey) + ")";
        }

        private void ChooseActivity(NudgeAction action)
        {
            if (action == NudgeAction.CustomKey && settings.CustomKey == Keys.None
                && !KeyCaptureForm.AskAndSave(Visible ? this : null, settings))
            {
                ShowValues();   // cancelled: keep the current mode
                return;
            }
            if (exiting || IsDisposed) return;
            if (settings.Action != action)
            {
                settings.Action = action;
                settings.Save();
                Logger.Write("Activity mode: " + AntiAfkEngine.Describe(action, settings.CustomKey));
            }
            ShowValues();
        }

        /// <summary>Shows the saved settings in every page and in the tray menu.</summary>
        private void ShowValues()
        {
            loading = true;
            activityPicker.Items = new[] { "Jump", "Camera nudge", CustomText() };
            activityPicker.SelectedIndex = (int)settings.Action;
            activityHelp.Set(settings.Action == NudgeAction.CameraNudge ? Copy.CameraHelp
                : settings.Action == NudgeAction.CustomKey ? Copy.CustomHelp + KeyChoice.Name(settings.CustomKey)
                    + ". Change it in Options."
                : Copy.JumpHelp, Theme.Muted, true);
            int index = Array.IndexOf(Settings.IntervalChoices, settings.IntervalMinutes);
            intervalPicker.SelectedIndex = index >= 0 ? index : Array.IndexOf(Settings.IntervalChoices, 10);
            keyValue.Text = settings.CustomKey == Keys.None ? "not set" : KeyChoice.Name(settings.CustomKey);
            autoRobloxSwitch.On = settings.AutoStartWithRoblox;
            launchSwitch.On = settings.TurnOnAtLaunch;
            windowsSwitch.On = StartWithWindows.IsOn;
            pauseSwitch.On = settings.WaitForPause;
            hideSwitch.On = settings.HideRoblox;
            awakeSwitch.On = settings.KeepPcAwake;
            loading = false;
            customItem.Text = settings.CustomKey == Keys.None ? "Custom key..." : "Custom key (" + KeyChoice.Name(settings.CustomKey) + ")";
            jumpItem.Checked = settings.Action == NudgeAction.Jump;
            cameraItem.Checked = settings.Action == NudgeAction.CameraNudge;
            customItem.Checked = settings.Action == NudgeAction.CustomKey;
        }

        // ---------- status ----------

        private void Toggle()
        {
            if (engine.Running) engine.Stop();
            else engine.Start();
        }

        private static Color StateColor(EngineState state)
        {
            return state == EngineState.Active ? Theme.Ok : state == EngineState.Standby ? Theme.Warn
                : state == EngineState.Halted ? Theme.Danger : Theme.Off;
        }

        private void RefreshStatus()
        {
            EngineState state = engine.State;
            if (shownState != state)
            {
                shownState = state;
                tray.Icon = state == EngineState.Active ? onIcon : state == EngineState.Standby ? standbyIcon
                    : state == EngineState.Halted ? haltedIcon : offIcon;
            }
            toggle.On = engine.Running;
            onItem.Checked = engine.Running;

            string big = Dash, caption, tip;
            switch (state)
            {
                case EngineState.Active:
                    TimeSpan left = engine.NextNudgeIn;
                    if (engine.PcLocked)
                    {
                        caption = "The PC is locked. Nudges resume when you unlock it.";
                        tip = "on, PC locked";
                    }
                    else if (engine.WaitingForPause)
                    {
                        big = "0:00";
                        caption = "Nudge due. Waiting for you to pause...";
                        tip = "on, nudge due";
                    }
                    else
                    {
                        big = Clock(left);
                        caption = engine.UserPlaying ? "You're playing, so the timer keeps resetting." : "Until the next nudge.";
                        tip = "on, next nudge in " + Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + " min";
                    }
                    break;
                case EngineState.Standby:
                    caption = "On standby until Roblox starts.";
                    tip = "on standby";
                    break;
                case EngineState.Halted:
                    caption = "Stopped: a window couldn't be put back. See the Activity log.";
                    tip = "stopped after a problem";
                    break;
                default:
                    caption = engine.RobloxCount > 0 ? "Roblox isn't being kept active."
                        : settings.AutoStartWithRoblox ? "Turns on by itself when Roblox starts."
                        : "Turn it on to keep Roblox active.";
                    tip = "off";
                    break;
            }

            tip = AppInfo.Name + ": " + tip;
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (tray.Text != tip) tray.Text = tip;

            bool shown = Visible && page == Page.Main && !slide.Visible;
            robloxDot.Pulse = shown && state == EngineState.Active;
            card.Glow = state == EngineState.Active;
            if (!Visible || page != Page.Main) return;

            // big state word and captions cross-fade; ticking numbers change in place
            stateLabel.Set(state == EngineState.Active ? "ON" : state == EngineState.Standby ? "STANDBY"
                : state == EngineState.Halted ? "STOPPED" : "OFF",
                state == EngineState.Off ? Theme.Heading : StateColor(state), true);
            robloxDot.Color = engine.RobloxCount == 0 ? Theme.Off : engine.UserPlaying || engine.RobloxInFront ? Theme.Ok
                : StateColor(state == EngineState.Active ? EngineState.Active : EngineState.Off);
            string roblox = RobloxText();
            robloxLabel.Set(roblox, Theme.Body, Kind(roblox) != Kind(robloxLabel.Text));
            countdownLabel.Set(big, Theme.Heading, (big == Dash) != (countdownLabel.Text == Dash));
            countdownCaption.Set(caption, Theme.Muted, true);
            UI.Set(sessionValue, engine.SessionStart == DateTime.MinValue ? Dash : Duration(DateTime.Now - engine.SessionStart));
            UI.Set(nudgesValue, engine.NudgesOk + " ok · " + engine.NudgesFailed + " failed");
            UI.Set(lastValue, engine.LastNudge == DateTime.MinValue ? Dash : engine.LastNudge.ToString("T"));
            lastValue.ForeColor = engine.LastNudge != DateTime.MinValue && !engine.LastNudgeWorked ? Theme.Danger : Theme.Heading;
        }

        /// <summary>The part of a status line that isn't a ticking number, e.g. "Roblox minimized".</summary>
        private static string Kind(string text)
        {
            int idle = (text ?? "").IndexOf(" · idle", StringComparison.Ordinal);
            return idle < 0 ? text : text.Substring(0, idle);
        }

        private string RobloxText()
        {
            if (engine.RobloxCount == 0) return "Roblox isn't running";
            string text;
            if (engine.UserPlaying) text = "Roblox open · you're playing";
            else
            {
                string where = engine.RobloxInFront ? "in front" : engine.RobloxMinimized ? "minimized" : "in the background";
                text = "Roblox " + where + " · idle " + Clock(engine.RobloxIdle);
            }
            if (engine.RobloxCount > 1) text += " · " + engine.RobloxCount + " windows";
            return text;
        }

        private static string Clock(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }

        private static string Duration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        private static string Capitalize(string s)
        {
            return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private void OpenLog()
        {
            if (logForm == null || logForm.IsDisposed) logForm = new LogForm();
            logForm.Show();
            logForm.Activate();
        }

        // ---------- tray behaviour ----------

        /// <summary>Lets the app start hidden in the tray (Application.Run would otherwise show the window).</summary>
        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        private RegisteredWaitHandle OnSignal(WaitHandle signal, MethodInvoker action)
        {
            if (signal == null) return null;
            return ThreadPool.RegisterWaitForSingleObject(signal, delegate
            {
                try { BeginInvoke(action); }
                catch (InvalidOperationException) { }
            }, null, -1, false);
        }

        /// <summary>Open the window from the tray, fading it in.</summary>
        internal void ShowFromTray()
        {
            allowVisible = true;
            fadeT.Done = null;
            if (!Visible) fadeT.Snap(0);
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            RefreshStatus();
            fadeT.To(1, 220);
        }

        /// <summary>Hide to the tray, fading out first (a minimized window just goes).</summary>
        private void HideToTray()
        {
            if (Visible && WindowState != FormWindowState.Minimized && Motion.Enabled)
            {
                fadeT.Done = delegate
                {
                    fadeT.Done = null;
                    FinishHiding();
                };
                fadeT.To(0, 170);
                return;
            }
            FinishHiding();
        }

        private void FinishHiding()
        {
            Hide();
            fadeT.Snap(1);
            WindowState = FormWindowState.Normal;
            robloxDot.Pulse = false;
            if (page != Page.Main) ShowPage(Page.Main);
            if (!toldAboutTray)
            {
                toldAboutTray = true;
                tray.ShowBalloonTip(3000, AppInfo.Name,
                    "Still running here in the tray. Click the icon to open it, right-click for the menu.",
                    ToolTipIcon.Info);
            }
            NativeMethods.TrimMemory();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && Visible) HideToTray();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && page != Page.Main)
            {
                ShowPage(Page.Main);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Logger.Write(AppInfo.Name + " closed");
            engine.Dispose();
            if (showWait != null) showWait.Unregister(null);
            if (exitWait != null) exitWait.Unregister(null);
            tray.Visible = false;
            tray.Dispose();
            onIcon.Dispose();
            standbyIcon.Dispose();
            offIcon.Dispose();
            haltedIcon.Dispose();
            base.OnFormClosed(e);
        }

        internal void ExitApp()
        {
            if (KeyCaptureForm.Open != null)
            {
                // let the key picker close first, then exit
                KeyCaptureForm.Open.Close();
                BeginInvoke((MethodInvoker)ExitApp);
                return;
            }
            exiting = true;
            if (logForm != null && !logForm.IsDisposed) logForm.Close();
            Close();
        }
    }

    /// <summary>Small helpers for building the dark pages.</summary>
    internal static class UI
    {
        public static Label Label(string text, Font font, Color color, int x, int y, int width, int height)
        {
            var label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.SetBounds(x, y, width, height);
            return label;
        }

        public static Label Caption(string text, int x, int y, int width, Color back)
        {
            Label label = Label(text, Theme.Text(8f, FontStyle.Bold), Theme.Muted, x, y, width, 18);
            label.BackColor = back;
            return label;
        }

        public static void Value(Label label, int x, int y, int width)
        {
            label.Font = Theme.Text(10f, FontStyle.Bold);
            label.ForeColor = Theme.Heading;
            label.SetBounds(x, y, width, 24);
        }

        public static Label Line(int x, int y, int width)
        {
            var line = new Label();
            line.BackColor = Theme.Line;
            line.SetBounds(x, y, width, 1);
            return line;
        }

        /// <summary>A wrapping paragraph; returns the y just below it.</summary>
        public static int Paragraph(Control parent, string text, Font font, Color color, int x, int y, int width)
        {
            Size size = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            int height = (int)Math.Ceiling(size.Height * 96f / ScreenDpi()) + 4;
            parent.Controls.Add(Label(text, font, color, x, y, width, height));
            return y + height;
        }

        private static float ScreenDpi()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiY;
        }

        public static void Set(Control control, string text)
        {
            if (control.Text != text) control.Text = text;
        }
    }

    /// <summary>A rounded surface card. While Anti-AFK is on, its border glows faintly mint.</summary>
    internal sealed class Card : Panel
    {
        private readonly Tween glowT;
        private bool glow;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint, true);
            BackColor = Theme.Window;
            glowT = new Tween(0, delegate { Invalidate(false); });
        }

        public bool Glow
        {
            get { return glow; }
            set
            {
                if (glow == value) return;
                glow = value;
                if (IsHandleCreated && Visible) glowT.To(glow ? 1 : 0, 500);
                else glowT.Snap(glow ? 1 : 0);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 10))
            {
                using (var brush = new SolidBrush(Theme.Surface)) e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(Theme.Mix(Theme.Line, Theme.Mix(Theme.Line, Theme.Accent, 0.55), glowT.Value)))
                    e.Graphics.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// The status dot. Its color glides between states, and while Anti-AFK is on a soft ring pulses out of it.
    /// The ring has its own relaxed 30 fps timer (it moves slowly), so it never holds the fast animation timer.
    /// </summary>
    internal sealed class Dot : Control
    {
        private const double PulseMs = 1900;
        private Color from = Theme.Off, to = Theme.Off;
        private readonly Tween colorT;
        private readonly System.Windows.Forms.Timer pulseTimer = new System.Windows.Forms.Timer();
        private double pulseStart;
        private bool pulse;

        public Dot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            colorT = new Tween(1, Invalidate);
            pulseTimer.Interval = 33;
            pulseTimer.Tick += delegate { Invalidate(); };
        }

        public Color Color
        {
            get { return to; }
            set
            {
                if (to == value) return;
                from = Current;
                to = value;
                colorT.Snap(0);
                if (IsHandleCreated && Visible) colorT.To(1, 320);
                else colorT.Snap(1);
            }
        }

        /// <summary>The live ring. Only runs while the window shows it, so it costs nothing in the tray.</summary>
        public bool Pulse
        {
            get { return pulse; }
            set
            {
                if (pulse == value) return;
                pulse = value && Motion.Enabled;
                if (pulse)
                {
                    pulseStart = Motion.Now;
                    pulseTimer.Start();
                }
                else
                {
                    pulseTimer.Stop();
                }
                Invalidate();
            }
        }

        /// <summary>True while the ring's timer runs.</summary>
        internal bool Pulsing
        {
            get { return pulseTimer.Enabled; }
        }

        private Color Current
        {
            get { return Theme.Mix(from, to, colorT.Value); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
            float cx = Width / 2f, cy = Height / 2f, core = 4f;
            Color c = Current;
            if (pulse)
            {
                double p = Motion.EaseOut(((Motion.Now - pulseStart) % PulseMs) / PulseMs);
                float ring = core + (float)(p * (Math.Min(Width, Height) / 2f - core - 0.5f));
                using (var halo = new SolidBrush(Theme.Fade(c, (1 - p) * 0.45)))
                    g.FillEllipse(halo, cx - ring, cy - ring, 2 * ring, 2 * ring);
            }
            using (var dot = new SolidBrush(c)) g.FillEllipse(dot, cx - core, cy - core, 2 * core, 2 * core);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pulseTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>A label whose text cross-fades (and can drift up a little) when it changes.</summary>
    internal sealed class SmoothLabel : Label
    {
        private readonly Tween fadeT;
        private string targetText;
        private Color targetColor, shownColor;
        private bool comingIn;
        private int baseTop = int.MinValue;

        /// <summary>The color behind the label, faded to and from.</summary>
        public Color Behind = Theme.Window;

        /// <summary>How far (in pixels) the new text drifts up into place.</summary>
        public int Rise;

        public SmoothLabel()
        {
            fadeT = new Tween(1, Apply);
        }

        public void Set(string text, Color color, bool animate)
        {
            if (text == targetText && color == targetColor) return;
            targetText = text;
            targetColor = color;
            if (!animate || !IsHandleCreated || !Visible || !Motion.Enabled || Text.Length == 0)
            {
                fadeT.Done = null;
                Text = text;
                shownColor = color;
                comingIn = false;
                fadeT.Snap(1);
                return;
            }
            if (baseTop == int.MinValue) baseTop = Top;
            comingIn = false;
            fadeT.Done = delegate
            {
                Text = targetText;
                shownColor = targetColor;
                comingIn = true;
                fadeT.Done = delegate { comingIn = false; fadeT.Done = null; };
                fadeT.To(1, 200);
            };
            fadeT.To(0, 120);
        }

        private void Apply()
        {
            double a = fadeT.Value;
            ForeColor = Theme.Mix(Behind, shownColor, a);
            if (Rise != 0 && baseTop != int.MinValue)
                Top = comingIn ? baseTop + (int)Math.Round((1 - a) * Rise) : baseTop;
        }
    }

    /// <summary>
    /// Plays a page change: pictures of the old and new page slide sideways and cross-fade, the way modern apps
    /// move between screens, then the real page is shown.
    /// </summary>
    internal sealed class PageSlide : Control
    {
        private Bitmap from, to;
        private int direction;
        private readonly Tween t;
        private Action finished;

        public PageSlide()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Opaque, true);
            t = new Tween(0, Invalidate);
            t.Ease = Motion.EaseOut;
        }

        /// <summary>0 at the start of a change, 1 when the new page is fully in.</summary>
        internal double Progress
        {
            get { return t.Value; }
        }

        /// <summary>Frames drawn so far (self-tests measure the frame rate with it).</summary>
        internal int Frames;

        public void Prepare(Bitmap leaving)
        {
            Release();
            from = leaving;
            t.Done = null;
            t.Snap(0);
            Invalidate();
        }

        public void Play(Bitmap arriving, int dir, double ms, Action done)
        {
            to = arriving;
            direction = dir;
            finished = done;
            t.Done = delegate
            {
                t.Done = null;
                Action f = finished;
                finished = null;
                if (f != null) f();
            };
            t.To(1, ms);
        }

        public void Release()
        {
            if (from != null) from.Dispose();
            if (to != null) to.Dispose();
            from = to = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
            g.Clear(Theme.Window);
            Frames++;
            double p = t.Value;
            float shift = Width * 0.14f;
            // the old page leaves quickly; the new one arrives a beat later
            if (from != null) Draw(g, from, (float)(-direction * shift * p), 1 - Math.Min(1, p / 0.55));
            if (to != null) Draw(g, to, (float)(direction * shift * (1 - p)), Math.Max(0, (p - 0.2) / 0.8));
        }

        private static void Draw(Graphics g, Bitmap bitmap, float x, double alpha)
        {
            if (alpha <= 0.01) return;
            if (alpha >= 0.99)
            {
                g.DrawImageUnscaled(bitmap, (int)Math.Round(x), 0);
                return;
            }
            var matrix = new System.Drawing.Imaging.ColorMatrix();
            matrix.Matrix33 = (float)alpha;
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                g.DrawImage(bitmap, new Rectangle((int)Math.Round(x), 0, bitmap.Width, bitmap.Height),
                    0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Release();
            base.Dispose(disposing);
        }
    }
}
