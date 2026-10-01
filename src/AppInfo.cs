using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;

[assembly: AssemblyTitle("NudgeNest")]
[assembly: AssemblyDescription("NudgeNest: a free anti-AFK app for Roblox, by xRed1.")]
[assembly: AssemblyProduct("NudgeNest")]
[assembly: AssemblyCompany("xRed1")]
[assembly: AssemblyCopyright("Copyright (c) 2026 xRed1. MIT License.")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace NudgeNest
{
    /// <summary>Name, version, developer and links, shown on the About screen.</summary>
    internal static class AppInfo
    {
        public const string Name = "NudgeNest";
        public const string Version = "2.0.0";
        public const string Developer = "xRed1";
        public const string Website = "https://bloxnest.github.io/nudge-nest/";
        public const string Source = "https://github.com/bloxnest/nudge-nest";

        /// <summary>The logo built into the exe (assets\logo-*.png), or null if it isn't there.</summary>
        public static Image Logo(int size)
        {
            string name = size <= 32 ? "logo-32.png" : size <= 64 ? "logo-64.png" : "logo-256.png";
            Stream stream = typeof(AppInfo).Assembly.GetManifestResourceStream("NudgeNest." + name);
            if (stream == null) return null;
            using (stream)
            using (var image = Image.FromStream(stream))
                return new Bitmap(image, size, size);
        }

        public static void Open(string url)
        {
            try { Process.Start(url); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
