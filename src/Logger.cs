using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NudgeNest
{
    /// <summary>
    /// A small log in %APPDATA%\NudgeNest\log.txt. When it passes 128 KB it becomes log.old.txt (replacing
    /// the previous one) and a fresh log.txt starts, so the two together never exceed about 256 KB.
    /// </summary>
    internal static class Logger
    {
        private const int KeepInMemory = 300;

        internal static string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NudgeNest");
        internal static long MaxFileBytes = 128 * 1024;

        private static readonly List<string> recent = new List<string>();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>Raised with each new line, for the log window.</summary>
        public static event Action<string> Written;

        public static string FilePath
        {
            get { return Path.Combine(Folder, "log.txt"); }
        }

        public static IList<string> Recent
        {
            get { return recent.AsReadOnly(); }
        }

        public static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message;
            recent.Add(line);
            if (recent.Count > KeepInMemory) recent.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxFileBytes)
                {
                    string old = Path.Combine(Folder, "log.old.txt");
                    File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, line + Environment.NewLine, Utf8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (Written != null) Written(line);
        }
    }
}
