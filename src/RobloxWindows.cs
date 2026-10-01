using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NudgeNest
{
    /// <summary>Finds the Roblox game windows: window class WINDOWSCLIENT owned by RobloxPlayerBeta.exe.</summary>
    internal static class RobloxWindows
    {
        // pid -> "is a Roblox player", so each process name is looked up only once
        private static readonly Dictionary<uint, bool> knownPids = new Dictionary<uint, bool>();

        /// <summary>Self-tests only: when set, only these processes count (their stand-in Roblox windows).</summary>
        internal static HashSet<uint> OnlyPids = null;

        public static List<IntPtr> Find()
        {
            var found = new List<IntPtr>();
            var seenPids = new HashSet<uint>();
            var cls = new StringBuilder(32);
            NativeMethods.EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;   // minimized windows still count as visible
                cls.Length = 0;
                NativeMethods.GetClassName(hWnd, cls, cls.Capacity);
                if (cls.ToString() != "WINDOWSCLIENT") return true;
                uint pid;
                NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
                if (OnlyPids != null && !OnlyPids.Contains(pid)) return true;
                seenPids.Add(pid);
                if (IsRobloxPlayer(pid)) found.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            foreach (uint pid in new List<uint>(knownPids.Keys))
                if (!seenPids.Contains(pid)) knownPids.Remove(pid);
            return found;
        }

        /// <summary>When the Roblox process that owns this window started (local time), or MinValue if unknown.</summary>
        public static DateTime StartedAt(IntPtr hWnd)
        {
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    return p.StartTime;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            return DateTime.MinValue;
        }

        private static bool IsRobloxPlayer(uint pid)
        {
            bool yes;
            if (knownPids.TryGetValue(pid, out yes)) return yes;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    yes = p.ProcessName.StartsWith("RobloxPlayer", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { yes = false; }          // process already gone
            catch (InvalidOperationException) { yes = false; }
            knownPids[pid] = yes;
            return yes;
        }
    }
}
