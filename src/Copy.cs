namespace NudgeNest
{
    /// <summary>The words in the app, in one place. Every claim here is true; keep it that way.</summary>
    internal static class Copy
    {
        public const string Tagline = "A small nudge for your Roblox hangout.";
        public const string TrayHint = "Closing keeps it in the tray.";

        public const string ActivityJump = "Jump (Space)";
        public const string ActivityCamera = "Camera nudge (→ then ←)";
        public const string JumpHelp = "Taps Space. Stands you up if you're sitting.";
        public const string CameraHelp = "Turns the camera a hair and back. Keeps you seated.";
        public const string CustomHelp = "Taps your custom key: ";

        public const string IntervalHelp = "How long Roblox can go without input before it's nudged.";
        public const string CustomKeyHelp = "Esc, Enter and / aren't allowed.";
        public const string AutoStartHelp = "Turns on when Roblox opens; standby when it closes.";
        public const string LaunchHelp = "Starts in the ON state.";
        public const string WindowsHelp = "Opens NudgeNest straight to the tray when you sign in.";
        public const string PauseHelp = "Waits for a 3-second pause, never past 18 idle minutes.";
        public const string HideHelp = "Only when Roblox is minimized or covered.";
        public const string AwakeHelp = "While Roblox is open. Your screen can still turn off.";
        public const string LogHelp = "Settings and log live in %APPDATA%\\NudgeNest.";

        public const string AboutIntro = "A free, open-source Windows app that keeps Roblox from disconnecting you after "
            + "20 idle minutes. Roblox can stay minimized, and your windows are put back after every nudge.";

        public static readonly string[][] AboutSections =
        {
            new[] { "Privacy", "No internet connections, accounts, telemetry or ads. It only saves your settings and a "
                + "small log (never more than about 256 KB) in your AppData folder." },
            new[] { "Safety", "It never touches Roblox's files, memory or process: no injection, no DLLs. It only "
                + "presses a key, like you would, and checks your windows after every nudge." },
            new[] { "Open source", "Every line is on GitHub under the MIT license. Releases are built by GitHub from that "
                + "code, with a SHA-256 checksum and a build attestation you can verify." },
            new[] { "Disclaimer", "Not made, approved or endorsed by Roblox Corporation. Roblox is a trademark of Roblox "
                + "Corporation. Nobody can promise anything about bans, so use it at your own risk." },
        };
    }
}
