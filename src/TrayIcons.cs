using System.Drawing;
using System.IO;

namespace NudgeNest
{
    /// <summary>
    /// The hidden-icons icon: a small tile that says AFK, in smooth Bahnschrift letters.
    /// Its colour shows the state: mint = on, amber = standby, grey = off, red = stopped.
    /// The pictures are drawn by tools/make_icon.py (assets\tray-*.ico) and built into the exe.
    /// </summary>
    internal static class TrayIcons
    {
        public const string On = "on", Standby = "standby", Off = "off", Stopped = "stopped";

        /// <summary>The icon for a state, picked from the sizes drawn for 16-32 px.</summary>
        public static Icon Make(string state, int size)
        {
            using (Stream s = typeof(TrayIcons).Assembly.GetManifestResourceStream("NudgeNest.tray-" + state + ".ico"))
                return new Icon(s, size, size);
        }
    }
}
