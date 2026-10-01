using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NudgeNest
{
    /// <summary>The Windows API calls the app needs (windows, focus, input, idle time).</summary>
    internal static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint colorKey, out byte alpha, out uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint command);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr large, out IntPtr small, uint iconSize);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dest, IntPtr a, IntPtr b, int mode);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong32(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const int RGN_AND = 1, RGN_DIFF = 4, NULLREGION = 1;
        public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
        public const long WS_EX_LAYERED = 0x00080000;
        public const uint LWA_ALPHA = 0x2;
        public const int SW_SHOWMAXIMIZED = 3, SW_SHOWNOACTIVATE = 4, SW_SHOWMINNOACTIVE = 7, SW_RESTORE = 9;
        public const int WPF_RESTORETOMAXIMIZED = 2;
        public const uint GW_HWNDPREV = 3;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_ASYNCWINDOWPOS = 0x4000;
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
        public const ushort VK_SPACE = 0x20, VK_LEFT = 0x25, VK_RIGHT = 0x27;
        public const uint ES_SYSTEM_REQUIRED = 0x1, ES_CONTINUOUS = 0x80000000;

        /// <summary>True while a mouse button, Shift, Ctrl, Alt or a Windows key is held down.</summary>
        public static bool AnythingHeld()
        {
            int[] keys = { 0x01, 0x02, 0x04, 0x10, 0x11, 0x12, 0x5B, 0x5C };
            foreach (int key in keys)
                if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
            return false;
        }

        /// <summary>Hand unused memory back to Windows (the app sits idle in the tray almost all the time).</summary>
        public static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(GetCurrentProcess());
        }

        public static long GetExStyle(IntPtr hWnd)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, GWL_EXSTYLE).ToInt64() : GetWindowLong32(hWnd, GWL_EXSTYLE);
        }

        public static void SetExStyle(IntPtr hWnd, long value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, GWL_EXSTYLE, new IntPtr(value));
            else SetWindowLong32(hWnd, GWL_EXSTYLE, (int)value);
        }

        /// <summary>Milliseconds since the last keyboard/mouse input anywhere on the PC.</summary>
        public static uint IdleMilliseconds()
        {
            int idle = unchecked((int)(TickNow() - LastInputTick()));
            return idle < 0 ? 0u : (uint)idle;   // the two clocks can disagree by a tick; never wrap around
        }

        /// <summary>When the last keyboard/mouse input happened, on the GetTickCount clock.</summary>
        public static uint LastInputTick()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            return GetLastInputInfo(ref info) ? info.dwTime : TickNow();
        }

        public static uint TickNow()
        {
            return unchecked((uint)Environment.TickCount);
        }

        /// <summary>
        /// Can you see any part of this window right now? It can't be seen if every bit of it is covered by
        /// windows above it (see-through overlays and hidden/cloaked windows don't count as covering) or is
        /// off screen.
        /// </summary>
        public static bool AnyPartVisible(IntPtr hWnd)
        {
            RECT r;
            if (IsIconic(hWnd) || !VisibleBounds(hWnd, out r)) return false;
            int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77);   // the whole desktop, all monitors
            IntPtr shown = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
            IntPtr screen = CreateRectRgn(vx, vy, vx + GetSystemMetrics(78), vy + GetSystemMetrics(79));
            try
            {
                int left = CombineRgn(shown, shown, screen, RGN_AND);
                for (IntPtr w = GetWindow(hWnd, GW_HWNDPREV); w != IntPtr.Zero && left > NULLREGION; w = GetWindow(w, GW_HWNDPREV))
                {
                    if (!IsWindowVisible(w) || IsIconic(w) || IsCloaked(w)) continue;
                    if ((GetExStyle(w) & (WS_EX_LAYERED | WS_EX_TRANSPARENT)) != 0) continue;
                    RECT c;
                    if (!VisibleBounds(w, out c)) continue;
                    IntPtr cover = CreateRectRgn(c.Left, c.Top, c.Right, c.Bottom);
                    left = CombineRgn(shown, shown, cover, RGN_DIFF);
                    DeleteObject(cover);
                }
                return left > NULLREGION;
            }
            finally
            {
                DeleteObject(shown);
                DeleteObject(screen);
            }
        }

        /// <summary>The window's on-screen rectangle without the invisible resize borders.</summary>
        private static bool VisibleBounds(IntPtr hWnd, out RECT r)
        {
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0
                && !GetWindowRect(hWnd, out r)) return false;
            return r.Right > r.Left && r.Bottom > r.Top;
        }

        private static bool IsCloaked(IntPtr hWnd)
        {
            int cloaked;
            return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0;
        }

        /// <summary>Turns a window's minimize/restore animations off (or back on).</summary>
        public static void SetAnimations(IntPtr hWnd, bool on)
        {
            int disabled = on ? 0 : 1;
            DwmSetWindowAttribute(hWnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, 4);
        }

        /// <summary>True while the lock screen (or a UAC prompt) is up: Windows delivers no keys to apps then.</summary>
        public static bool InputDesktopLocked()
        {
            IntPtr desktop = OpenInputDesktop(0, false, 0x0001);   // DESKTOP_READOBJECTS
            if (desktop == IntPtr.Zero) return true;
            try
            {
                var name = new StringBuilder(64);
                int needed;
                return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out needed)   // UOI_NAME
                    && !name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CloseDesktop(desktop);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }
    }
}
