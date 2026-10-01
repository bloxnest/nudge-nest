using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NudgeNest
{
    /// <summary>Your choices, kept in a small text file: %APPDATA%\NudgeNest\settings.ini.</summary>
    internal sealed class Settings
    {
        /// <summary>The folder the app used before it was renamed NudgeNest.</summary>
        private static readonly string OldFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobloxAntiAFK");

        public static readonly int[] IntervalChoices = { 1, 2, 5, 10, 15 };

        public int IntervalMinutes = 10;
        public NudgeAction Action = NudgeAction.Jump;
        public Keys CustomKey = Keys.None;
        public bool AutoStartWithRoblox = true;
        public bool WaitForPause = true;
        public bool HideRoblox = true;
        public bool KeepPcAwake = true;
        public bool TurnOnAtLaunch = true;

        internal static string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NudgeNest");

        private static string FilePath
        {
            get { return Path.Combine(Folder, "settings.ini"); }
        }

        /// <summary>First start after the rename: bring the settings and log over from the old folder.</summary>
        public static void MoveFromOldFolder()
        {
            try
            {
                if (Directory.Exists(Folder) || !Directory.Exists(OldFolder)) return;
                Directory.CreateDirectory(Folder);
                foreach (string name in new[] { "settings.ini", "log.txt", "log.old.txt" })
                {
                    string old = Path.Combine(OldFolder, name);
                    if (File.Exists(old)) File.Copy(old, Path.Combine(Folder, name));
                }
                Directory.Delete(OldFolder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    bool on = value != "0";
                    switch (key)
                    {
                        case "interval":
                            int minutes;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes)
                                && Array.IndexOf(IntervalChoices, minutes) >= 0)
                                s.IntervalMinutes = minutes;
                            break;
                        case "action":
                            s.Action = value.Equals("camera", StringComparison.OrdinalIgnoreCase) ? NudgeAction.CameraNudge
                                : value.Equals("custom", StringComparison.OrdinalIgnoreCase) ? NudgeAction.CustomKey
                                : NudgeAction.Jump;
                            break;
                        case "customkey":
                            int code;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out code)
                                && KeyChoice.Problem((Keys)code) == null)
                                s.CustomKey = (Keys)code;
                            break;
                        case "autostartwithroblox": s.AutoStartWithRoblox = on; break;
                        case "waitforpause": s.WaitForPause = on; break;
                        case "hideroblox": s.HideRoblox = on; break;
                        case "keeppcawake": s.KeepPcAwake = on; break;
                        case "turnonatlaunch": s.TurnOnAtLaunch = on; break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (s.Action == NudgeAction.CustomKey && s.CustomKey == Keys.None) s.Action = NudgeAction.Jump;
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(FilePath, new[]
                {
                    "interval=" + IntervalMinutes.ToString(CultureInfo.InvariantCulture),
                    "action=" + (Action == NudgeAction.CameraNudge ? "camera"
                        : Action == NudgeAction.CustomKey ? "custom" : "jump"),
                    "customKey=" + ((int)CustomKey).ToString(CultureInfo.InvariantCulture),
                    "autoStartWithRoblox=" + Flag(AutoStartWithRoblox),
                    "waitForPause=" + Flag(WaitForPause),
                    "hideRoblox=" + Flag(HideRoblox),
                    "keepPcAwake=" + Flag(KeepPcAwake),
                    "turnOnAtLaunch=" + Flag(TurnOnAtLaunch),
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string Flag(bool on) { return on ? "1" : "0"; }
    }

    /// <summary>"Start with Windows": a per-user Run entry (no admin rights needed).</summary>
    internal static class StartWithWindows
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NudgeNest";
        private const string OldValueName = "Roblox Anti-AFK";   // before the rename

        /// <summary>Self-tests only: never touch the real Run entry.</summary>
        internal static bool LeaveAlone = false;

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --tray"; }
        }

        public static bool IsOn
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(ValueName) != null;
            }
        }

        public static bool Set(bool on)
        {
            if (LeaveAlone) return true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) key.SetValue(ValueName, Command);
                    else key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }

        /// <summary>Keeps the entry pointing at this exe if the folder was moved, and renames the old entry.</summary>
        public static void RefreshPath()
        {
            if (LeaveAlone) return;
            bool hadOld = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null && key.GetValue(OldValueName) != null)
                    {
                        key.DeleteValue(OldValueName, false);
                        hadOld = true;
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            if (hadOld || IsOn) Set(true);
        }
    }
}
