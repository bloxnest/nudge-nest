using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using N = NudgeNest.NativeMethods;

namespace NudgeNest
{
    /// <summary>What a nudge must leave exactly as it found it, recorded just before the nudge.</summary>
    internal sealed class WindowSnapshot
    {
        public IntPtr Foreground;
        public N.WINDOWPLACEMENT Placement;
        public bool Minimized, Maximized;
        public N.RECT Rect;
        public bool Layered;
        public byte Alpha;
        public N.POINT Cursor;

        public static WindowSnapshot Take(IntPtr roblox)
        {
            var s = new WindowSnapshot();
            s.Foreground = N.GetForegroundWindow();
            s.Placement.length = Marshal.SizeOf(typeof(N.WINDOWPLACEMENT));
            N.GetWindowPlacement(roblox, ref s.Placement);
            s.Minimized = N.IsIconic(roblox);
            s.Maximized = !s.Minimized && N.IsZoomed(roblox);
            N.GetWindowRect(roblox, out s.Rect);
            s.Layered = (N.GetExStyle(roblox) & N.WS_EX_LAYERED) != 0;
            s.Alpha = 255;
            if (s.Layered)
            {
                uint key, flags;
                N.GetLayeredWindowAttributes(roblox, out key, out s.Alpha, out flags);
            }
            N.GetCursorPos(out s.Cursor);
            return s;
        }
    }

    [Flags]
    internal enum Leftover { None = 0, Invisible = 1, ShowState = 2, Moved = 4, Focus = 8, Cursor = 16 }

    /// <summary>
    /// After every nudge: is your window in front again, is Roblox minimized/maximized/normal as before,
    /// at the same position and size, fully visible, and is the mouse where it was? Anything that isn't
    /// gets one attempt at putting it back.
    /// </summary>
    internal static class NudgeCheck
    {
        /// <summary>Returns what was still wrong at the end (None = all good). fixedNow says what had to be put back.</summary>
        public static Leftover VerifyAndRepair(IntPtr roblox, WindowSnapshot before, out Leftover fixedNow)
        {
            fixedNow = Leftover.None;
            if (!N.IsWindow(roblox)) return Leftover.None;   // Roblox closed in the meantime: nothing to put back
            bool youActed = YouActedSinceNudge();
            Leftover wrong = Compare(roblox, before, youActed);
            if (wrong == Leftover.None) return wrong;
            Thread.Sleep(150);                               // window changes can land a moment late
            wrong = Compare(roblox, before, youActed);
            if (wrong == Leftover.None) return wrong;

            Repair(roblox, before, wrong);
            Thread.Sleep(150);
            Leftover still = Compare(roblox, before, youActed);
            fixedNow = wrong & ~still;
            return still;
        }

        /// <summary>Did you press a key, click or move the mouse after the nudge's own input?</summary>
        private static bool YouActedSinceNudge()
        {
            return unchecked((int)(N.LastInputTick() - Nudger.OwnInputTick)) > 20;
        }

        private static Leftover Compare(IntPtr roblox, WindowSnapshot before, bool youActed)
        {
            Leftover wrong = Leftover.None;

            bool layered = (N.GetExStyle(roblox) & N.WS_EX_LAYERED) != 0;
            if (layered != before.Layered) wrong |= Leftover.Invisible;
            else if (layered)
            {
                uint key, flags;
                byte alpha;
                N.GetLayeredWindowAttributes(roblox, out key, out alpha, out flags);
                if (alpha != before.Alpha) wrong |= Leftover.Invisible;
            }

            bool minimized = N.IsIconic(roblox);
            bool maximized = !minimized && N.IsZoomed(roblox);
            if (minimized != before.Minimized || maximized != before.Maximized) wrong |= Leftover.ShowState;

            var p = new N.WINDOWPLACEMENT();
            p.length = Marshal.SizeOf(typeof(N.WINDOWPLACEMENT));
            N.GetWindowPlacement(roblox, ref p);
            if (!Same(p.rcNormalPosition, before.Placement.rcNormalPosition)
                || (p.flags & N.WPF_RESTORETOMAXIMIZED) != (before.Placement.flags & N.WPF_RESTORETOMAXIMIZED))
                wrong |= Leftover.Moved;
            if (!minimized && !before.Minimized)
            {
                N.RECT r;
                N.GetWindowRect(roblox, out r);
                if (!Same(r, before.Rect)) wrong |= Leftover.Moved;
            }

            IntPtr previous = before.Foreground;
            if (previous != IntPtr.Zero && previous != roblox && N.IsWindow(previous))
            {
                IntPtr front = N.GetForegroundWindow();
                // another app in front is fine if you clicked it yourself; Roblox (or nothing) in front never is
                if (front != previous && (front == roblox || front == IntPtr.Zero || !youActed))
                    wrong |= Leftover.Focus;
            }

            N.POINT c;
            N.GetCursorPos(out c);
            if ((c.X != before.Cursor.X || c.Y != before.Cursor.Y) && !youActed) wrong |= Leftover.Cursor;
            return wrong;
        }

        private static void Repair(IntPtr roblox, WindowSnapshot before, Leftover wrong)
        {
            if ((wrong & Leftover.Invisible) != 0)
            {
                if (before.Layered) N.SetLayeredWindowAttributes(roblox, 0, before.Alpha, N.LWA_ALPHA);
                else N.SetExStyle(roblox, N.GetExStyle(roblox) & ~N.WS_EX_LAYERED);
            }
            if ((wrong & (Leftover.ShowState | Leftover.Moved)) != 0)
            {
                N.WINDOWPLACEMENT p = before.Placement;
                p.showCmd = before.Minimized ? N.SW_SHOWMINNOACTIVE
                    : before.Maximized ? N.SW_SHOWMAXIMIZED : N.SW_SHOWNOACTIVATE;
                N.SetWindowPlacement(roblox, ref p);
            }
            IntPtr previous = before.Foreground;
            if (previous != IntPtr.Zero && previous != roblox && N.IsWindow(previous))
            {
                IntPtr front = N.GetForegroundWindow();
                if ((wrong & Leftover.Focus) != 0 || front == roblox || front == IntPtr.Zero)
                    Nudger.BringToFront(previous);
            }
            if ((wrong & Leftover.Cursor) != 0)
            {
                N.SetCursorPos(before.Cursor.X, before.Cursor.Y);
                Nudger.MarkOwnInput();
            }
        }

        public static string Describe(Leftover what, WindowSnapshot before)
        {
            var parts = new List<string>();
            if ((what & Leftover.Invisible) != 0) parts.Add("Roblox was left see-through");
            if ((what & Leftover.ShowState) != 0)
                parts.Add("Roblox didn't go back to " + (before.Minimized ? "minimized"
                    : before.Maximized ? "maximized" : "its normal size"));
            if ((what & Leftover.Moved) != 0) parts.Add("Roblox's position or size changed");
            if ((what & Leftover.Focus) != 0) parts.Add("your window didn't get the focus back");
            if ((what & Leftover.Cursor) != 0) parts.Add("the mouse pointer moved");
            return string.Join(", ", parts);
        }

        private static bool Same(N.RECT a, N.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }
    }
}
