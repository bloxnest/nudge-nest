using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace NudgeNest
{
    internal enum EngineState { Off, Standby, Active, Halted }

    /// <summary>The engine's clock. Real use counts in minutes; the self-tests run the same rules in seconds.</summary>
    internal static class Timing
    {
        public static TimeSpan Minute = TimeSpan.FromMinutes(1);
        public static int TickMs = 1000;

        public static TimeSpan Minutes(double n)
        {
            return TimeSpan.FromTicks((long)(Minute.Ticks * n));
        }
    }

    /// <summary>
    /// Once a second: notice Roblox opening and closing, notice when you use Roblox yourself, and nudge a
    /// Roblox window once it has gone a full interval without input. Roblox disconnects after 20 idle minutes,
    /// so a nudge that is waiting for you to stop typing is never held back past 18 minutes. After every nudge
    /// the windows are checked; if they can't be put back exactly, automatic nudging stops.
    /// </summary>
    internal sealed class AntiAfkEngine : IDisposable
    {
        private const double LatestNudgeMinutes = 18;    // Roblox disconnects at 20
        private const double PatienceMinutes = 3;        // how long a due nudge may wait for you to pause
        private const double FirstNudgeMinutes = 0.25;   // Roblox was open before the app: idle time unknown
        private const double RetryMinutes = 1 / 3.0;     // after a nudge that didn't reach Roblox
        private const uint PausedMs = 3000;              // no keyboard/mouse input for this long = you've paused
        private const uint PlayingMs = 10000;            // your Roblox input this recent = you're playing

        private sealed class Tracked
        {
            public DateTime LastInput;       // UTC: the last input Roblox got, yours or a nudge's
            public DateTime LastYourInput;   // UTC: the last input of yours that went to Roblox
            public DateTime InFrontSince;    // UTC: when it was first seen in front; MinValue = not in front
            public DateTime RetryAt;
        }

        private readonly Settings settings;
        private readonly Timer timer = new Timer();
        private readonly Dictionary<IntPtr, Tracked> windows = new Dictionary<IntPtr, Tracked>();
        private DateTime nextScan;
        private bool running, halted, firstScanDone, keepingAwake, nudging, lockLogged;
        private int repairsInARow;
        private string lastFailure;

        public event EventHandler Changed;
        public event Action<string> Halted;

        public AntiAfkEngine(Settings settings)
        {
            this.settings = settings;
            SessionStart = DateTime.MinValue;
            LastNudge = DateTime.MinValue;
            timer.Interval = Timing.TickMs;
            timer.Tick += delegate { Tick(); };
            timer.Start();
        }

        public bool Running { get { return running; } }
        public string HaltReason { get; private set; }
        public int RobloxCount { get { return windows.Count; } }
        public bool RobloxMinimized { get; private set; }
        public bool RobloxInFront { get; private set; }
        public TimeSpan RobloxIdle { get; private set; }     // the longest any Roblox window has gone without input
        public bool UserPlaying { get; private set; }
        public bool WaitingForPause { get; private set; }
        public bool PcLocked { get; private set; }

        // session statistics, reset when Roblox closes
        public DateTime SessionStart { get; private set; }   // local time; MinValue = Roblox isn't open
        public int NudgesOk { get; private set; }
        public int NudgesFailed { get; private set; }
        public DateTime LastNudge { get; private set; }      // local time; MinValue = none yet
        public bool LastNudgeWorked { get; private set; }

        public EngineState State
        {
            get
            {
                if (halted) return EngineState.Halted;
                if (!running) return EngineState.Off;
                return windows.Count == 0 ? EngineState.Standby : EngineState.Active;
            }
        }

        public TimeSpan Interval
        {
            get { return Timing.Minutes(settings.IntervalMinutes); }
        }

        /// <summary>How long after Roblox's last input a due nudge goes ahead even if you haven't paused.</summary>
        private TimeSpan Deadline
        {
            get
            {
                TimeSpan d = Interval + Timing.Minutes(PatienceMinutes);
                TimeSpan latest = Timing.Minutes(LatestNudgeMinutes);
                if (d > latest) d = latest;
                return d < Interval ? Interval : d;
            }
        }

        public TimeSpan NextNudgeIn
        {
            get
            {
                DateTime now = DateTime.UtcNow;
                TimeSpan soonest = TimeSpan.MaxValue;
                foreach (Tracked t in windows.Values)
                {
                    DateTime due = t.LastInput + Interval;
                    if (t.RetryAt > due) due = t.RetryAt;
                    if (due - now < soonest) soonest = due - now;
                }
                if (soonest == TimeSpan.MaxValue || soonest < TimeSpan.Zero) return TimeSpan.Zero;
                return soonest;
            }
        }

        public void Start()
        {
            if (running) return;
            TurnOn();
            Logger.Write("Anti-AFK started (nudges after " + Minutes(settings.IntervalMinutes) + " without Roblox input)");
            Tick();
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            Logger.Write("Anti-AFK stopped");
            Tick();
        }

        public void NudgeNow()
        {
            if (nudging) return;
            Scan(DateTime.UtcNow);
            if (windows.Count == 0) Logger.Write("Nudge now: Roblox isn't running");
            foreach (var pair in new List<KeyValuePair<IntPtr, Tracked>>(windows))
            {
                Nudge(pair.Key, pair.Value, "Nudge now");
                if (halted) break;
            }
            OnChanged();
        }

        private void TurnOn()
        {
            running = true;
            halted = false;
            HaltReason = null;
            repairsInARow = 0;
            lastFailure = null;
        }

        private void Tick()
        {
            if (nudging) return;
            DateTime now = DateTime.UtcNow;
            if (now >= nextScan) Scan(now);
            WatchRoblox(now);
            WaitingForPause = false;
            PcLocked = false;
            if (running && windows.Count > 0) NudgeWhatsDue(now);
            UpdateKeepAwake();
            OnChanged();
        }

        // ---------- Roblox opening and closing ----------

        private void Scan(DateTime now)
        {
            List<IntPtr> found = RobloxWindows.Find();
            nextScan = now.AddSeconds(found.Count > 0 ? 3 : 5);
            bool hadRoblox = windows.Count > 0;

            var gone = new List<IntPtr>();
            foreach (IntPtr h in windows.Keys)
                if (!found.Contains(h)) gone.Add(h);
            foreach (IntPtr h in gone) windows.Remove(h);

            int added = 0;
            foreach (IntPtr h in found)
            {
                if (windows.ContainsKey(h)) continue;
                var t = new Tracked();
                // Already open when the app started: its idle time is unknown, so nudge soon.
                // Opened later: its idle clock starts now.
                t.LastInput = firstScanDone ? now : now - Interval + Timing.Minutes(FirstNudgeMinutes);
                t.LastYourInput = DateTime.MinValue;
                t.InFrontSince = DateTime.MinValue;
                t.RetryAt = DateTime.MinValue;
                windows[h] = t;
                added++;
            }
            firstScanDone = true;

            if (!hadRoblox && windows.Count > 0) RobloxOpened(found[0]);
            else if (hadRoblox && windows.Count == 0) RobloxClosed();
            else if (added > 0) Logger.Write("Another Roblox window opened (" + windows.Count + " open)");
        }

        private void RobloxOpened(IntPtr hWnd)
        {
            DateTime started = RobloxWindows.StartedAt(hWnd);
            SessionStart = started == DateTime.MinValue ? DateTime.Now : started;
            ResetStats();
            Logger.Write("Roblox detected" + (windows.Count > 1 ? " (" + windows.Count + " windows)" : ""));
            if (!running && settings.AutoStartWithRoblox)
            {
                TurnOn();
                Logger.Write("Anti-AFK started automatically because Roblox started");
            }
        }

        private void RobloxClosed()
        {
            Logger.Write("Roblox closed" + (running ? "; Anti-AFK is on standby until it starts again" : ""));
            SessionStart = DateTime.MinValue;
            ResetStats();
        }

        private void ResetStats()
        {
            NudgesOk = 0;
            NudgesFailed = 0;
            LastNudge = DateTime.MinValue;
            LastNudgeWorked = false;
            lastFailure = null;
            repairsInARow = 0;
        }

        // ---------- smart AFK detection ----------

        /// <summary>
        /// Your input only counts as Roblox activity while Roblox is the window in front, the input came after
        /// it got there, and the mouse is over it. Typing in Chrome, Discord or VS Code never counts.
        /// </summary>
        private void WatchRoblox(DateTime now)
        {
            IntPtr front = NativeMethods.GetForegroundWindow();
            uint lastTick = NativeMethods.LastInputTick();
            DateTime lastInput = now - TimeSpan.FromMilliseconds(NativeMethods.IdleMilliseconds());
            bool yours = !FromLastNudge(lastTick);

            UserPlaying = false;
            RobloxInFront = false;
            RobloxMinimized = false;
            RobloxIdle = TimeSpan.Zero;
            foreach (var pair in windows)
            {
                Tracked t = pair.Value;
                if (pair.Key == front)
                {
                    RobloxInFront = true;
                    if (t.InFrontSince == DateTime.MinValue) t.InFrontSince = now;
                    else if (yours && lastInput > t.InFrontSince && lastInput > t.LastInput && MouseOver(pair.Key))
                    {
                        t.LastInput = lastInput;
                        t.LastYourInput = lastInput;
                    }
                    if (now - t.LastYourInput < TimeSpan.FromMilliseconds(PlayingMs)) UserPlaying = true;
                }
                else
                {
                    t.InFrontSince = DateTime.MinValue;
                }

                TimeSpan idle = now - t.LastInput;
                if (idle >= RobloxIdle)
                {
                    RobloxIdle = idle;
                    RobloxMinimized = NativeMethods.IsIconic(pair.Key);
                }
            }
        }

        /// <summary>Is the PC's latest input the last nudge's own keypress?</summary>
        private static bool FromLastNudge(uint lastInputTick)
        {
            return unchecked((int)(lastInputTick - Nudger.StartedTick)) >= 0
                && unchecked((int)(lastInputTick - Nudger.OwnInputTick)) <= 20;
        }

        private static bool MouseOver(IntPtr hWnd)
        {
            NativeMethods.POINT p;
            NativeMethods.RECT r;
            return NativeMethods.GetCursorPos(out p) && NativeMethods.GetWindowRect(hWnd, out r)
                && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        }

        // ---------- nudging ----------

        private void NudgeWhatsDue(DateTime now)
        {
            bool youreBusy = NativeMethods.IdleMilliseconds() < PausedMs || NativeMethods.AnythingHeld();
            bool lockChecked = false;
            foreach (var pair in new List<KeyValuePair<IntPtr, Tracked>>(windows))
            {
                Tracked t = pair.Value;
                if (now < t.LastInput + Interval || now < t.RetryAt) continue;
                if (!lockChecked)
                {
                    lockChecked = true;
                    if (NativeMethods.InputDesktopLocked())
                    {
                        PcLocked = true;
                        if (!lockLogged) Logger.Write("The PC is locked, so Roblox can't be nudged until you unlock it");
                        lockLogged = true;
                        return;
                    }
                    lockLogged = false;
                }
                bool lastChance = now >= t.LastInput + Deadline;
                if (settings.WaitForPause && youreBusy && !lastChance)
                {
                    WaitingForPause = true;
                    continue;
                }
                Nudge(pair.Key, t, settings.WaitForPause && youreBusy ? "couldn't wait any longer for a pause" : "automatic");
                if (!running) return;
            }
        }

        private void Nudge(IntPtr hWnd, Tracked t, string why)
        {
            WindowSnapshot before = WindowSnapshot.Take(hWnd);
            string detail;
            bool reached;
            Leftover left, fixedNow;
            nudging = true;
            try
            {
                reached = Nudger.Nudge(hWnd, settings.Action, settings.CustomKey, settings.HideRoblox, out detail);
                left = NudgeCheck.VerifyAndRepair(hWnd, before, out fixedNow);
            }
            finally
            {
                nudging = false;
            }

            DateTime now = DateTime.UtcNow;
            LastNudge = DateTime.Now;
            LastNudgeWorked = reached && left == Leftover.None;
            if (LastNudgeWorked) NudgesOk++;
            else NudgesFailed++;
            if (reached)
            {
                t.LastInput = now;
                t.RetryAt = DateTime.MinValue;
            }
            else
            {
                t.RetryAt = now + Timing.Minutes(RetryMinutes);
            }

            string what = Describe(settings.Action, settings.CustomKey);
            if (left != Leftover.None)
            {
                string problem = NudgeCheck.Describe(left, before);
                Logger.Write("Focus restoration failure after a " + what + ": " + problem);
                Halt("Anti-AFK couldn't put your windows back exactly after a nudge (" + problem + ")");
                return;
            }
            if (fixedNow != Leftover.None)
            {
                repairsInARow++;
                Logger.Write("After a " + what + ", " + NudgeCheck.Describe(fixedNow, before) + ", so Anti-AFK put it back");
                if (repairsInARow >= 2)
                {
                    Halt("windows needed putting back after two nudges in a row");
                    return;
                }
            }
            else
            {
                repairsInARow = 0;
            }

            if (reached)
            {
                lastFailure = null;
                Logger.Write("Nudge OK: " + what + " (" + why + ")");
            }
            else
            {
                if (detail != lastFailure) Logger.Write("Nudge failed: " + detail + ". Retrying every 20 seconds");
                lastFailure = detail;
            }
        }

        /// <summary>Stop automatic nudging rather than keep interrupting you.</summary>
        private void Halt(string reason)
        {
            running = false;
            halted = true;
            HaltReason = reason;
            WaitingForPause = false;
            Logger.Write("Automatic nudging stopped: " + reason + ". Turn Anti-AFK back on when you're ready");
            if (Halted != null) Halted(reason);
        }

        /// <summary>If the PC goes to sleep, Roblox disconnects anyway. The screen can still turn off.</summary>
        private void UpdateKeepAwake()
        {
            bool want = running && settings.KeepPcAwake && windows.Count > 0;
            if (want == keepingAwake) return;
            keepingAwake = want;
            NativeMethods.SetThreadExecutionState(want
                ? NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED
                : NativeMethods.ES_CONTINUOUS);
        }

        public static string Minutes(int n)
        {
            return n == 1 ? "1 minute" : n + " minutes";
        }

        public static string Describe(NudgeAction action, Keys customKey)
        {
            if (action == NudgeAction.CameraNudge) return "camera nudge";
            if (action == NudgeAction.CustomKey && customKey != Keys.None) return "custom key " + KeyChoice.Name(customKey);
            return "jump";
        }

        private void OnChanged()
        {
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
            if (keepingAwake) NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
            keepingAwake = false;
        }
    }
}
