using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// A stand-in for the Roblox player, used only by the self-tests: a window of class WINDOWSCLIENT in a
/// process named RobloxPlayerBeta.exe that writes every key it receives to a log file.
///   --log=path --life=seconds --x=N --y=N --w=N --h=N --start=normal|minimized|maxmin --class=Name --color=RRGGBB
///   --misbehave=move           jumps 40 px sideways each time it gets focus (can be put back)
///   --misbehave=resist         keeps itself 80 px off for 3 s after getting focus (can't be put back)
///   --misbehave=center-cursor  pulls the mouse to its middle while it has focus, like Shift Lock
/// </summary>
internal static class FakeRoblox
{
    private delegate IntPtr WndProcFn(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public WndProcFn lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out MSG msg, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr callback);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint colorref);

    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    private static readonly WndProcFn Proc = WndProc;   // kept alive as long as the window
    private static StreamWriter log;
    private static string misbehave = "";
    private static int homeX, homeY, resistUntil;

    [STAThread]
    private static int Main(string[] args)
    {
        string logPath = null, start = "normal", cls = "WINDOWSCLIENT";
        double life = 60;
        int x = 120, y = 120, width = 520, height = 340;
        uint rgb = 0xFF00FF;   // magenta, so a test can spot this window on screen
        foreach (string a in args)
        {
            int eq = a.IndexOf('=');
            string key = eq < 0 ? a : a.Substring(0, eq), v = eq < 0 ? "" : a.Substring(eq + 1);
            if (key == "--log") logPath = v;
            else if (key == "--life") life = double.Parse(v, CultureInfo.InvariantCulture);
            else if (key == "--x") x = int.Parse(v);
            else if (key == "--y") y = int.Parse(v);
            else if (key == "--w") width = int.Parse(v);
            else if (key == "--h") height = int.Parse(v);
            else if (key == "--start") start = v;
            else if (key == "--class") cls = v;
            else if (key == "--misbehave") misbehave = v;
            else if (key == "--color") rgb = uint.Parse(v, NumberStyles.HexNumber);
        }
        if (logPath != null)
        {
            log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete));
            log.AutoFlush = true;
        }

        var wc = new WNDCLASSEX();
        wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
        wc.lpfnWndProc = Proc;
        wc.hInstance = GetModuleHandle(null);
        wc.hbrBackground = CreateSolidBrush(((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));   // RGB -> COLORREF
        wc.lpszClassName = cls;
        RegisterClassEx(ref wc);
        homeX = x;
        homeY = y;
        IntPtr window = CreateWindowEx(0, cls, "Roblox", WS_OVERLAPPEDWINDOW, x, y, width, height,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (start == "minimized") ShowWindow(window, 7);                       // SW_SHOWMINNOACTIVE
        else if (start == "maxmin") { ShowWindow(window, 3); ShowWindow(window, 6); }   // maximized, then minimized
        else ShowWindow(window, 4);                                            // SW_SHOWNOACTIVATE
        SetTimer(window, new IntPtr(1), (uint)(life * 1000), IntPtr.Zero);
        SetTimer(window, new IntPtr(2), 30, IntPtr.Zero);
        Write("ready " + window.ToInt64().ToString("X"));

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        Write("exit");
        return 0;
    }

    private static IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case 0x0100: case 0x0104:   // WM_KEYDOWN, WM_SYSKEYDOWN
                Write("down " + w.ToInt64().ToString("X2"));
                break;
            case 0x0101: case 0x0105:   // WM_KEYUP, WM_SYSKEYUP
                Write("up " + w.ToInt64().ToString("X2"));
                break;
            case 0x0006:                // WM_ACTIVATE
                bool active = (w.ToInt64() & 0xFFFF) != 0;
                Write(active ? "activated" : "deactivated");
                if (active && misbehave == "move")
                {
                    RECT r;
                    GetWindowRect(h, out r);
                    SetWindowPos(h, IntPtr.Zero, r.Left + 40, r.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                }
                if (active && misbehave == "resist") resistUntil = Environment.TickCount + 3000;
                break;
            case 0x0113:                // WM_TIMER
                if (w.ToInt64() == 1) DestroyWindow(h);
                else Misbehave(h);
                break;
            case 0x0002:                // WM_DESTROY
                PostQuitMessage(0);
                break;
        }
        return DefWindowProc(h, msg, w, l);
    }

    private static void Misbehave(IntPtr h)
    {
        if (misbehave == "resist" && unchecked(Environment.TickCount - resistUntil) < 0)
            SetWindowPos(h, IntPtr.Zero, homeX + 80, homeY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        if (misbehave == "center-cursor" && GetForegroundWindow() == h)
        {
            RECT r;
            GetWindowRect(h, out r);
            SetCursorPos((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        }
    }

    private static void Write(string line)
    {
        if (log != null) log.WriteLine(Environment.TickCount + " " + line);
    }
}
