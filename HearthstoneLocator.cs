using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace HearthStoner
{
    /// <summary>Finds the Hearthstone install, its log folder, and makes sure Power logging is switched on.</summary>
    public static class HearthstoneLocator
    {
        public static string FindInstallDir(string overridePath = null)
        {
            if (!string.IsNullOrEmpty(overridePath)) return overridePath;

            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var key = RegistryKey.OpenBaseKey(hive, view)
                               .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone"))
                    {
                        var loc = key?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) return loc;
                    }
                }
                catch { /* keep looking */ }
            }

            var defaults = new[]
            {
                @"C:\Program Files (x86)\Hearthstone",
                @"C:\Program Files\Hearthstone",
            };
            return defaults.FirstOrDefault(Directory.Exists);
        }

        /// <summary>
        /// Newer clients write each session to Logs\Hearthstone_YYYY_MM_DD_HH_MM_SS\Power.log.
        /// Older ones wrote Logs\Power.log directly. Returns the most recently active one.
        /// </summary>
        public static string FindLatestPowerLog(string installDir)
        {
            if (installDir == null) return null;
            var logs = Path.Combine(installDir, "Logs");
            if (!Directory.Exists(logs)) return null;

            var candidates = Directory.GetDirectories(logs, "Hearthstone_*")
                .Select(d => Path.Combine(d, "Power.log"))
                .Concat(new[] { Path.Combine(logs, "Power.log") })
                .Where(File.Exists);

            return candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                   // Session folder exists but Power.log not written yet: watch where it will appear.
                   ?? Directory.GetDirectories(logs, "Hearthstone_*")
                       .OrderByDescending(Directory.GetCreationTimeUtc)
                       .Select(d => Path.Combine(d, "Power.log"))
                       .FirstOrDefault();
        }

        /// <summary>
        /// True while you're in a game, from the newest Power.log: a CREATE_GAME after the last "value=COMPLETE"
        /// (a game's end), in a log written in the last 10 minutes (else Hearthstone was closed mid-game).
        /// </summary>
        public static bool GameInProgress(string installDir)
        {
            try
            {
                var log = FindLatestPowerLog(installDir);
                if (log == null || !File.Exists(log)) return false;
                const int tailBytes = 4 << 20;   // a session's log can run to hundreds of MB; one game is a few MB
                using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var n = (int)Math.Min(fs.Length, tailBytes);
                    fs.Seek(-n, SeekOrigin.End);
                    var buf = new byte[n];
                    var read = 0;
                    while (read < n) { var r = fs.Read(buf, read, n - read); if (r <= 0) break; read += r; }
                    return GameInProgress(Encoding.UTF8.GetString(buf, 0, read), fs.Length > n, File.GetLastWriteTimeUtc(log), DateTime.UtcNow);
                }
            }
            catch { return false; }
        }

        /// <summary>The decision for <see cref="GameInProgress(string)"/>. With neither marker in a cut-off tail, a game is still going.</summary>
        public static bool GameInProgress(string logTail, bool truncated, DateTime lastWriteUtc, DateTime nowUtc)
        {
            if (nowUtc - lastWriteUtc > TimeSpan.FromMinutes(10)) return false;
            int created = logTail.LastIndexOf("CREATE_GAME", StringComparison.Ordinal), ended = logTail.LastIndexOf("value=COMPLETE", StringComparison.Ordinal);
            return created > ended || (truncated && created < 0 && ended < 0);
        }

        public static string LogConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Blizzard\Hearthstone\log.config");

        public static string ClientConfigPath(string installDir) =>
            installDir == null ? null : Path.Combine(installDir, "client.config");

        /// <summary>
        /// Hearthstone stops writing a log for the rest of its session once it passes Log.FileSizeLimit.Int KB
        /// (default 10000, about 10 MB: a few constructed games). client.config in the install folder with
        /// FileSizeLimit.Int=-1 under [Log] removes the limit. Read at game start. Returns true if the file changed.
        /// </summary>
        public static bool EnsureClientConfig(string installDir)
        {
            var path = ClientConfigPath(installDir);
            if (path == null || !Directory.Exists(installDir)) return false;
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();

            int section = -1, end = lines.Count;
            for (var i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    if (section >= 0) { end = i; break; }
                    if (t.Substring(1, t.Length - 2).Trim().Equals("Log", StringComparison.OrdinalIgnoreCase)) section = i;
                }
            }

            const string setting = "FileSizeLimit.Int=-1";
            if (section < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[Log]");
                lines.Add(setting);
            }
            else
            {
                var idx = -1;
                for (var i = section + 1; i < end; i++)
                    if (lines[i].Trim().StartsWith("FileSizeLimit.Int", StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                if (idx >= 0)
                {
                    if (lines[idx].Replace(" ", "").Equals(setting, StringComparison.OrdinalIgnoreCase)) return false;
                    lines[idx] = setting;
                }
                else lines.Insert(section + 1, setting);
            }
            File.WriteAllLines(path, lines);
            return true;
        }

        private static readonly (string Section, string[] Lines)[] Required =
        {
            ("Power", new[] { "LogLevel=1", "FilePrinting=true", "ConsolePrinting=false", "ScreenPrinting=false", "Verbose=true" }),
            ("Decks", new[] { "LogLevel=1", "FilePrinting=true", "ConsolePrinting=false", "ScreenPrinting=false" }),
        };

        /// <summary>Adds/overwrites the [Power] and [Decks] sections, keeping any other sections. Returns true if it changed.</summary>
        public static bool EnsureLogConfig()
        {
            var path = LogConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var sections = new List<(string Name, List<string> Lines)>();
            if (File.Exists(path))
            {
                (string Name, List<string> Lines) cur = (null, new List<string>());
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        if (cur.Name != null) sections.Add(cur);
                        cur = (line.Substring(1, line.Length - 2), new List<string>());
                    }
                    else if (line.Length > 0 && cur.Name != null) cur.Lines.Add(line);
                }
                if (cur.Name != null) sections.Add(cur);
            }

            var changed = false;
            foreach (var (name, lines) in Required)
            {
                var idx = sections.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                var want = lines.ToList();
                if (idx < 0) { sections.Add((name, want)); changed = true; }
                else if (!sections[idx].Lines.SequenceEqual(want)) { sections[idx] = (name, want); changed = true; }
            }

            if (changed)
            {
                var sb = new StringBuilder();
                foreach (var s in sections)
                {
                    sb.AppendLine($"[{s.Name}]");
                    foreach (var l in s.Lines) sb.AppendLine(l);
                    sb.AppendLine();
                }
                File.WriteAllText(path, sb.ToString());
            }
            return changed;
        }
    }
}
