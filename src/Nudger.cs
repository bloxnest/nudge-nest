using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using N = NudgeNest.NativeMethods;

namespace NudgeNest
{
    public enum NudgeAction { Jump, CameraNudge, CustomKey }

    /// <summary>
    /// Roblox only counts input that reaches its own window, and a minimized or background window gets none.
    /// So a nudge hands Roblox the keyboard for under half a second, taps a key, and puts everything back:
    /// your window in front again, Roblox minimized again (keeping its size), the mouse where it was.
    /// With "hide" on, Roblox is fully see-through for that moment, so nothing flashes on screen.
    /// Nothing is injected into Roblox and its files and memory are never touched.
    /// </summary>
    internal static class Nudger
    {
        /// <summary>GetTickCount times of when the last nudge started and when it last pressed a key or moved
        /// the mouse. Input newer than OwnInputTick came from you, not from Anti-AFK.</summary>
        public static uint StartedTick, OwnInputTick;

        public static bool Nudge(IntPtr roblox, NudgeAction action, Keys customKey, bool hide, out string detail)
        {
            StartedTick = OwnInputTick = N.TickNow();
            if (!N.IsWindow(roblox))
            {
                detail = "the Roblox window closed";
                return false;
            }
            IntPtr previous = N.GetForegroundWindow();
            if (previous == roblox)
            {
                SendActivity(action, customKey);
                detail = "";
                return true;
            }

            N.POINT cursor;
            N.GetCursorPos(out cursor);
            var placement = new N.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(N.WINDOWPLACEMENT));
            N.GetWindowPlacement(roblox, ref placement);
            bool wasMinimized = N.IsIconic(roblox);
            IntPtr above = wasMinimized ? IntPtr.Zero : WindowAbove(roblox);
            // See-through only while Roblox can't be seen anyway (minimized or covered). Making a Roblox you can
            // see transparent is what made it vanish and come back, so a visible Roblox is left as it is.
            bool seeThrough = hide && !N.AnyPartVisible(roblox);

            long exStyle = N.GetExStyle(roblox);
            bool wasLayered = (exStyle & N.WS_EX_LAYERED) != 0;
            uint oldKey = 0, oldFlags = 0;
            byte oldAlpha = 255;
            bool reached = false, robloxTookMouse = false;
            try
            {
                if (seeThrough)
                {
                    if (wasLayered) N.GetLayeredWindowAttributes(roblox, out oldKey, out oldAlpha, out oldFlags);
                    else N.SetExStyle(roblox, exStyle | N.WS_EX_LAYERED);
                    N.SetLayeredWindowAttributes(roblox, 0, 0, N.LWA_ALPHA);
                }
                if (wasMinimized)
                {
                    N.SetAnimations(roblox, false);   // no restore/minimize animation for this split second
                    N.ShowWindow(roblox, N.SW_RESTORE);
                }
                if (!seeThrough && !wasMinimized) N.DwmFlush();   // start right after a screen refresh
                reached = BringToFront(roblox, delegate { StayInPlace(roblox, wasMinimized, above); });
                if (reached)
                {
                    Thread.Sleep(100);      // let Roblox notice it has the keyboard
                    SendActivity(action, customKey);
                    Thread.Sleep(150);      // let it read the key before it loses focus again
                }
                robloxTookMouse = MouseCentredOn(roblox, cursor);
            }
            finally
            {
                if (previous != IntPtr.Zero && N.IsWindow(previous)) BringToFront(previous);
                if (wasMinimized)
                {
                    // back to the taskbar; the saved placement remembers its size and "maximized"
                    placement.showCmd = N.SW_SHOWMINNOACTIVE;
                    N.SetWindowPlacement(roblox, ref placement);
                }
                else if (above != IntPtr.Zero)
                {
                    // it was open behind other windows: put it back at the same depth
                    N.SetWindowPos(roblox, above, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
                }
                if (seeThrough)
                {
                    if (wasLayered) N.SetLayeredWindowAttributes(roblox, oldKey, oldAlpha, oldFlags);
                    else N.SetExStyle(roblox, exStyle);
                }
                if (wasMinimized) N.SetAnimations(roblox, true);
                if (robloxTookMouse)
                {
                    N.SetCursorPos(cursor.X, cursor.Y);
                    MarkOwnInput();
                }
            }
            detail = reached ? "" : "Windows wouldn't let Roblox come to the front (is the PC locked?)";
            return reached;
        }

        internal static void MarkOwnInput()
        {
            OwnInputTick = N.TickNow();
        }

        /// <summary>Make a window the foreground window. Windows normally refuses this to background apps,
        /// so for a moment we share input state with whichever app is in front.</summary>
        internal static bool BringToFront(IntPtr hWnd)
        {
            return BringToFront(hWnd, null);
        }

        /// <param name="raised">Runs the moment the window has been raised (before waiting for it to take focus).</param>
        private static bool BringToFront(IntPtr hWnd, Action raised)
        {
            uint me = N.GetCurrentThreadId();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr front = N.GetForegroundWindow();
                if (front == hWnd) return true;
                uint pid;
                uint frontThread = front == IntPtr.Zero ? 0 : N.GetWindowThreadProcessId(front, out pid);
                bool attached = frontThread != 0 && frontThread != me && N.AttachThreadInput(me, frontThread, true);
                try
                {
                    // When the window is going straight back to its place, the first try skips the extra raise
                    if (raised == null || attempt > 0) N.BringWindowToTop(hWnd);
                    N.SetForegroundWindow(hWnd);
                    if (raised != null) raised();
                }
                finally
                {
                    if (attached) N.AttachThreadInput(me, frontThread, false);
                }
                for (int i = 0; i < 10; i++)
                {
                    if (N.GetForegroundWindow() == hWnd) return true;
                    Thread.Sleep(15);
                }
            }
            return false;
        }

        /// <summary>In shift-lock or first person, Roblox pulls the mouse to the middle of its window while it
        /// has focus. Only then is the mouse put back; if you moved it yourself, it's left alone.</summary>
        private static bool MouseCentredOn(IntPtr roblox, N.POINT before)
        {
            N.POINT now;
            N.RECT r;
            if (!N.GetCursorPos(out now) || !N.GetWindowRect(roblox, out r)) return false;
            if (now.X == before.X && now.Y == before.Y) return false;
            return Math.Abs(now.X - (r.Left + r.Right) / 2) <= 4 && Math.Abs(now.Y - (r.Top + r.Bottom) / 2) <= 4;
        }

        /// <summary>
        /// Getting the keyboard lifts Roblox to the top of the window stack. Put it straight back (it keeps the
        /// keyboard), so nothing moves on screen: under the window that was above it, or right at the bottom
        /// if it was minimized. The request is queued to Roblox right behind the activation, so both usually
        /// land in the same screen refresh.
        /// </summary>
        private static void StayInPlace(IntPtr roblox, bool wasMinimized, IntPtr above)
        {
            const uint keepAsIs = N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE | N.SWP_ASYNCWINDOWPOS;
            if (wasMinimized) N.SetWindowPos(roblox, N.HWND_BOTTOM, 0, 0, 0, 0, keepAsIs);
            else if (above != IntPtr.Zero) N.SetWindowPos(roblox, above, 0, 0, 0, 0, keepAsIs);
        }

        private static IntPtr WindowAbove(IntPtr hWnd)
        {
            IntPtr above = N.GetWindow(hWnd, N.GW_HWNDPREV);
            // tucking Roblox under an always-on-top window would make it always-on-top too
            if (above == IntPtr.Zero || (N.GetExStyle(above) & N.WS_EX_TOPMOST) != 0) return IntPtr.Zero;
            return above;
        }

        private static void SendActivity(NudgeAction action, Keys customKey)
        {
            if (action == NudgeAction.CameraNudge)
            {
                // turn the camera a hair right, then the same amount back; your character doesn't move
                TapKey(N.VK_RIGHT, true, 60);
                TapKey(N.VK_LEFT, true, 60);
            }
            else if (action == NudgeAction.CustomKey && KeyChoice.Problem(customKey) == null)
            {
                TapKey((ushort)customKey, KeyChoice.IsExtended(customKey), 80);
            }
            else
            {
                TapKey(N.VK_SPACE, false, 80);
            }
        }

        private static void TapKey(ushort vk, bool extended, int holdMs)
        {
            ushort scan = (ushort)N.MapVirtualKey(vk, 0);
            uint flags = extended ? N.KEYEVENTF_EXTENDEDKEY : 0;
            Send(vk, scan, flags);
            Thread.Sleep(holdMs);
            Send(vk, scan, flags | N.KEYEVENTF_KEYUP);
        }

        private static void Send(ushort vk, ushort scan, uint flags)
        {
            var input = new N.INPUT();
            input.type = N.INPUT_KEYBOARD;
            input.u.ki.wVk = vk;
            input.u.ki.wScan = scan;
            input.u.ki.dwFlags = flags;
            N.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(N.INPUT)));
            MarkOwnInput();
        }
    }
}
