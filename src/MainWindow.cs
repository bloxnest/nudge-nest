using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace NudgeNest
{
    internal enum Page { Main, Settings, About, Log }

    /// <summary>
    /// The app window, built the same way as BloxNest: the layout and every animation live in MainWindow.xaml
    /// as short WPF storyboards (hover fades, press-and-spring, sliding switch knobs), drawn by WPF on the
    /// graphics card. Nothing animates unless something changes, so an idle window costs no CPU.
    /// Also owns the tray icon. Closing or minimizing hides it to the hidden icons.
    /// </summary>
    internal sealed class MainWindow
    {
        private const string Dash = "—";
        private const string BoltIcon = "", CheckIcon = "", WarnIcon = "";

        private static readonly Color Gray = Hex("#6B717C"), Green = Hex("#46CD82"), Amber = Hex("#F5B94B"), Red = Hex("#F56464");
        private static readonly Brush Mint = Freeze(Hex("#86E3CE")), Light = Freeze(Hex("#EEF1E9")), Muted = Freeze(Hex("#A3AB9C")),
                                      Faint = Freeze(Hex("#929C89")), Danger = Freeze(Hex("#F56464"));

        public readonly Window Window;
        private readonly Settings settings;
        private readonly AntiAfkEngine engine;
        private readonly EventWaitHandle showSignal, exitSignal;
        private RegisteredWaitHandle showWait, exitWait;

        private readonly FrameworkElement windowRoot, mainPanel, settingsPanel, aboutPanel, logPanel;
        private readonly ScaleTransform rootScale;
        private readonly RadioButton offSegment, onSegment;
        private readonly RadioButton[] activitySegments, intervalSegments;
        private readonly TextBlock intervalText, countdownText, countdownCaption, activityHint, nudgeIcon, nudgeText,
                                   statusText, sessionText, nudgesText, lastText, keyValueText, keyHint, logText;
        private readonly SolidColorBrush dotFill;
        private readonly Ellipse dot;
        private readonly Button changeKeyButton;
        private readonly CheckBox optAutoRoblox, optLaunch, optWindows, optPause, optHide, optAwake;
        private readonly ScrollViewer logScroll;
        private readonly DispatcherTimer nudgeReset;
        private FrameworkElement currentPanel;
        private bool loading, quitting, hiding, capturing, useCapturedKey, toldAboutTray;
        private EngineState? shownState;
        private DateTime shownNudge = DateTime.MinValue;

        private Forms.NotifyIcon tray;
        private Drawing.Icon onIcon, standbyIcon, offIcon, haltedIcon, taskbarIcon, smallIcon;
        private Forms.ContextMenuStrip trayMenu;
        private Forms.ToolStripMenuItem onItem, jumpItem, cameraItem, customItem;

        /// <summary>What Exit does. Self-tests replace it so the test run keeps going.</summary>
        internal Action ShutDown = delegate { Application.Current.Shutdown(); };

        public MainWindow(EventWaitHandle showSignal, EventWaitHandle exitSignal)
        {
            this.showSignal = showSignal;
            this.exitSignal = exitSignal;
            settings = Settings.Load();
            engine = new AntiAfkEngine(settings);

            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                Window = (Window)XamlReader.Load(s);

            windowRoot = Find<FrameworkElement>("WindowRoot");
            rootScale = (ScaleTransform)windowRoot.RenderTransform;
            mainPanel = Find<FrameworkElement>("MainPanel");
            settingsPanel = Find<FrameworkElement>("SettingsPanel");
            aboutPanel = Find<FrameworkElement>("AboutPanel");
            logPanel = Find<FrameworkElement>("LogPanel");
            offSegment = Find<RadioButton>("OffSegment");
            onSegment = Find<RadioButton>("OnSegment");
            activitySegments = Find<UniformGrid>("ActivitySegments").Children.OfType<RadioButton>().ToArray();
            intervalSegments = Find<UniformGrid>("IntervalSegments").Children.OfType<RadioButton>().ToArray();
            intervalText = Find<TextBlock>("IntervalText");
            countdownText = Find<TextBlock>("CountdownText");
            countdownCaption = Find<TextBlock>("CountdownCaption");
            activityHint = Find<TextBlock>("ActivityHint");
            nudgeIcon = Find<TextBlock>("NudgeIcon");
            nudgeText = Find<TextBlock>("NudgeText");
            statusText = Find<TextBlock>("StatusText");
            sessionText = Find<TextBlock>("SessionText");
            nudgesText = Find<TextBlock>("NudgesText");
            lastText = Find<TextBlock>("LastText");
            keyValueText = Find<TextBlock>("KeyValueText");
            keyHint = Find<TextBlock>("KeyHint");
            logText = Find<TextBlock>("LogText");
            logScroll = Find<ScrollViewer>("LogScroll");
            dot = Find<Ellipse>("Dot");
            changeKeyButton = Find<Button>("ChangeKeyButton");
            optAutoRoblox = Find<CheckBox>("OptAutoRoblox");
            optLaunch = Find<CheckBox>("OptLaunch");
            optWindows = Find<CheckBox>("OptWindows");
            optPause = Find<CheckBox>("OptPause");
            optHide = Find<CheckBox>("OptHide");
            optAwake = Find<CheckBox>("OptAwake");
            Find<TextBlock>("VersionText").Text = "Version " + AppInfo.Version;
            currentPanel = mainPanel;

            // the status dot's colour fades between states, so it gets its own brush
            dotFill = new SolidColorBrush(Gray);
            dot.Fill = dotFill;

            nudgeReset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            nudgeReset.Tick += delegate
            {
                nudgeReset.Stop();
                nudgeIcon.Text = BoltIcon;
                nudgeText.Text = "Nudge now";
            };

            CreateTray();
            RoundCorners();
            Window.SourceInitialized += delegate { SetWindowIcons(); };
            WireWindow();
            WirePower();
            WireActivity();
            WireOptions();
            WireKeyCapture();
            ShowValues();

            engine.Changed += delegate { Render(); };
            engine.Halted += delegate (string reason)
            {
                tray.ShowBalloonTip(6000, "NudgeNest stopped nudging",
                    char.ToUpperInvariant(reason[0]) + reason.Substring(1) + ". Open the activity log for details.",
                    Forms.ToolTipIcon.Warning);
            };
            Logger.Written += delegate (string line) { if (currentPanel == logPanel) AppendLog(line); };

            StartWithWindows.RefreshPath();
            Logger.Write(AppInfo.Name + " " + AppInfo.Version + " opened");
            if (settings.TurnOnAtLaunch) engine.Start();
            Render();

            // a second copy of the app signals these instead of starting (see Program)
            showWait = OnSignal(showSignal, ShowFromTray);
            exitWait = OnSignal(exitSignal, Quit);
            Window.Loaded += delegate { AnimateIn(); };
        }

        internal AntiAfkEngine Engine { get { return engine; } }
        internal Forms.ContextMenuStrip TrayMenu { get { return trayMenu; } }

        internal Page CurrentPage
        {
            get
            {
                return currentPanel == settingsPanel ? Page.Settings : currentPanel == aboutPanel ? Page.About
                    : currentPanel == logPanel ? Page.Log : Page.Main;
            }
        }

        private T Find<T>(string name) where T : class
        {
            return (T)Window.FindName(name);
        }

        private static Color Hex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // Keeps the title bar's hover colours inside the rounded corners
        private void RoundCorners()
        {
            var content = Find<FrameworkElement>("ContentRoot");
            content.SizeChanged += delegate { content.Clip = new RectangleGeometry(new Rect(content.RenderSize), 9, 9); };
        }

        private const uint WM_SETICON = 0x0080;
        private static readonly IntPtr IconSmall = IntPtr.Zero, IconBig = new IntPtr(1);

        /// <summary>
        /// The taskbar shows the window's big icon at 24 px (at 100% scaling). Left alone, Windows shrinks the
        /// 32 px picture and blurs it, so the window gets the icon file's picture drawn for exactly that size.
        /// </summary>
        private void SetWindowIcons()
        {
            IntPtr hWnd = new WindowInteropHelper(Window).Handle;
            double scale = VisualTreeHelper.GetDpi(Window).DpiScaleX;
            taskbarIcon = AppIcon((int)Math.Round(24 * scale));
            smallIcon = AppIcon((int)Math.Round(16 * scale));
            if (taskbarIcon != null) NativeMethods.SendMessage(hWnd, WM_SETICON, IconBig, taskbarIcon.Handle);
            if (smallIcon != null) NativeMethods.SendMessage(hWnd, WM_SETICON, IconSmall, smallIcon.Handle);
        }

        /// <summary>The picture closest to this size from the icon file built into the exe (assets\icon.ico).</summary>
        private static Drawing.Icon AppIcon(int size)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("NudgeNest.icon.ico"))
                return s == null ? null : new Drawing.Icon(s, size, size);
        }

        internal Drawing.Size TaskbarIconSize
        {
            get { return taskbarIcon == null ? Drawing.Size.Empty : taskbarIcon.Size; }
        }

        // ---------- window animations and the hidden icons (same as BloxNest) ----------

        private void AnimateIn()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            windowRoot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        private void AnimateOut(Action then)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            fade.Completed += delegate { then(); };
            windowRoot.BeginAnimation(UIElement.OpacityProperty, fade);
            rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
            rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        }

        private void WireWindow()
        {
            Find<Button>("MinButton").Click += delegate { HideToTray(); };
            Find<Button>("CloseButton").Click += delegate { HideToTray(); };
            Find<Button>("SettingsButton").Click += delegate { ShowPanel(currentPanel == settingsPanel ? mainPanel : settingsPanel); };
            Find<Button>("AboutButton").Click += delegate { ShowPanel(currentPanel == aboutPanel ? mainPanel : aboutPanel); };
            Find<Button>("SettingsBackButton").Click += delegate { ShowPanel(mainPanel); };
            Find<Button>("BackButton").Click += delegate { ShowPanel(mainPanel); };
            Find<Button>("LogBackButton").Click += delegate { ShowPanel(settingsPanel); };
            Find<Button>("ViewLogButton").Click += delegate { ShowPanel(logPanel); };
            Find<Button>("OpenLogButton").Click += delegate { if (File.Exists(Logger.FilePath)) Process.Start(Logger.FilePath); };
            Find<Button>("WebsiteButton").Click += delegate { AppInfo.Open(AppInfo.Website); };
            Find<Button>("SourceButton").Click += delegate { AppInfo.Open(AppInfo.Source); };
            Find<Button>("NudgeButton").Click += delegate { NudgeNow(); };

            Window.StateChanged += delegate { if (Window.WindowState == WindowState.Normal) AnimateIn(); };
            Window.Closing += (sender, e) =>
            {
                // Alt+F4 and the like: same as the close button
                if (quitting) return;
                e.Cancel = true;
                if (!hiding) HideToTray();
            };
        }

        /// <summary>Close or minimize: fade out, then tuck into the hidden icons.</summary>
        private void HideToTray()
        {
            if (hiding || !Window.IsVisible) return;
            hiding = true;
            AnimateOut(delegate
            {
                Window.Hide();
                hiding = false;
                ShowPanel(mainPanel, false);
                if (!toldAboutTray)
                {
                    toldAboutTray = true;
                    tray.ShowBalloonTip(4000, "NudgeNest is still running",
                        "It's in your hidden icons. Right-click it to quit.", Forms.ToolTipIcon.None);
                }
                NativeMethods.TrimMemory();
            });
        }

        internal void ShowFromTray()
        {
            Window.Show();
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            Window.Activate();
            Render();
            AnimateIn();
        }

        internal void Quit()
        {
            if (quitting) return;
            quitting = true;
            Logger.Write(AppInfo.Name + " closed");
            engine.Dispose();
            if (showWait != null) showWait.Unregister(null);
            if (exitWait != null) exitWait.Unregister(null);
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            if (Window.IsVisible) AnimateOut(delegate { Window.Close(); ShutDown(); });
            else { Window.Close(); ShutDown(); }
        }

        private RegisteredWaitHandle OnSignal(WaitHandle signal, Action action)
        {
            if (signal == null) return null;
            return ThreadPool.RegisterWaitForSingleObject(signal,
                delegate { Window.Dispatcher.BeginInvoke(action); }, null, -1, false);
        }

        /// <summary>Swaps Main, Options, About and the log with a short slide + fade (same as BloxNest).</summary>
        internal void ShowPanel(Page page)
        {
            ShowPanel(page == Page.Settings ? settingsPanel : page == Page.About ? aboutPanel : page == Page.Log ? logPanel : mainPanel);
        }

        private void ShowPanel(FrameworkElement panel, bool animate = true)
        {
            if (panel == currentPanel) return;
            if (capturing) EndKeyCapture();
            foreach (FrameworkElement p in new[] { mainPanel, settingsPanel, aboutPanel, logPanel })
                p.Visibility = p == panel ? Visibility.Visible : Visibility.Hidden;
            bool back = panel == mainPanel || (panel == settingsPanel && currentPanel == logPanel);
            currentPanel = panel;
            if (panel == logPanel) FillLog();
            if (panel == mainPanel) Render();
            if (!animate) return;

            var slide = new TranslateTransform(back ? -14 : 14, 0);
            panel.RenderTransform = slide;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }

        // ---------- Off / On ----------

        private void WirePower()
        {
            onSegment.Checked += delegate { if (!loading) engine.Start(); };
            offSegment.Checked += delegate { if (!loading) engine.Stop(); };
        }

        // ---------- activity mode ----------

        private void WireActivity()
        {
            for (int i = 0; i < activitySegments.Length; i++)
            {
                var mode = (NudgeAction)i;
                activitySegments[i].Checked += delegate { if (!loading) ChooseActivity(mode); };
            }
        }

        private void ChooseActivity(NudgeAction mode)
        {
            if (mode == NudgeAction.CustomKey && settings.CustomKey == Forms.Keys.None)
            {
                // no key yet: pick one first (on the Options page), then switch to it
                ShowValues();
                ShowPanel(settingsPanel);
                StartKeyCapture(true);
                return;
            }
            if (settings.Action != mode)
            {
                settings.Action = mode;
                settings.Save();
                Logger.Write("Activity mode: " + AntiAfkEngine.Describe(mode, settings.CustomKey));
            }
            ShowValues();
        }

        private string ActivityHelp()
        {
            if (settings.Action == NudgeAction.CameraNudge) return "Turns the camera a hair and back. Keeps you seated.";
            if (settings.Action == NudgeAction.CustomKey) return "Taps " + KeyChoice.Name(settings.CustomKey) + ". Change it in Options.";
            return "Taps Space. Stands you up if you're sitting.";
        }

        // ---------- options ----------

        private void WireOptions()
        {
            for (int i = 0; i < intervalSegments.Length; i++)
            {
                int minutes = Settings.IntervalChoices[i];
                intervalSegments[i].Checked += delegate
                {
                    if (loading) return;
                    settings.IntervalMinutes = minutes;
                    settings.Save();
                    Logger.Write("Nudge interval: " + AntiAfkEngine.Minutes(minutes));
                    Render();
                };
            }
            Option(optAutoRoblox, on => { settings.AutoStartWithRoblox = on; return "Turn on automatically when Roblox starts: " + (on ? "yes" : "no"); });
            Option(optLaunch, on => { settings.TurnOnAtLaunch = on; return null; });
            Option(optPause, on => { settings.WaitForPause = on; return null; });
            Option(optHide, on => { settings.HideRoblox = on; return null; });
            Option(optAwake, on => { settings.KeepPcAwake = on; return null; });
            Option(optWindows, on =>
            {
                if (!StartWithWindows.Set(on))
                {
                    Logger.Write("Couldn't change the Start with Windows setting");
                    Window.Dispatcher.BeginInvoke(new Action(ShowValues));
                }
                return null;
            });
        }

        private void Option(CheckBox box, Func<bool, string> apply)
        {
            RoutedEventHandler changed = delegate
            {
                if (loading) return;
                string log = apply(box.IsChecked == true);
                settings.Save();
                if (log != null) Logger.Write(log);
                Render();
            };
            box.Checked += changed;
            box.Unchecked += changed;
        }

        /// <summary>Shows the saved settings on every page and in the tray menu.</summary>
        private void ShowValues()
        {
            loading = true;
            activitySegments[(int)settings.Action].IsChecked = true;
            int index = Array.IndexOf(Settings.IntervalChoices, settings.IntervalMinutes);
            intervalSegments[index >= 0 ? index : 3].IsChecked = true;
            optAutoRoblox.IsChecked = settings.AutoStartWithRoblox;
            optLaunch.IsChecked = settings.TurnOnAtLaunch;
            optWindows.IsChecked = StartWithWindows.IsOn;
            optPause.IsChecked = settings.WaitForPause;
            optHide.IsChecked = settings.HideRoblox;
            optAwake.IsChecked = settings.KeepPcAwake;
            loading = false;
            activityHint.Text = ActivityHelp();
            if (!capturing) keyValueText.Text = settings.CustomKey == Forms.Keys.None ? "Not set" : KeyChoice.Name(settings.CustomKey);
            string custom = settings.CustomKey == Forms.Keys.None ? "Custom key..." : "Custom key (" + KeyChoice.Name(settings.CustomKey) + ")";
            customItem.Text = custom;
            jumpItem.Checked = settings.Action == NudgeAction.Jump;
            cameraItem.Checked = settings.Action == NudgeAction.CameraNudge;
            customItem.Checked = settings.Action == NudgeAction.CustomKey;
        }

        // ---------- custom key: picked right on the Options page ----------

        private void WireKeyCapture()
        {
            changeKeyButton.Click += delegate
            {
                if (capturing) EndKeyCapture();
                else StartKeyCapture(false);
            };
            Window.PreviewKeyDown += (sender, e) =>
            {
                if (!capturing)
                {
                    if (e.Key == Key.Escape && currentPanel != mainPanel)
                    {
                        ShowPanel(currentPanel == logPanel ? settingsPanel : mainPanel);
                        e.Handled = true;
                    }
                    return;
                }
                e.Handled = true;
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Escape)
                {
                    EndKeyCapture();
                    return;
                }
                OfferKey((Forms.Keys)KeyInterop.VirtualKeyFromKey(key));
            };
        }

        internal void StartKeyCapture(bool thenUseIt)
        {
            capturing = true;
            useCapturedKey = thenUseIt;
            keyValueText.Text = "Press a key...";
            keyValueText.Foreground = Mint;
            keyHint.Text = "Letters, numbers, F-keys, arrows and punctuation work. Esc cancels.";
            keyHint.Foreground = Muted;
            changeKeyButton.Content = "Cancel";
            Window.Activate();
            Keyboard.Focus(Window);
        }

        private void EndKeyCapture()
        {
            capturing = false;
            useCapturedKey = false;
            keyValueText.Foreground = Light;
            keyHint.Text = "Esc, Enter, / and other troublesome keys aren't allowed.";
            keyHint.Foreground = Muted;
            changeKeyButton.Content = "Change";
            ShowValues();
        }

        /// <summary>Takes the key, or explains (with a little shake) why it can't be used.</summary>
        internal bool OfferKey(Forms.Keys key)
        {
            string problem = KeyChoice.Problem(key);
            if (problem != null)
            {
                keyHint.Text = KeyChoice.Name(key) + " can't be used: " + problem + ".";
                keyHint.Foreground = Danger;
                Shake(keyHint);
                return false;
            }
            settings.CustomKey = key;
            if (useCapturedKey) settings.Action = NudgeAction.CustomKey;
            settings.Save();
            Logger.Write("Custom key set to " + KeyChoice.Name(key));
            EndKeyCapture();
            return true;
        }

        internal string KeyMessage
        {
            get { return keyHint.Text; }
        }

        private static void Shake(UIElement element)
        {
            var move = (TranslateTransform)element.RenderTransform;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
            double[] at = { 0, -6, 6, -4, 4, -2, 0 };
            for (int i = 0; i < at.Length; i++)
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(at[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 60))));
            move.BeginAnimation(TranslateTransform.XProperty, shake);
        }

        // ---------- Nudge now ----------

        private void NudgeNow()
        {
            engine.NudgeNow();
            if (engine.RobloxCount == 0) Confirm(WarnIcon, "Roblox isn't running");
            else if (engine.LastNudgeWorked) Confirm(CheckIcon, "Nudged");
            else Confirm(WarnIcon, "Didn't go through. See the log");
        }

        private void Confirm(string icon, string text)
        {
            nudgeIcon.Text = icon;
            nudgeText.Text = text;
            var content = (UIElement)nudgeText.Parent;
            content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(200)));
            nudgeReset.Stop();
            nudgeReset.Start();
        }

        // ---------- status ----------

        private void Render()
        {
            EngineState state = engine.State;
            loading = true;
            onSegment.IsChecked = engine.Running;
            offSegment.IsChecked = !engine.Running;
            loading = false;
            onItem.Checked = engine.Running;

            if (shownState != state)
            {
                shownState = state;
                tray.Icon = state == EngineState.Active ? onIcon : state == EngineState.Standby ? standbyIcon
                    : state == EngineState.Halted ? haltedIcon : offIcon;
                Color c = state == EngineState.Active ? Green : state == EngineState.Standby ? Amber
                    : state == EngineState.Halted ? Red : Gray;
                dotFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(300)));
            }
            if (engine.LastNudge != shownNudge)
            {
                // a short blink of the dot for each nudge, like BloxNest's busy pulse
                shownNudge = engine.LastNudge;
                if (shownNudge != DateTime.MinValue)
                    dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(350))
                    {
                        AutoReverse = true,
                        RepeatBehavior = new RepeatBehavior(2),
                    });
            }

            intervalText.Text = "Nudges after " + AntiAfkEngine.Minutes(settings.IntervalMinutes) + " idle";
            string big = Dash, caption, status, tip;
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
                        caption = engine.UserPlaying ? "You're playing, so the timer keeps resetting." : "until the next nudge";
                        tip = "on, next nudge in " + Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + " min";
                    }
                    status = "On. " + RobloxStatus() + ".";
                    break;
                case EngineState.Standby:
                    caption = "Waiting for Roblox to start.";
                    status = "On standby. NudgeNest takes over when Roblox opens.";
                    tip = "on standby";
                    break;
                case EngineState.Halted:
                    caption = "Stopped after a problem.";
                    status = "Stopped: a window couldn't be put back exactly. Options > Activity log has the details.";
                    tip = "stopped after a problem";
                    break;
                default:
                    caption = "Anti-AFK is off.";
                    status = engine.RobloxCount > 0 ? "Off. " + RobloxStatus() + ", but it isn't being kept active."
                        : settings.AutoStartWithRoblox ? "Off. Turns on by itself when Roblox starts." : "Off. Turn it on to keep Roblox active.";
                    tip = "off";
                    break;
            }
            Set(countdownText, big);
            Set(countdownCaption, caption);
            Set(statusText, status);
            Set(sessionText, engine.SessionStart == DateTime.MinValue ? Dash : Duration(DateTime.Now - engine.SessionStart));
            Set(nudgesText, engine.NudgesOk + " ok · " + engine.NudgesFailed + " failed");
            Set(lastText, engine.LastNudge == DateTime.MinValue ? Dash : engine.LastNudge.ToString("t"));
            lastText.Foreground = engine.LastNudge != DateTime.MinValue && !engine.LastNudgeWorked ? Danger : Light;

            tip = AppInfo.Name + ": " + tip;
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (tray.Text != tip) tray.Text = tip;
        }

        private string RobloxStatus()
        {
            if (engine.UserPlaying) return "You're playing Roblox";
            string where = engine.RobloxInFront ? "in front" : engine.RobloxMinimized ? "minimized" : "in the background";
            string text = "Roblox is " + where + ", idle " + Clock(engine.RobloxIdle);
            if (engine.RobloxCount > 1) text += " (" + engine.RobloxCount + " windows)";
            return text;
        }

        private static void Set(TextBlock block, string text)
        {
            if (block.Text != text) block.Text = text;
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

        // ---------- activity log page ----------

        private void FillLog()
        {
            logText.Text = string.Join("\n", Logger.Recent);
            logScroll.ScrollToEnd();
        }

        private void AppendLog(string line)
        {
            logText.Text = logText.Text.Length == 0 ? line : logText.Text + "\n" + line;
            logScroll.ScrollToEnd();
        }

        // ---------- the hidden-icons icon and its menu ----------

        private void CreateTray()
        {
            int size = Forms.SystemInformation.SmallIconSize.Width;
            onIcon = TrayIcons.Make(TrayIcons.On, size);
            standbyIcon = TrayIcons.Make(TrayIcons.Standby, size);
            offIcon = TrayIcons.Make(TrayIcons.Off, size);
            haltedIcon = TrayIcons.Make(TrayIcons.Stopped, size);

            tray = new Forms.NotifyIcon { Text = AppInfo.Name, Icon = offIcon };
            trayMenu = new Forms.ContextMenuStrip
            {
                Renderer = new TrayMenuRenderer(),
                Font = new Drawing.Font("Segoe UI", 9.5f),
                Padding = new Forms.Padding(2, 4, 2, 4),
            };
            onItem = TrayItem("Anti-AFK On", false, delegate { if (engine.Running) engine.Stop(); else engine.Start(); });
            var activity = TrayItem("Activity Mode", false, null);
            jumpItem = TrayItem("Jump (Space)", false, delegate { ChooseActivity(NudgeAction.Jump); });
            cameraItem = TrayItem("Camera nudge (→ then ←)", false, delegate { ChooseActivity(NudgeAction.CameraNudge); });
            customItem = TrayItem("Custom key...", false, delegate
            {
                if (settings.CustomKey == Forms.Keys.None) ShowFromTray();
                ChooseActivity(NudgeAction.CustomKey);
            });
            activity.DropDownItems.AddRange(new Forms.ToolStripItem[] { jumpItem, cameraItem, customItem });
            ((Forms.ToolStripDropDownMenu)activity.DropDown).Renderer = trayMenu.Renderer;
            trayMenu.Items.AddRange(new Forms.ToolStripItem[]
            {
                onItem,
                TrayItem("Nudge Now", false, delegate { engine.NudgeNow(); }),
                activity,
                new Forms.ToolStripSeparator(),
                TrayItem("Open " + AppInfo.Name, true, delegate { ShowFromTray(); }),
                new Forms.ToolStripSeparator(),
                TrayItem("Exit", false, delegate { Quit(); }),
            });
            tray.ContextMenuStrip = trayMenu;
            tray.MouseClick += (sender, e) => { if (e.Button == Forms.MouseButtons.Left) ShowFromTray(); };
            tray.BalloonTipClicked += delegate { ShowFromTray(); };
            tray.Visible = true;
        }

        private static Forms.ToolStripMenuItem TrayItem(string text, bool bold, EventHandler onClick)
        {
            var item = new Forms.ToolStripMenuItem(text)
            {
                ForeColor = Drawing.Color.FromArgb(0xEE, 0xF1, 0xE9),
                Padding = new Forms.Padding(6, 5, 18, 5),
            };
            if (bold) item.Font = new Drawing.Font("Segoe UI Semibold", 9.5f);
            if (onClick != null) item.Click += onClick;
            return item;
        }
    }

    /// <summary>The dark right-click menu of the hidden-icons icon: light arrows and a mint tick.</summary>
    internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private static readonly Drawing.Color Light = Drawing.Color.FromArgb(0xEE, 0xF1, 0xE9);
        private static readonly Drawing.Color Mint = Drawing.Color.FromArgb(0x86, 0xE3, 0xCE);

        public TrayMenuRenderer() : base(new TrayMenuColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Light;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            Drawing.Rectangle r = e.ImageRectangle;
            float s = r.Height / 16f, x = r.Left + r.Width / 2f, y = r.Top + r.Height / 2f;
            e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Drawing.Pen(Mint, 1.8f * s))
            {
                pen.StartCap = pen.EndCap = Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = Drawing.Drawing2D.LineJoin.Round;
                e.Graphics.DrawLines(pen, new[]
                {
                    new Drawing.PointF(x - 4.5f * s, y + 0.2f * s),
                    new Drawing.PointF(x - 1.5f * s, y + 3.2f * s),
                    new Drawing.PointF(x + 4.5f * s, y - 3.3f * s),
                });
            }
        }
    }

    /// <summary>Colours for the dark right-click menu of the hidden-icons icon (same as BloxNest's).</summary>
    internal sealed class TrayMenuColors : Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Back = Drawing.Color.FromArgb(0x21, 0x26, 0x1E);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(0x30, 0x37, 0x2C);
        private static readonly Drawing.Color Line = Drawing.Color.FromArgb(0x33, 0x3B, 0x2D);

        public override Drawing.Color ToolStripDropDownBackground { get { return Back; } }
        public override Drawing.Color ImageMarginGradientBegin { get { return Back; } }
        public override Drawing.Color ImageMarginGradientMiddle { get { return Back; } }
        public override Drawing.Color ImageMarginGradientEnd { get { return Back; } }
        public override Drawing.Color MenuBorder { get { return Line; } }
        public override Drawing.Color MenuItemBorder { get { return Hover; } }
        public override Drawing.Color MenuItemSelected { get { return Hover; } }
        public override Drawing.Color MenuItemSelectedGradientBegin { get { return Hover; } }
        public override Drawing.Color MenuItemSelectedGradientEnd { get { return Hover; } }
        public override Drawing.Color SeparatorDark { get { return Line; } }
        public override Drawing.Color SeparatorLight { get { return Back; } }
        public override Drawing.Color CheckBackground { get { return Back; } }
        public override Drawing.Color CheckSelectedBackground { get { return Hover; } }
        public override Drawing.Color CheckPressedBackground { get { return Hover; } }
    }
}
