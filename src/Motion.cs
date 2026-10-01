using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NudgeNest
{
    /// <summary>
    /// Drives every animation in the app from one ~60 fps timer that only runs while something is moving,
    /// so the app costs nothing when it's still. Follows the Windows "Animation effects" setting: with it off,
    /// everything changes instantly.
    /// </summary>
    internal static class Motion
    {
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);

        private static readonly List<Tween> active = new List<Tween>();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static Timer timer;

        /// <summary>Self-tests can turn this off; normally it follows Windows.</summary>
        internal static bool Enabled = WindowsAnimationsOn();

        public static double Now
        {
            get { return clock.Elapsed.TotalMilliseconds; }
        }

        /// <summary>True while any animation is moving (the timer is running).</summary>
        public static bool Busy
        {
            get { return active.Count > 0; }
        }

        internal static void Run(Tween tween)
        {
            if (!active.Contains(tween)) active.Add(tween);
            if (timer == null)
            {
                timer = new Timer();
                timer.Interval = 14;
                timer.Tick += delegate { Step(); };
            }
            if (!timer.Enabled)
            {
                timeBeginPeriod(1);   // smooth frame timing, only while something moves
                timer.Start();
            }
        }

        private static void Step()
        {
            double now = Now;
            foreach (Tween t in active.ToArray())
                if (!t.Step(now) && !t.Running) active.Remove(t);   // Done may have started it again
            if (active.Count == 0 && timer.Enabled)
            {
                timer.Stop();
                timeEndPeriod(1);
            }
        }

        internal static void Forget(Tween tween)
        {
            active.Remove(tween);
        }

        /// <summary>Fast start, gentle stop: the default for anything that moves.</summary>
        public static double EaseOut(double t)
        {
            t = 1 - t;
            return 1 - t * t * t;
        }

        /// <summary>Gentle at both ends: for things that travel further, like pages.</summary>
        public static double EaseInOut(double t)
        {
            return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        private static bool WindowsAnimationsOn()
        {
            bool on;
            return !SystemParametersInfo(0x1042, 0, out on, 0) || on;   // SPI_GETCLIENTAREAANIMATION
        }
    }

    /// <summary>A number that glides to new values. Changed runs on every frame; Done once it arrives.</summary>
    internal sealed class Tween
    {
        private readonly Action changed;
        private double from, to, start, duration;
        private bool running;

        public Func<double, double> Ease = Motion.EaseOut;
        public Action Done;

        public Tween(double initial, Action changed)
        {
            Value = from = to = initial;
            this.changed = changed;
        }

        public double Value { get; private set; }

        public double Target
        {
            get { return to; }
        }

        public bool Running
        {
            get { return running; }
        }

        public void To(double target, double milliseconds)
        {
            if (target == to && (running || Value == target)) return;
            if (!Motion.Enabled || milliseconds <= 0)
            {
                // no animation: arrive at once, but still run Done so sequences carry on
                running = false;
                Motion.Forget(this);
                from = to = Value = target;
                if (changed != null) changed();
                if (Done != null) Done();
                return;
            }
            from = Value;
            to = target;
            start = Motion.Now;
            duration = milliseconds;
            running = true;
            Motion.Run(this);
        }

        /// <summary>Jump straight to a value, no animation.</summary>
        public void Snap(double target)
        {
            bool wasRunning = running;
            running = false;
            Motion.Forget(this);
            from = to = Value = target;
            if (changed != null) changed();
            if (wasRunning && Done != null) Done();
        }

        internal bool Step(double now)
        {
            if (!running) return false;
            double t = Math.Min(1, (now - start) / duration);
            Value = from + (to - from) * Ease(t);
            if (changed != null) changed();
            if (t < 1) return true;
            running = false;
            if (Done != null) Done();
            return false;
        }
    }
}
