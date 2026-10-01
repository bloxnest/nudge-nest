using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using N = NudgeNest.NativeMethods;

namespace NudgeNest
{
    /// <summary>
    /// Self-tests for the engine, the nudge, the safety checks and the tray menu. They drive the real code
    /// against stand-in Roblox windows (FakeRoblox.cs), never the real game, with the engine's minutes shortened
    /// to seconds. Run tests\run-tests.bat; it moves focus and the mouse around for about two minutes.
    /// Logs, settings and screenshots go to tests\bin, never to %APPDATA%.
    /// </summary>
    internal static class SelfTest
    {
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        private static string bin;
        private static int passed, failed;
        private static readonly List<Fake> fakes = new List<Fake>();

        private sealed class Fake
        {
            public Process Process;
            public IntPtr Window;
            public string LogPath;

            /// <summary>Each line of the stand-in's log: tick, event, argument.</summary>
            public List<string[]> Events()
            {
                var events = new List<string[]>();
                try
                {
                    using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(fs))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) events.Add(line.Split(' '));
                    }
                }
                catch (IOException) { }
                return events;
            }

            public List<int> KeyDowns()
            {
                var keys = new List<int>();
                foreach (string[] e in Events())
                    if (e.Length >= 3 && e[1] == "down") keys.Add(int.Parse(e[2], NumberStyles.HexNumber));
                return keys;
            }

            public int LastKeyTick()
            {
                int tick = 0;
                foreach (string[] e in Events())
                    if (e.Length >= 3 && e[1] == "down") tick = int.Parse(e[0]);
                return tick;
            }

            public void Close()
            {
                PostMessage(Window, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE, like closing Roblox
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            var only = new List<string>(args);   // e.g. "flicker" runs just that part; none = everything
            Func<string, bool> run = part => only.Count == 0 || only.Contains(part);
            bin = Path.GetDirectoryName(Application.ExecutablePath);
            Logger.Folder = Path.Combine(bin, "logs");
            Settings.Folder = Path.Combine(bin, "settings");
            foreach (string folder in new[] { Logger.Folder, Settings.Folder })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            StartWithWindows.LeaveAlone = true;
            RobloxWindows.OnlyPids = new HashSet<uint>();
            Timing.Minute = TimeSpan.FromSeconds(1);
            Timing.TickMs = 200;
            Application.EnableVisualStyles();

            N.POINT mouse;
            N.GetCursorPos(out mouse);
            IntPtr yourWindow = N.GetForegroundWindow();
            try
            {
                if (run("keys")) TestKeys();
                if (run("settings")) TestSettings();
                if (run("log")) TestLogRotation();
                if (run("engine")) TestEngine();
                if (run("flicker")) TestNoFlicker();
                if (run("window")) TestWindow();
            }
            catch (Exception e)
            {
                Check(false, "no crash: " + e);
            }
            finally
            {
                foreach (Fake f in fakes)
                {
                    try { if (!f.Process.HasExited) f.Process.Kill(); }
                    catch (Exception) { }
                }
                N.SetCursorPos(mouse.X, mouse.Y);
                if (yourWindow != IntPtr.Zero) Nudger.BringToFront(yourWindow);
            }
            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed;
        }

        // ---------- small pieces ----------

        private static void TestKeys()
        {
            Section("Custom key");
            Check(KeyChoice.Problem(Keys.K) == null && KeyChoice.Problem(Keys.F2) == null && KeyChoice.Problem(Keys.Left) == null,
                "letters, F-keys and arrows can be the custom key");
            Check(KeyChoice.Problem(Keys.Escape) != null && KeyChoice.Problem(Keys.CapsLock) != null
                && KeyChoice.Problem(Keys.LWin) != null && KeyChoice.Problem(Keys.Enter) != null
                && KeyChoice.Problem(Keys.OemQuestion) != null && KeyChoice.Problem(Keys.Menu) != null,
                "Esc, Caps Lock, Windows, Enter, / and Alt are refused");
            Check(KeyChoice.Name(Keys.D7) == "7" && KeyChoice.Name(Keys.Left) == "Left arrow" && KeyChoice.Name(Keys.K) == "K",
                "keys get readable names");
            Check(KeyChoice.IsExtended(Keys.Left) && !KeyChoice.IsExtended(Keys.K), "arrow keys are sent as extended keys");
            using (var picker = new KeyCaptureForm())
            {
                picker.Offer(Keys.CapsLock);
                Check(picker.Key == Keys.None && picker.ProblemText.Contains("whole PC"), "the key picker explains a refused key");
                picker.Offer(Keys.K);
                Check(picker.Key == Keys.K && picker.DialogResult == DialogResult.OK, "the key picker accepts K");
            }
        }

        private static void TestSettings()
        {
            Section("Settings");
            var s = new Settings();
            s.Action = NudgeAction.CustomKey;
            s.CustomKey = Keys.K;
            s.AutoStartWithRoblox = false;
            s.IntervalMinutes = 15;
            s.Save();
            Settings loaded = Settings.Load();
            Check(loaded.Action == NudgeAction.CustomKey && loaded.CustomKey == Keys.K && !loaded.AutoStartWithRoblox
                && loaded.IntervalMinutes == 15, "activity mode, custom key and options survive a restart");
            File.WriteAllText(Path.Combine(Settings.Folder, "settings.ini"), "action=custom\r\ncustomKey=27\r\n");
            Check(Settings.Load().Action == NudgeAction.Jump, "a refused custom key (Esc) in the file falls back to Jump");
            Directory.Delete(Settings.Folder, true);
        }

        private static void TestLogRotation()
        {
            Section("Log file");
            long saved = Logger.MaxFileBytes;
            Logger.MaxFileBytes = 2000;
            for (int i = 0; i < 200; i++) Logger.Write("rotation test line " + i);
            var log = new FileInfo(Logger.FilePath);
            var old = new FileInfo(Path.Combine(Logger.Folder, "log.old.txt"));
            Check(log.Exists && log.Length < 2200 && old.Exists && old.Length < 2200,
                "the log rotates instead of growing (" + log.Length + " + " + (old.Exists ? old.Length : 0) + " bytes)");
            Check(File.ReadAllText(Logger.FilePath).Contains("line 199"), "the newest entries are kept");
            Logger.MaxFileBytes = saved;
            log.Delete();
            old.Delete();
        }

        // ---------- the engine against stand-in Roblox windows ----------

        private static void TestEngine()
        {
            Fake other = Launch("OtherApp.exe", "other", "--class=OtherWindow --x=40 --y=40");
            Nudger.BringToFront(other.Window);
            var s = new Settings();
            s.IntervalMinutes = 5;          // 5 s here
            s.WaitForPause = false;
            s.KeepPcAwake = false;
            s.HideRoblox = true;
            s.AutoStartWithRoblox = true;
            s.Action = NudgeAction.Jump;

            using (var engine = new AntiAfkEngine(s))
            {
                Section("Auto start with Roblox");
                Pump(0.6);
                Check(engine.State == EngineState.Off && engine.RobloxCount == 0, "off while Roblox isn't running");
                Fake a = LaunchRoblox("a", "--start=minimized --x=700 --y=160");
                Check(PumpUntil(() => engine.RobloxCount == 1, 8), "notices Roblox opening");
                Check(engine.State == EngineState.Active, "turns on automatically when Roblox starts");
                Check(engine.SessionStart != DateTime.MinValue, "starts the session clock");
                Check(LogHas("Roblox detected") && LogHas("started automatically"), "logs both");
                Pump(3);
                Check(a.KeyDowns().Count == 0, "doesn't nudge a Roblox that just opened");
                Check(PumpUntil(() => a.KeyDowns().Count >= 1, 4), "nudges minimized Roblox after the interval");
                Pump(0.3);
                Check(a.KeyDowns().Count >= 1 && a.KeyDowns()[0] == 0x20, "Jump taps Space");
                Check(N.IsIconic(a.Window), "Roblox is minimized again afterwards");
                Check(N.GetForegroundWindow() == other.Window, "your window has the focus again");
                Check(engine.NudgesOk == 1 && engine.NudgesFailed == 0, "the session counts 1 successful nudge");
                Check(LogHas("Nudge OK: jump"), "the nudge is logged");

                Section("Smart AFK detection");
                int firstTick = a.LastKeyTick();
                int count = a.KeyDowns().Count;
                BusyFor(3.5);   // you, busy in another app
                Check(engine.RobloxIdle >= TimeSpan.FromSeconds(3), "using another app doesn't reset Roblox's AFK timer");
                Check(PumpUntil(() => a.KeyDowns().Count > count, 4), "so the next nudge still comes on time");
                double gap = unchecked(a.LastKeyTick() - firstTick) / 1000.0;
                Check(gap > 4.4 && gap < 6.4, "...one interval after the last nudge (" + gap.ToString("0.0") + " s)");
                a.Close();
                Check(PumpUntil(() => engine.RobloxCount == 0, 8), "notices Roblox closing");
                Check(engine.State == EngineState.Standby, "goes on standby when Roblox closes");
                Check(engine.SessionStart == DateTime.MinValue && engine.NudgesOk == 0 && engine.NudgesFailed == 0
                    && engine.LastNudge == DateTime.MinValue, "resets the session statistics");
                Check(LogHas("Roblox closed"), "logs it");

                Fake b = LaunchRoblox("b", "--start=normal --x=700 --y=160");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(b.Window);
                N.RECT rb = RectOf(b.Window);
                PlayFor(8, (rb.Left + rb.Right) / 2, (rb.Top + rb.Bottom) / 2);   // longer than the interval
                Check(b.KeyDowns().Count == 0, "no nudges while you're playing Roblox");
                Check(engine.UserPlaying, "shows that you're playing");
                Nudger.BringToFront(other.Window);
                DateTime stopped = DateTime.UtcNow;
                Check(PumpUntil(() => b.KeyDowns().Count >= 1, 8), "nudges once Roblox itself has been idle for the interval");
                double after = (DateTime.UtcNow - stopped).TotalSeconds;
                Check(after > 4.0 && after < 6.6, "...counted from your last Roblox input (" + after.ToString("0.0") + " s)");

                Section("Activity modes, with Roblox open behind your window");
                N.RECT before = RectOf(b.Window);
                N.POINT mouse = Mouse();
                s.Action = NudgeAction.CameraNudge;
                Check(Same(NewKeys(b, engine), new[] { 0x27, 0x25 }), "Camera nudge taps Right, then Left");
                s.Action = NudgeAction.CustomKey;
                s.CustomKey = Keys.K;
                Check(Same(NewKeys(b, engine), new[] { 0x4B }), "Custom key taps the chosen key (K)");
                s.CustomKey = Keys.Home;
                Check(Same(NewKeys(b, engine), new[] { 0x24 }), "...including navigation keys (Home)");
                s.Action = NudgeAction.Jump;
                Check(Same(NewKeys(b, engine), new[] { 0x20 }), "Jump taps Space");
                Check(SameRect(RectOf(b.Window), before) && !N.IsIconic(b.Window), "Roblox keeps its position and size");
                Check(N.GetForegroundWindow() == other.Window, "your window keeps the focus");
                Check(SamePoint(Mouse(), mouse), "the mouse pointer doesn't move");
                Check(engine.NudgesFailed == 0 && engine.State == EngineState.Active, "no failed nudges");
                b.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);

                Section("Maximized Roblox that was minimized");
                Fake c = LaunchRoblox("c", "--start=maxmin");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(other.Window);
                N.WINDOWPLACEMENT p0 = PlacementOf(c.Window);
                engine.NudgeNow();
                Pump(0.3);
                N.WINDOWPLACEMENT p1 = PlacementOf(c.Window);
                Check(c.KeyDowns().Count >= 1, "the nudge reaches it");
                Check(N.IsIconic(c.Window) && (p1.flags & N.WPF_RESTORETOMAXIMIZED) != 0
                    && SameRect(p1.rcNormalPosition, p0.rcNormalPosition), "it's minimized again and still opens maximized");
                Check(N.GetForegroundWindow() == other.Window, "your window has the focus again");
                c.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);

                Section("Safety checks after every nudge");
                Fake d = LaunchRoblox("d", "--start=normal --misbehave=move --x=700 --y=160");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(other.Window);
                N.RECT rd = RectOf(d.Window);
                engine.NudgeNow();
                Pump(0.3);
                Check(SameRect(RectOf(d.Window), rd), "a Roblox window that moved during a nudge is put back");
                Check(engine.Running && LogHas("so Anti-AFK put it back"), "...that's logged, and nudging carries on");
                engine.NudgeNow();
                Pump(0.3);
                Check(engine.State == EngineState.Halted, "needing that twice in a row stops automatic nudging");
                Check(LogHas("Automatic nudging stopped"), "...and that's logged");
                int dKeys = d.KeyDowns().Count;
                Pump(6.5);
                Check(d.KeyDowns().Count == dKeys, "no automatic nudges while stopped");
                d.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);
                engine.Start();
                Check(engine.State == EngineState.Standby, "turning it back on clears the stop");

                Fake e = LaunchRoblox("e", "--start=normal --misbehave=resist --x=700 --y=160");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(other.Window);
                engine.NudgeNow();
                Pump(0.3);
                Check(engine.State == EngineState.Halted && LogHas("Focus restoration failure"),
                    "a window that can't be put back stops automatic nudging right away");
                Check(engine.NudgesFailed >= 1, "...and counts as a failed nudge");
                e.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);
                engine.Start();

                Fake f = LaunchRoblox("f", "--start=normal --misbehave=center-cursor --x=700 --y=160");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(other.Window);
                N.SetCursorPos(200, 200);
                Pump(0.2);
                N.POINT parked = Mouse();
                engine.NudgeNow();
                Pump(0.3);
                Check(SamePoint(Mouse(), parked), "if Roblox grabs the mouse (Shift Lock), the pointer goes back");
                Check(engine.Running, "...without stopping");
                f.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);

                Section("Waiting for you to pause");
                Timing.Minute = TimeSpan.FromSeconds(2);    // interval 10 s; a due nudge may wait until 16 s
                s.WaitForPause = true;
                Fake g = LaunchRoblox("g", "--start=minimized --x=700 --y=160");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                DateTime seen = DateTime.UtcNow;
                Nudger.BringToFront(other.Window);
                BusyFor(11.5);
                Check(engine.WaitingForPause && g.KeyDowns().Count == 0, "a due nudge waits while you're typing or clicking");
                Check(BusyUntil(() => g.KeyDowns().Count >= 1, 7), "...but goes ahead before Roblox's limit");
                double waited = (DateTime.UtcNow - seen).TotalSeconds;
                Check(waited > 14.5 && waited < 18, "...at the scaled 18-minute mark (" + waited.ToString("0.0") + " s)");
                Check(LogHas("couldn't wait any longer"), "...and logs why");
                BusyFor(12);    // due again after 10 s, but you're still busy
                int nudges = g.KeyDowns().Count;
                Check(nudges == 1, "no nudge while you're still busy");
                DateTime quiet = DateTime.UtcNow;
                Check(PumpUntil(() => g.KeyDowns().Count > nudges, 6), "nudges once you pause");
                double pause = (DateTime.UtcNow - quiet).TotalSeconds;
                Check(pause > 2.5 && pause < 3.8, "...after a 3-second pause (" + pause.ToString("0.0") + " s)");
                g.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);
                Timing.Minute = TimeSpan.FromSeconds(1);
            }
            other.Close();
        }

        // ---------- what you see on screen during a nudge ----------

        private const string Green = "--color=00A050";

        /// <summary>
        /// Films fixed spots on the screen while nudging (magenta = the stand-in Roblox, green = your window)
        /// and counts the frames where any spot changed. Zero means no flicker.
        /// </summary>
        private static void TestNoFlicker()
        {
            Section("No flicker");
            var s = new Settings();
            s.IntervalMinutes = 15;    // only Nudge now in this part
            s.WaitForPause = false;
            s.KeepPcAwake = false;
            s.HideRoblox = true;
            s.Action = NudgeAction.CameraNudge;
            using (var engine = new AntiAfkEngine(s))
            {
                engine.Start();

                // Roblox in full view, your window beside it (like the Anti-AFK window next to Roblox)
                Fake you = Launch("OtherApp.exe", "you", "--class=OtherWindow --x=40 --y=40 " + Green);
                Fake r = LaunchRoblox("visible", "--x=700 --y=160");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(you.Window);
                Pump(0.4);
                int changed = FramesChanged(engine, r, ClientSpot(r.Window), ClientSpot(you.Window));
                Check(changed == 0, "Roblox in full view doesn't disappear during a nudge (" + changed + " changed frames)");
                Check(r.KeyDowns().Count == 2, "...and still gets the keys");
                r.Close();
                you.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);

                // Roblox partly behind your window
                you = Launch("OtherApp.exe", "you", "--class=OtherWindow --x=500 --y=250 " + Green);
                r = LaunchRoblox("partly", "--x=700 --y=160");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(you.Window);
                Pump(0.4);
                N.RECT rr = RectOf(r.Window), yr = RectOf(you.Window);
                var robloxOnly = new Point(rr.Right - 40, rr.Top + 60);                   // part of Roblox you can see
                var overlap = new Point((rr.Left + yr.Right) / 2, (yr.Top + rr.Bottom) / 2); // your window over Roblox
                changed = FramesChanged(engine, r, robloxOnly, overlap);
                Check(changed <= 1, "Roblox partly behind your window doesn't disappear, and shows over your window "
                    + "for at most one frame (" + changed + " changed frames)");
                Check(r.KeyDowns().Count == 2, "...and still gets the keys");
                Check(N.GetForegroundWindow() == you.Window, "...and your window keeps the focus");
                r.Close();
                you.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);

                // Roblox completely covered by your window
                r = LaunchRoblox("covered", "--x=700 --y=160");
                you = Launch("OtherApp.exe", "you", "--class=OtherWindow --x=660 --y=120 --w=600 --h=420 " + Green);
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(you.Window);
                Pump(0.4);
                changed = FramesChanged(engine, r, ClientSpot(r.Window), new Point(RectOf(r.Window).Left + 40, RectOf(r.Window).Top + 60));
                Check(changed == 0, "Roblox hidden behind your window doesn't flash in front (" + changed + " changed frames)");
                Check(r.KeyDowns().Count == 2, "...and still gets the keys");
                r.Close();
                you.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);

                // minimized Roblox, with your window where Roblox would open
                you = Launch("OtherApp.exe", "you", "--class=OtherWindow --x=690 --y=150 " + Green);
                r = LaunchRoblox("minimized", "--start=minimized --x=700 --y=160");
                PumpUntil(() => engine.RobloxCount == 1, 8);
                Nudger.BringToFront(you.Window);
                Pump(0.4);
                changed = FramesChanged(engine, r, ClientSpot(you.Window), new Point(RectOf(you.Window).Left + 60, RectOf(you.Window).Bottom - 40));
                Check(changed == 0, "minimized Roblox doesn't flash on screen (" + changed + " changed frames)");
                Check(r.KeyDowns().Count == 2 && N.IsIconic(r.Window), "...gets the keys and is minimized again");
                r.Close();
                you.Close();
                PumpUntil(() => engine.RobloxCount == 0, 8);
            }
        }

        private static Point ClientSpot(IntPtr h)
        {
            N.RECT r = RectOf(h);
            return new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2 + 20);
        }

        private static volatile bool filming;

        /// <summary>Nudge now while filming the given screen spots; returns how many frames differed.</summary>
        private static int FramesChanged(AntiAfkEngine engine, Fake roblox, params Point[] spots)
        {
            int changed = 0, frames = 0;
            Color[] before = ReadSpots(spots);
            var notes = new List<string>();
            DateTime start = DateTime.UtcNow;
            filming = true;
            var camera = new Thread(delegate ()
            {
                while (filming)
                {
                    Color[] now = ReadSpots(spots);
                    frames++;
                    for (int i = 0; i < spots.Length; i++)
                    {
                        if (Math.Abs(now[i].R - before[i].R) + Math.Abs(now[i].G - before[i].G) + Math.Abs(now[i].B - before[i].B) > 60)
                        {
                            changed++;
                            notes.Add("spot " + i + " at " + (DateTime.UtcNow - start).TotalMilliseconds.ToString("0") + " ms: "
                                + before[i].Name + " -> " + now[i].Name);
                            break;
                        }
                    }
                }
            });
            camera.Start();
            Pump(0.2);
            double nudgeAt = (DateTime.UtcNow - start).TotalMilliseconds;
            engine.NudgeNow();
            double nudgeEnd = (DateTime.UtcNow - start).TotalMilliseconds;
            Pump(0.5);
            filming = false;
            camera.Join();
            Console.WriteLine("        (" + frames + " frames filmed, keys: " + roblox.KeyDowns().Count + ", nudge "
                + nudgeAt.ToString("0") + "-" + nudgeEnd.ToString("0") + " ms)");
            foreach (string note in notes) Console.WriteLine("        " + note);
            return changed;
        }

        private static Color[] ReadSpots(Point[] spots)
        {
            var colors = new Color[spots.Length];
            using (var pixel = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(pixel))
            {
                for (int i = 0; i < spots.Length; i++)
                {
                    g.CopyFromScreen(spots[i], Point.Empty, new Size(1, 1));
                    colors[i] = pixel.GetPixel(0, 0);
                }
            }
            return colors;
        }

        // ---------- the window and tray menu ----------

        private static void TestWindow()
        {
            Section("Window and tray menu");
            new Settings().Save();   // defaults, in the test folder
            using (var form = new MainForm(true, null, null))
            {
                ContextMenuStrip menu = form.TrayMenu;
                var names = new List<string>();
                foreach (ToolStripItem item in menu.Items) names.Add(item is ToolStripSeparator ? "-" : item.Text);
                string shown = string.Join(" | ", names);
                Check(shown == "Anti-AFK On | Nudge Now | Activity Mode | - | Open NudgeNest | - | Exit", "tray menu: " + shown);
                var on = (ToolStripMenuItem)menu.Items[0];
                Pump(0.5);
                Check(on.Checked && form.Engine.Running, "Anti-AFK On is ticked while it's on");
                on.PerformClick();
                Pump(0.3);
                Check(!form.Engine.Running && !on.Checked, "clicking it turns Anti-AFK off");
                on.PerformClick();
                Pump(0.3);
                Check(form.Engine.Running && on.Checked, "...and on again");
                var modes = (ToolStripMenuItem)menu.Items[2];
                Check(modes.DropDownItems.Count == 3, "Activity Mode lists Jump, Camera nudge and Custom key");
                modes.DropDownItems[1].PerformClick();
                Pump(0.2);
                Check(Settings.Load().Action == NudgeAction.CameraNudge && ((ToolStripMenuItem)modes.DropDownItems[1]).Checked,
                    "choosing Camera nudge in the tray is saved for next time");

                // screenshots for a visual check
                string shots = Path.Combine(bin, "screens");
                Directory.CreateDirectory(shots);
                form.ShowFromTray();
                form.TopMost = true;
                Pump(1.2);
                Snap(form.Bounds, Path.Combine(shots, "main.png"));
                form.ShowPage(Page.Settings);
                Pump(0.8);
                Check(form.CurrentPage == Page.Settings, "the settings icon's page opens in the same window");
                Snap(form.Bounds, Path.Combine(shots, "settings.png"));
                form.ShowPage(Page.About);
                Pump(0.8);
                Snap(form.Bounds, Path.Combine(shots, "about.png"));
                Check(PageHasText(form, "Made by xRed1"), "the About page says Made by xRed1");
                form.ShowPage(Page.Main);
                Pump(0.6);
                TestMotion(form, shots);
                menu.Show(new Point(form.Right + 12, form.Top + 40));
                modes.ShowDropDown();
                Pump(0.8);
                Snap(Rectangle.Union(menu.Bounds, modes.DropDown.Bounds), Path.Combine(shots, "tray-menu.png"));
                menu.Close();
                Check(true, "screenshots saved to tests\\bin\\screens");
                form.ExitApp();
                Pump(0.3);
            }
        }

        // ---------- animations ----------

        /// <summary>Checks that things move through in-between frames instead of cutting, and stop afterwards.</summary>
        private static void TestMotion(MainForm form, string shots)
        {
            Section("Smooth animations");
            Check(Motion.Enabled, "animations are on (Windows animation effects are enabled)");
            using (var f = new Form())
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(form.Right + 20, form.Top);
                f.ClientSize = new Size(420, 130);
                f.BackColor = Theme.Window;
                f.TopMost = true;
                var sw = new ToggleSwitch();
                sw.ShowText = true;
                sw.SetBounds(20, 20, 92, 40);
                var picker = new Segmented();
                picker.Items = new[] { "1 min", "2 min", "5 min", "10 min", "15 min" };
                picker.SetBounds(20, 76, 380, 40);
                f.Controls.Add(sw);
                f.Controls.Add(picker);
                f.Show();
                picker.SelectedIndex = 0;
                Pump(0.3);

                sw.On = true;
                List<double> knob = Sample(() => sw.KnobPosition, 0.45);
                int between = knob.FindAll(k => k > 0.03 && k < 0.97).Count;
                Check(between >= 5 && knob[knob.Count - 1] == 1,
                    "the ON/OFF knob slides across (" + between + " in-between frames, not a cut)");

                picker.SelectedIndex = 4;
                List<double> pill = Sample(() => picker.HighlightPosition, 0.45);
                between = pill.FindAll(x => x > 0.1 && x < 3.9).Count;
                Check(between >= 5 && pill[pill.Count - 1] == 4,
                    "the picker's highlight glides to the new choice (" + between + " in-between frames)");
            }

            PageSlide slide = null;
            foreach (Control c in form.Controls) if (c is PageSlide) slide = (PageSlide)c;
            var frames = new List<Bitmap>();
            var progress = new List<double>();
            Rectangle area = form.Bounds;
            filming = true;
            var camera = new Thread(delegate ()
            {
                while (filming)
                {
                    var frame = new Bitmap(area.Width, area.Height);
                    using (Graphics g = Graphics.FromImage(frame)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    lock (frames) frames.Add(frame);
                    Thread.Sleep(25);
                }
            });
            camera.Start();
            form.ShowPage(Page.Settings);
            progress = Sample(() => slide.Visible ? slide.Progress : -1, 0.6);
            filming = false;
            camera.Join();
            int slideFrames = progress.FindAll(x => x > 0.1 && x < 0.9).Count;
            Check(slideFrames >= 5 && !slide.Visible && form.CurrentPage == Page.Settings,
                "pages slide and fade into each other (" + slideFrames + " in-between frames)");

            // frame rate without the camera running: the slide lasts 320 ms
            form.ShowPage(Page.Main);
            Pump(0.6);
            slide.Frames = 0;
            form.ShowPage(Page.About);
            Pump(0.6);
            double fps = slide.Frames / 0.32;
            Check(slide.Frames >= 12, "the page slide draws smoothly (" + slide.Frames + " frames, about " + fps.ToString("0") + " fps)");
            lock (frames)
            {
                if (frames.Count > 0)
                {
                    // a filmstrip of 6 frames from the transition, for a visual check
                    int n = Math.Min(6, frames.Count), w = frames[0].Width / 2, h = frames[0].Height / 2;
                    using (var strip = new Bitmap(w * n, h))
                    {
                        using (Graphics g = Graphics.FromImage(strip))
                            for (int i = 0; i < n; i++)
                                g.DrawImage(frames[i * (frames.Count - 1) / Math.Max(1, n - 1)], i * w, 0, w, h);
                        strip.Save(Path.Combine(shots, "page-slide.png"), ImageFormat.Png);
                    }
                }
                foreach (Bitmap b in frames) b.Dispose();
            }

            form.ShowPage(Page.Main);
            Pump(1.2);
            Check(!Motion.Busy, "nothing keeps animating once everything is still (no timer left running)");
        }

        private static List<double> Sample(Func<double> value, double seconds)
        {
            var values = new List<double>();
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                values.Add(value());
                Thread.Sleep(8);
            }
            values.Add(value());
            return values;
        }

        // ---------- helpers ----------

        private static bool PageHasText(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                if (c.Visible && c.Text == text) return true;
                if (c.Visible && PageHasText(c, text)) return true;
            }
            return false;
        }

        private static Fake Launch(string exe, string name, string args)
        {
            var f = new Fake();
            f.LogPath = Path.Combine(bin, name + ".log");
            if (File.Exists(f.LogPath)) File.Delete(f.LogPath);
            var start = new ProcessStartInfo(Path.Combine(bin, exe), "\"--log=" + f.LogPath + "\" --life=300 " + args);
            start.UseShellExecute = false;
            f.Process = Process.Start(start);
            fakes.Add(f);
            PumpUntil(() => f.Events().Exists(e => e.Length >= 3 && e[1] == "ready"), 5);
            foreach (string[] e in f.Events())
                if (e.Length >= 3 && e[1] == "ready") f.Window = new IntPtr(long.Parse(e[2], NumberStyles.HexNumber));
            return f;
        }

        private static Fake LaunchRoblox(string name, string args)
        {
            Fake f = Launch("RobloxPlayerBeta.exe", name, args);
            RobloxWindows.OnlyPids.Add((uint)f.Process.Id);
            return f;
        }

        /// <summary>Nudge now and return the keys the stand-in received from it.</summary>
        private static int[] NewKeys(Fake f, AntiAfkEngine engine)
        {
            int before = f.KeyDowns().Count;
            engine.NudgeNow();
            Pump(0.3);
            List<int> all = f.KeyDowns();
            return all.GetRange(before, all.Count - before).ToArray();
        }

        private static void Check(bool ok, string what)
        {
            if (ok) passed++;
            else failed++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine(name);
        }

        private static void Pump(double seconds)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static bool PumpUntil(Func<bool> done, double seconds)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                if (done()) return true;
                Application.DoEvents();
                Thread.Sleep(10);
            }
            return done();
        }

        /// <summary>Simulates you using the PC: a 1-pixel mouse wiggle every 0.4 s.</summary>
        private static void BusyFor(double seconds)
        {
            BusyUntil(() => false, seconds);
        }

        /// <summary>Simulates you playing Roblox: the pointer stays over it while you use the mouse.</summary>
        private static void PlayFor(double seconds, int x, int y)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                N.SetCursorPos(x, y);
                Wiggle();
                Pump(0.4);
            }
        }

        private static bool BusyUntil(Func<bool> done, double seconds)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                Wiggle();
                if (PumpUntil(done, 0.4)) return true;
            }
            return done();
        }

        private static void Wiggle()
        {
            var inputs = new N.INPUT[2];
            inputs[0].type = 0;   // mouse
            inputs[0].u.mi.dx = 1;
            inputs[0].u.mi.dwFlags = 0x1;   // MOUSEEVENTF_MOVE
            inputs[1].type = 0;
            inputs[1].u.mi.dx = -1;
            inputs[1].u.mi.dwFlags = 0x1;
            N.SendInput(2, inputs, Marshal.SizeOf(typeof(N.INPUT)));
        }

        private static bool LogHas(string text)
        {
            return File.Exists(Logger.FilePath) && File.ReadAllText(Logger.FilePath).Contains(text);
        }

        private static N.RECT RectOf(IntPtr h)
        {
            N.RECT r;
            N.GetWindowRect(h, out r);
            return r;
        }

        private static N.WINDOWPLACEMENT PlacementOf(IntPtr h)
        {
            var p = new N.WINDOWPLACEMENT();
            p.length = Marshal.SizeOf(typeof(N.WINDOWPLACEMENT));
            N.GetWindowPlacement(h, ref p);
            return p;
        }

        private static N.POINT Mouse()
        {
            N.POINT p;
            N.GetCursorPos(out p);
            return p;
        }

        private static bool SameRect(N.RECT a, N.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        private static bool SamePoint(N.POINT a, N.POINT b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        private static bool Same(int[] got, int[] want)
        {
            if (got.Length != want.Length) return false;
            for (int i = 0; i < got.Length; i++) if (got[i] != want[i]) return false;
            return true;
        }

        private static void Snap(Rectangle area, string path)
        {
            using (var bmp = new Bitmap(area.Width, area.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
