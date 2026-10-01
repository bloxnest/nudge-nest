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
using System.Windows.Threading;
using N = NudgeNest.NativeMethods;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;

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
                if (only.Contains("shots")) WebsiteShots();   // only when asked for by name
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
            var w = new MainWindow(null, null);
            w.ShutDown = delegate { };   // Exit would end the test run otherwise
            try
            {
                ContextMenuStrip menu = w.TrayMenu;
                var names = new List<string>();
                foreach (ToolStripItem item in menu.Items) names.Add(item is ToolStripSeparator ? "-" : item.Text);
                string shown = string.Join(" | ", names);
                Check(shown == "Anti-AFK On | Nudge Now | Activity Mode | - | Open NudgeNest | - | Exit", "tray menu: " + shown);
                var on = (ToolStripMenuItem)menu.Items[0];
                Pump(0.5);
                Check(on.Checked && w.Engine.Running, "Anti-AFK On is ticked while it's on");
                on.PerformClick();
                Pump(0.3);
                Check(!w.Engine.Running && !on.Checked, "clicking it turns Anti-AFK off");
                on.PerformClick();
                Pump(0.3);
                Check(w.Engine.Running && on.Checked, "...and on again");
                var modes = (ToolStripMenuItem)menu.Items[2];
                Check(modes.DropDownItems.Count == 3, "Activity Mode lists Jump, Camera nudge and Custom key");
                modes.DropDownItems[1].PerformClick();
                Pump(0.2);
                Check(Settings.Load().Action == NudgeAction.CameraNudge && ((ToolStripMenuItem)modes.DropDownItems[1]).Checked,
                    "choosing Camera nudge in the tray is saved for next time");

                var offSegment = Find<WpfControls.RadioButton>(w, "OffSegment");
                var onSegment = Find<WpfControls.RadioButton>(w, "OnSegment");
                offSegment.IsChecked = true;
                Pump(0.2);
                Check(!w.Engine.Running && !on.Checked, "the window's Off turns Anti-AFK off, and the tray menu follows");
                onSegment.IsChecked = true;
                Pump(0.2);
                Check(w.Engine.Running && on.Checked, "...and On turns it back on");

                // screenshots for a visual check
                string shots = Path.Combine(bin, "screens");
                Directory.CreateDirectory(shots);
                w.ShowFromTray();
                w.Window.Topmost = true;
                Pump(1.0);
                Color fill = SelectedFill(onSegment);
                Check(fill == Color.FromArgb(0x86, 0xE3, 0xCE), "the On switch lights up mint (" + fill.Name + ")");
                Check(ContentFits(w), "the main page fits in the window (" + Overflow(w) + ")");
                Snap(Bounds(w), Path.Combine(shots, "main.png"));
                var heights = new List<int> { Bounds(w).Height };
                RenderClean(w, Path.Combine(shots, "clean-main.png"));

                // Custom key with no key yet: Options opens and asks for one
                Find<WpfControls.RadioButton>(w, "CustomSegment").IsChecked = true;
                Pump(0.6);
                Check(w.CurrentPage == Page.Settings && w.KeyMessage.Contains("Esc cancels"),
                    "choosing Custom key before picking one opens Options and asks for a key");
                Check(!w.OfferKey(Keys.CapsLock) && w.KeyMessage.Contains("whole PC"), "a refused key is explained");
                Check(w.OfferKey(Keys.K) && Settings.Load().CustomKey == Keys.K && Settings.Load().Action == NudgeAction.CustomKey,
                    "K is saved and becomes the activity");
                Check(Find<WpfControls.TextBlock>(w, "KeyValueText").Text == "K", "Options shows the custom key: K");
                Check(((ToolStripMenuItem)modes.DropDownItems[2]).Checked && modes.DropDownItems[2].Text == "Custom key (K)",
                    "the tray menu shows Custom key (K), ticked");
                Pump(0.5);
                Check(ContentFits(w), "the Options page fits in the window (" + Overflow(w) + ")");
                Snap(Bounds(w), Path.Combine(shots, "settings.png"));
                heights.Add(Bounds(w).Height);
                RenderClean(w, Path.Combine(shots, "clean-settings.png"));

                w.ShowPanel(Page.About);
                Pump(0.6);
                Check(w.CurrentPage == Page.About, "the About page opens in the same window");
                Check(ContentFits(w), "the About page fits in the window (" + Overflow(w) + ")");
                Check(HasText(Find<Wpf.FrameworkElement>(w, "AboutPanel"), "Made by xRed1"), "the About page says Made by xRed1");
                Check(Find<WpfControls.TextBlock>(w, "VersionText").Text == "Version " + AppInfo.Version,
                    "the About page shows the version (" + AppInfo.Version + ")");
                Snap(Bounds(w), Path.Combine(shots, "about.png"));
                heights.Add(Bounds(w).Height);
                RenderClean(w, Path.Combine(shots, "clean-about.png"));

                w.ShowPanel(Page.Log);
                Pump(0.6);
                Check(Find<WpfControls.TextBlock>(w, "LogText").Text.Contains("Custom key set to K"), "the activity log page shows the log");
                Logger.Write("test line while the log is open");
                Pump(0.1);
                Check(Find<WpfControls.TextBlock>(w, "LogText").Text.Contains("test line while the log is open"),
                    "new log lines appear while it's open");
                Check(ContentFits(w), "the log page fits in the window (" + Overflow(w) + ")");
                Snap(Bounds(w), Path.Combine(shots, "log.png"));
                heights.Add(Bounds(w).Height);

                Check(heights.TrueForAll(h => h == heights[0]),
                    "the window keeps one height on every page, so switching pages doesn't jump (" + string.Join(", ", heights) + " px)");
                w.ShowPanel(Page.Main);
                Pump(0.6);
                TestMotion(w, shots);

                Rectangle at = Bounds(w);
                menu.Show(new Point(at.Right + 12, at.Top + 40));
                modes.ShowDropDown();
                Pump(0.8);
                Snap(Rectangle.Union(menu.Bounds, modes.DropDown.Bounds), Path.Combine(shots, "tray-menu.png"));
                menu.Close();
                TestTrayIcons(shots);
                Check(true, "screenshots saved to tests\\bin\\screens");

                menu.Items[menu.Items.Count - 1].PerformClick();   // Exit
                Pump(0.5);
                Check(!w.Window.IsVisible && LogHas("NudgeNest closed"), "Exit closes the window and the app");
            }
            finally
            {
                w.Quit();
                Pump(0.3);
            }
        }

        /// <summary>
        /// Website screenshots (run-tests.bat shots): the window at real speed, with a stand-in Roblox open,
        /// drawn at 2x into tests\bin\website. tools\make_screenshots.py crops them for docs\assets.
        /// </summary>
        private static void WebsiteShots()
        {
            Section("Website screenshots");
            Timing.Minute = TimeSpan.FromMinutes(1);
            Timing.TickMs = 1000;
            new Settings { IntervalMinutes = 15, Action = NudgeAction.CameraNudge, CustomKey = Keys.D2 }.Save();
            string folder = Path.Combine(bin, "website");
            Directory.CreateDirectory(folder);
            var w = new MainWindow(null, null);
            w.ShutDown = delegate { };
            try
            {
                w.ShowFromTray();
                Pump(1.5);
                LaunchRoblox("website-roblox", "--start=minimized");   // opened after the app: a full countdown
                Pump(12.5);   // a few seconds into the session
                RenderClean(w, Path.Combine(folder, "main.png"));
                w.ShowPanel(Page.Settings);
                Pump(0.8);
                RenderClean(w, Path.Combine(folder, "options.png"));
                w.ShowPanel(Page.About);
                Pump(0.8);
                RenderClean(w, Path.Combine(folder, "about.png"));
                Check(w.Engine.RobloxCount == 1 && w.Engine.NudgesFailed == 0, "screenshots saved to tests\\bin\\website");
            }
            finally
            {
                w.Quit();
                Pump(0.3);
            }
        }

        private static void TestTrayIcons(string shots)
        {
            var states = new[] { TrayIcons.On, TrayIcons.Standby, TrayIcons.Off, TrayIcons.Stopped };
            int[] sizes = { 16, 20, 24, 32 };
            bool readable = true;
            using (var strip = new Bitmap(sizes.Length * 40, states.Length * 40))
            {
                using (Graphics g = Graphics.FromImage(strip))
                {
                    g.Clear(Color.FromArgb(0x20, 0x20, 0x20));
                    for (int s = 0; s < states.Length; s++)
                        for (int i = 0; i < sizes.Length; i++)
                            using (Icon icon = TrayIcons.Make(states[s], sizes[i]))
                            using (Bitmap b = icon.ToBitmap())
                            {
                                g.DrawImageUnscaled(b, i * 40 + 4, s * 40 + 4);
                                int ink = 0;
                                for (int y = 0; y < b.Height; y++)
                                    for (int x = 0; x < b.Width; x++)
                                        if (b.GetPixel(x, y).A > 200 && b.GetPixel(x, y).R < 0x30) ink++;
                                if (ink < 20) readable = false;   // the three letters are 25+ pixels
                            }
                }
                strip.Save(Path.Combine(shots, "tray-icons.png"), ImageFormat.Png);
            }
            Check(readable, "the tray icon spells AFK at 16, 20, 24 and 32 px, in all four state colours");
        }

        // ---------- animations ----------

        /// <summary>
        /// The window animates like BloxNest: short WPF storyboards that move through in-between frames
        /// (no cuts) and then stop, so an idle window uses no CPU.
        /// </summary>
        private static void TestMotion(MainWindow w, string shots)
        {
            Section("Smooth animations (WPF, like BloxNest)");
            var root = Find<Wpf.FrameworkElement>(w, "WindowRoot");
            var scale = (WpfMedia.ScaleTransform)root.RenderTransform;

            // closing fades the window out, then it hides in the tray
            Find<WpfControls.Button>(w, "CloseButton").RaiseEvent(new Wpf.RoutedEventArgs(WpfControls.Primitives.ButtonBase.ClickEvent));
            List<double> fade = Sample(() => root.Opacity, 0.4);
            int between = fade.FindAll(x => x > 0.05 && x < 0.95).Count;
            Check(between >= 3 && !w.Window.IsVisible, "closing fades the window out, then hides it (" + between + " in-between frames)");

            // opening from the tray scales up and fades in
            w.ShowFromTray();
            List<double> grow = Sample(() => scale.ScaleX, 0.45);
            between = grow.FindAll(x => x > 0.962 && x < 0.998).Count;
            Check(between >= 4 && grow[grow.Count - 1] == 1, "opening from the tray scales the window in (" + between + " in-between frames)");
            w.Window.Topmost = true;
            Pump(0.3);

            // switches slide their knob across
            w.ShowPanel(Page.Settings);
            Pump(0.5);
            var box = Find<WpfControls.CheckBox>(w, "OptAwake");
            var knob = (Wpf.FrameworkElement)box.Template.FindName("Knob", box);
            var slide = (WpfMedia.TranslateTransform)((WpfMedia.TransformGroup)knob.RenderTransform).Children[1];
            bool was = box.IsChecked == true;
            box.IsChecked = !was;
            List<double> knobX = Sample(() => slide.X, 0.4);
            between = knobX.FindAll(x => x > 0.5 && x < 17.5).Count;
            Check(between >= 4 && knobX[knobX.Count - 1] == (was ? 0 : 18), "a switch's knob slides across (" + between + " in-between frames, not a cut)");
            box.IsChecked = was;
            Pump(0.4);

            // segmented choices fade their highlight in
            var choices = Find<WpfControls.Primitives.UniformGrid>(w, "IntervalSegments");
            var five = (WpfControls.RadioButton)choices.Children[2];
            var fillBorder = (Wpf.FrameworkElement)five.Template.FindName("SelectedFill", five);
            var keep = (WpfControls.RadioButton)choices.Children[System.Array.IndexOf(Settings.IntervalChoices, Settings.Load().IntervalMinutes)];
            five.IsChecked = true;
            List<double> glow = Sample(() => fillBorder.Opacity, 0.35);
            between = glow.FindAll(x => x > 0.05 && x < 0.95).Count;
            Check(between >= 3, "a picked choice fades in (" + between + " in-between frames)");
            keep.IsChecked = true;
            Pump(0.3);

            // pages slide and fade into each other; film it for a visual check
            var frames = new List<Bitmap>();
            Rectangle area = Bounds(w);
            filming = true;
            var camera = new Thread(delegate ()
            {
                while (filming)
                {
                    var frame = new Bitmap(area.Width, area.Height);
                    using (Graphics g = Graphics.FromImage(frame)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    lock (frames) frames.Add(frame);
                    Thread.Sleep(20);
                }
            });
            camera.Start();
            var about = Find<Wpf.FrameworkElement>(w, "AboutPanel");
            w.ShowPanel(Page.About);
            List<double> pageX = Sample(() => about.RenderTransform is WpfMedia.TranslateTransform ? ((WpfMedia.TranslateTransform)about.RenderTransform).X : -1, 0.4);
            filming = false;
            camera.Join();
            between = pageX.FindAll(x => x > 0.3 && x < 13.7).Count;
            Check(between >= 5 && pageX[pageX.Count - 1] == 0, "pages slide and fade into each other (" + between + " in-between frames)");
            lock (frames)
            {
                if (frames.Count > 0)
                {
                    // a filmstrip of 6 frames from the transition
                    int n = Math.Min(6, frames.Count), fw = frames[0].Width / 2, fh = frames[0].Height / 2;
                    using (var strip = new Bitmap(fw * n, fh))
                    {
                        using (Graphics g = Graphics.FromImage(strip))
                            for (int i = 0; i < n; i++)
                                g.DrawImage(frames[i * (frames.Count - 1) / Math.Max(1, n - 1)], i * fw, 0, fw, fh);
                        strip.Save(Path.Combine(shots, "page-slide.png"), ImageFormat.Png);
                    }
                }
                foreach (Bitmap b in frames) b.Dispose();
            }

            // once everything is still, the window costs (next to) nothing
            w.ShowPanel(Page.Main);
            Pump(1.0);
            TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            DateTime start = DateTime.UtcNow;
            Pump(3);
            double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalMilliseconds
                / (DateTime.UtcNow - start).TotalMilliseconds * 100;
            Check(cpu < 2, "the open window idles at " + cpu.ToString("0.0") + "% of one CPU core (the test run updates it 5x a second)");
        }

        private static List<double> Sample(Func<double> value, double seconds)
        {
            var values = new List<double>();
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                DoEvents();
                values.Add(value());
                Thread.Sleep(5);
            }
            values.Add(value());
            return values;
        }

        // ---------- WPF helpers ----------

        private static T Find<T>(MainWindow w, string name) where T : class
        {
            return (T)w.Window.FindName(name);
        }

        private static Rectangle Bounds(MainWindow w)
        {
            N.RECT r = RectOf(new Wpf.Interop.WindowInteropHelper(w.Window).Handle);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        private static Color SelectedFill(WpfControls.RadioButton segment)
        {
            var border = (WpfControls.Border)segment.Template.FindName("SelectedFill", segment);
            var brush = border.Background as WpfMedia.SolidColorBrush;
            return brush == null ? Color.Empty : Color.FromArgb(brush.Color.R, brush.Color.G, brush.Color.B);
        }

        /// <summary>The window drawn on its own (no desktop behind it), at 2x for sharp website screenshots.</summary>
        private static void RenderClean(MainWindow w, string path)
        {
            int width = (int)Math.Ceiling(w.Window.ActualWidth * 2), height = (int)Math.Ceiling(w.Window.ActualHeight * 2);
            var picture = new WpfMedia.Imaging.RenderTargetBitmap(width, height, 192, 192, WpfMedia.PixelFormats.Pbgra32);
            picture.Render(w.Window);
            var png = new WpfMedia.Imaging.PngBitmapEncoder();
            png.Frames.Add(WpfMedia.Imaging.BitmapFrame.Create(picture));
            using (var file = File.Create(path)) png.Save(file);
        }

        /// <summary>Is everything on the open page inside the page's margins, with no text cut off?</summary>
        private static bool ContentFits(MainWindow w)
        {
            return Overflow(w) == "fits";
        }

        private static string Overflow(MainWindow w)
        {
            var root = Find<Wpf.FrameworkElement>(w, "ContentRoot");
            string name = w.CurrentPage == Page.Settings ? "SettingsPanel" : w.CurrentPage == Page.About ? "AboutPanel"
                : w.CurrentPage == Page.Log ? "LogPanel" : "MainPanel";
            var panel = Find<Wpf.FrameworkElement>(w, name);
            double right = root.ActualWidth - 24 + 0.5;   // the pages' side margin
            string problem = null;
            Walk(panel, e =>
            {
                if (problem != null || !e.IsVisible || e.ActualWidth == 0) return;
                Wpf.Rect r = e.TransformToAncestor(root).TransformBounds(new Wpf.Rect(e.RenderSize));
                string what = e is WpfControls.TextBlock ? "'" + ((WpfControls.TextBlock)e).Text + "'" : e.Name.Length > 0 ? e.Name : e.GetType().Name;
                if (r.Right > right)
                    problem = what + " ends at x=" + r.Right.ToString("0") + " but the page ends at " + right.ToString("0");
                var text = e as WpfControls.TextBlock;
                if (text != null && text.TextWrapping == Wpf.TextWrapping.NoWrap && TextWidth(text) > text.ActualWidth + 2)
                    problem = what + " is cut off (" + TextWidth(text).ToString("0") + " px of text in " + text.ActualWidth.ToString("0") + " px)";
            });
            if (problem == null && w.Window.ActualHeight > Wpf.SystemParameters.WorkArea.Height)
                problem = "the window is taller than the screen";
            return problem ?? "fits";
        }

        private static void Walk(Wpf.DependencyObject e, Action<Wpf.FrameworkElement> visit)
        {
            var element = e as Wpf.FrameworkElement;
            if (element != null) visit(element);
            for (int i = 0; i < WpfMedia.VisualTreeHelper.GetChildrenCount(e); i++)
                Walk(WpfMedia.VisualTreeHelper.GetChild(e, i), visit);
        }

        private static double TextWidth(WpfControls.TextBlock t)
        {
            var text = new WpfMedia.FormattedText(t.Text, CultureInfo.CurrentUICulture, t.FlowDirection,
                new WpfMedia.Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, WpfMedia.Brushes.Black,
                null, WpfMedia.TextOptions.GetTextFormattingMode(t), 1.0);   // the window draws text in Display mode
            return text.WidthIncludingTrailingWhitespace;
        }

        private static bool HasText(Wpf.DependencyObject e, string text)
        {
            var block = e as WpfControls.TextBlock;
            if (block != null && block.Text == text) return true;
            foreach (object child in Wpf.LogicalTreeHelper.GetChildren(e))
                if (child is Wpf.DependencyObject && HasText((Wpf.DependencyObject)child, text)) return true;
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
                DoEvents();
                Thread.Sleep(10);
            }
        }

        private static bool PumpUntil(Func<bool> done, double seconds)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                if (done()) return true;
                DoEvents();
                Thread.Sleep(10);
            }
            return done();
        }

        /// <summary>Runs whatever is waiting: Windows messages (timers, the tray) and the WPF window's work.</summary>
        private static void DoEvents()
        {
            Application.DoEvents();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(delegate { frame.Continue = false; return null; }), null);
            Dispatcher.PushFrame(frame);
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
