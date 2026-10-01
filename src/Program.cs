using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace NudgeNest
{
    internal static class Program
    {
        private const string MutexName = @"Local\NudgeNest.Running";
        private const string ShowEventName = @"Local\NudgeNest.Show";
        private const string ExitEventName = @"Local\NudgeNest.Exit";

        /// <summary>
        ///   NudgeNest.exe               open the window
        ///   NudgeNest.exe --tray        start hidden in the tray (used by "Start with Windows")
        ///   NudgeNest.exe --exit        close the copy that's running
        ///   NudgeNest.exe --nudge-once  nudge every Roblox window once and exit (testing);
        ///                               add --camera / --jump / --custom / --no-hide to override the settings
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            Settings.MoveFromOldFolder();
            var flags = new List<string>(args);
            if (flags.Contains("--nudge-once")) return NudgeOnce(flags);
            if (flags.Contains("--exit"))
            {
                Signal(ExitEventName);
                WaitUntilClosed();
                return 0;
            }

            bool firstCopy;
            using (var mutex = new Mutex(true, MutexName, out firstCopy))
            {
                if (!firstCopy)
                {
                    // already running: bring that copy's window up instead of starting another
                    NativeMethods.AllowSetForegroundWindow(-1);
                    Signal(ShowEventName);
                    return 0;
                }
                // the app before it was renamed NudgeNest: close it, so the two don't both nudge
                Signal(@"Local\RobloxAntiAFK.Exit");
                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm(flags.Contains("--tray"), show, exit));
                }
                mutex.ReleaseMutex();
            }
            return 0;
        }

        private static void Signal(string eventName)
        {
            EventWaitHandle handle;
            if (EventWaitHandle.TryOpenExisting(eventName, out handle))
                using (handle) handle.Set();
        }

        /// <summary>The running copy holds the mutex until it ends, so getting the mutex means it's gone.</summary>
        private static void WaitUntilClosed()
        {
            Mutex running;
            if (!Mutex.TryOpenExisting(MutexName, out running)) return;
            using (running)
            {
                try
                {
                    if (running.WaitOne(5000)) running.ReleaseMutex();
                }
                catch (AbandonedMutexException) { }   // it ended without releasing: also gone
            }
            Thread.Sleep(300);   // give Windows a moment to unlock the exe file
        }

        /// <summary>
        /// Nudges every Roblox window once and checks the windows were put back. Exit code 0 = all good,
        /// 1 = a nudge didn't reach Roblox, 2 = Roblox isn't running, 3 = windows couldn't be put back.
        /// </summary>
        private static int NudgeOnce(List<string> flags)
        {
            Settings settings = Settings.Load();
            NudgeAction action = settings.Action;
            if (flags.Contains("--camera")) action = NudgeAction.CameraNudge;
            if (flags.Contains("--jump")) action = NudgeAction.Jump;
            if (flags.Contains("--custom")) action = NudgeAction.CustomKey;
            bool hide = settings.HideRoblox && !flags.Contains("--no-hide");

            List<IntPtr> found = RobloxWindows.Find();
            if (found.Count == 0)
            {
                Console.WriteLine("Roblox isn't running");
                return 2;
            }
            int worst = 0;
            foreach (IntPtr hWnd in found)
            {
                WindowSnapshot before = WindowSnapshot.Take(hWnd);
                string detail;
                bool ok = Nudger.Nudge(hWnd, action, settings.CustomKey, hide, out detail);
                Leftover fixedNow;
                Leftover left = NudgeCheck.VerifyAndRepair(hWnd, before, out fixedNow);
                string id = hWnd.ToString("X");
                if (left != Leftover.None)
                {
                    Console.WriteLine("RESTORE FAILED " + id + ": " + NudgeCheck.Describe(left, before));
                    worst = 3;
                }
                else if (!ok)
                {
                    Console.WriteLine("FAILED " + id + ": " + detail);
                    worst = Math.Max(worst, 1);
                }
                else
                {
                    Console.WriteLine("nudged " + id + " (" + AntiAfkEngine.Describe(action, settings.CustomKey) + "), windows verified"
                        + (fixedNow != Leftover.None ? " after putting back: " + NudgeCheck.Describe(fixedNow, before) : ""));
                }
            }
            return worst;
        }
    }
}
