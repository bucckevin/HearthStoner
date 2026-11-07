using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HearthStoner.Game
{
    /// <summary>The deck you queued with, as Hearthstone logs it.</summary>
    public sealed class QueuedDeck
    {
        public string Name;
        public long DeckId;
        public string DeckCode;
        public DateTime LoggedAt;
    }

    /// <summary>
    /// Reads Decks.log (enabled by the [Decks] section of log.config). Whenever you queue, Hearthstone writes:
    ///   Finding Game With Deck:
    ///   ### Aura Paladin
    ///   # Deck ID: 3379985521
    ///   AAECAZ8F...
    /// The last such block in the current session's Decks.log is the deck you're playing.
    /// </summary>
    public static class DecksLog
    {
        private static readonly Regex Prefix = new Regex(@"^[A-Z] \d{2}:\d{2}:\d{2}\.\d+ ");

        /// <summary>
        /// Your saved decks as last listed in any session's Decks.log ("Deck Contents Received:" blocks, written when
        /// you open the game or My Collection). Newest session first, last block in it wins.
        /// </summary>
        public static List<QueuedDeck> SavedDecks(string installDir)
        {
            var logs = installDir == null ? null : Path.Combine(installDir, "Logs");
            if (logs == null || !Directory.Exists(logs)) return new List<QueuedDeck>();
            var files = Directory.GetDirectories(logs, "Hearthstone_*")
                .Select(d => Path.Combine(d, "Decks.log"))
                .Concat(new[] { Path.Combine(logs, "Decks.log") })
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc);
            foreach (var f in files)
            {
                var lines = ReadLines(f);
                if (lines == null) continue;
                var start = Array.FindLastIndex(lines, l => l.StartsWith("Deck Contents Received:"));
                if (start < 0) continue;
                var result = new List<QueuedDeck>();
                QueuedDeck cur = null;
                for (var i = start + 1; i < lines.Length; i++)
                {
                    var l = lines[i].Trim();
                    if (l.StartsWith("Deck Contents Received:") || l.StartsWith("Finding Game With Deck:")) break;
                    if (l.StartsWith("### ")) { cur = new QueuedDeck { Name = l.Substring(4).Trim(), LoggedAt = File.GetLastWriteTime(f) }; }
                    else if (cur != null && l.StartsWith("# Deck ID:") && long.TryParse(l.Substring(10).Trim(), out var id)) cur.DeckId = id;
                    else if (cur != null && l.StartsWith("AAE")) { cur.DeckCode = l; result.Add(cur); cur = null; }
                }
                if (result.Count > 0) return result;
            }
            return new List<QueuedDeck>();
        }

        private static string[] ReadLines(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs))
                    return r.ReadToEnd().Split('\n').Select(l => Prefix.Replace(l.TrimEnd('\r'), "")).ToArray();
            }
            catch (IOException) { return null; }
        }

        /// <summary>
        /// Decks.log next to the given Power.log (same session folder): the last deck queued, or with
        /// <paramref name="before"/> the last one queued before that time (a session can have games with different decks).
        /// </summary>
        public static QueuedDeck LastQueuedDeck(string powerLogPath, DateTime? before = null)
        {
            if (string.IsNullOrEmpty(powerLogPath)) return null;
            var path = Path.Combine(Path.GetDirectoryName(powerLogPath) ?? "", "Decks.log");
            if (!File.Exists(path)) return null;

            string[] raw;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs))
                    raw = r.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            }
            catch (IOException) { return null; }
            var lines = raw.Select(l => Prefix.Replace(l, "")).ToArray();

            // When each line was logged: time of day from its stamp, date from the session folder.
            var when = new DateTime?[raw.Length];
            if (before.HasValue)
            {
                var start = GameLog.SessionStart(powerLogPath);
                var day = start.Date;
                var last = start.TimeOfDay;
                for (var i = 0; i < raw.Length; i++)
                {
                    if (!Prefix.IsMatch(raw[i]) || !TimeSpan.TryParse(raw[i].Substring(2, 8), out var t)) continue;
                    if (t < last - TimeSpan.FromHours(12)) day = day.AddDays(1);   // past midnight
                    last = t;
                    when[i] = day + t;
                }
            }

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (!lines[i].StartsWith("Finding Game With Deck:")) continue;
                if (before.HasValue && when[i] > before.Value) continue;
                var deck = new QueuedDeck { LoggedAt = File.GetLastWriteTime(path) };
                for (var j = i + 1; j < lines.Length && j <= i + 4; j++)
                {
                    var l = lines[j].Trim();
                    if (l.StartsWith("### ")) deck.Name = l.Substring(4).Trim();
                    else if (l.StartsWith("# Deck ID:") && long.TryParse(l.Substring(10).Trim(), out var id)) deck.DeckId = id;
                    else if (l.StartsWith("AAE")) { deck.DeckCode = l; break; }
                }
                return deck.DeckCode != null ? deck : null;
            }
            return null;
        }
    }
}
