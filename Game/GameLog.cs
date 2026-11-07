using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace HearthStoner
{
    /// <summary>
    /// Each game's own part of Power.log, kept (gzip) next to its record in the games folder so it can be replayed
    /// later; Hearthstone deletes its old log folders after a while. Only the GameState lines are kept: they hold
    /// everything the tracker needs (entities, tags, choices, options) and are about half the file.
    /// </summary>
    public static class GameLog
    {
        public sealed class Slice
        {
            public List<string> Lines = new List<string>();
            /// <summary>When the game was created, from the log (date from the log folder's name).</summary>
            public DateTime Start;
            public bool Complete;
            public string SourceLog;
        }

        public static bool Keep(string line) => line.Contains("GameState.");

        /// <summary>The games in a Power.log. <paramref name="sessionStart"/> gives the date for the time-of-day stamps.</summary>
        public static List<Slice> Split(IEnumerable<string> lines, DateTime sessionStart, string source = null)
        {
            var result = new List<Slice>();
            Slice cur = null;
            var day = sessionStart.Date;
            var lastTime = sessionStart.TimeOfDay;
            foreach (var line in lines)
            {
                if (!Keep(line)) continue;
                var t = TimeOf(line);
                if (t.HasValue)
                {
                    if (t.Value < lastTime - TimeSpan.FromHours(12)) day = day.AddDays(1);   // past midnight
                    lastTime = t.Value;
                }
                if (line.Contains("GameState.DebugPrintPower() - CREATE_GAME"))
                {
                    cur = new Slice { Start = day + (t ?? lastTime), SourceLog = source };
                    result.Add(cur);
                }
                if (cur == null) continue;
                cur.Lines.Add(line);
                if (line.Contains("GameState.DebugPrintPower()") && line.Contains("tag=STATE value=COMPLETE")) cur.Complete = true;
            }
            return result;
        }

        /// <summary>The last game in a Power.log file (the one that just ended), or null.</summary>
        public static Slice LastGame(string powerLogPath)
        {
            var slices = Split(ReadShared(powerLogPath), SessionStart(powerLogPath), powerLogPath);
            return slices.LastOrDefault();
        }

        /// <summary>"...\Logs\Hearthstone_2026_10_30_01_18_26\Power.log" -> 2026-10-30 01:18:26 (else the file's creation time).</summary>
        public static DateTime SessionStart(string powerLogPath)
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(powerLogPath) ?? "");
            if (folder.StartsWith("Hearthstone_") &&
                DateTime.TryParseExact(folder.Substring(12), "yyyy_MM_dd_HH_mm_ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
            try { return File.GetCreationTime(powerLogPath); } catch { return DateTime.Now; }
        }

        public static TimeSpan? TimeOf(string line) =>
            line.Length > 18 && line[0] == 'D' && line[1] == ' ' && TimeSpan.TryParse(line.Substring(2, 8), out var t) ? t : (TimeSpan?)null;

        /// <summary>Lines of a file Hearthstone may still be writing.</summary>
        public static IEnumerable<string> ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs))
            {
                string line;
                while ((line = r.ReadLine()) != null) yield return line;
            }
        }

        /// <summary>Writes the slice next to the record and sets <see cref="GameRecord.LogFile"/> (save the record afterwards).</summary>
        public static void Attach(GameRecord rec, Slice slice)
        {
            var name = rec.FileStem + ".log.gz";
            using (var fs = File.Create(Path.Combine(GameRecord.Dir, name)))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            using (var w = new StreamWriter(gz, new UTF8Encoding(false)))
                foreach (var l in slice.Lines) w.WriteLine(l);
            rec.LogFile = name;
        }

        /// <summary>The saved lines of a game, or null if it has none.</summary>
        public static List<string> Load(GameRecord rec)
        {
            if (string.IsNullOrEmpty(rec?.LogFile)) return null;
            var path = Path.Combine(GameRecord.Dir, rec.LogFile);
            if (!File.Exists(path)) return null;
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var r = new StreamReader(gz))
            {
                var lines = new List<string>();
                string l;
                while ((l = r.ReadLine()) != null) lines.Add(l);
                return lines;
            }
        }

        /// <summary>The mode and format a game's lines print ("GT_RANKED", "FT_STANDARD"); nulls when they don't.</summary>
        public static (string GameType, string Format) ModeOf(IEnumerable<string> lines)
        {
            string gt = null, ft = null;
            foreach (var l in lines ?? Enumerable.Empty<string>())
            {
                int i;
                if (gt == null && (i = l.IndexOf("DebugPrintGame() - GameType=", StringComparison.Ordinal)) >= 0) gt = l.Substring(i + 28).Trim();
                else if (ft == null && (i = l.IndexOf("DebugPrintGame() - FormatType=", StringComparison.Ordinal)) >= 0) ft = l.Substring(i + 30).Trim();
                if (gt != null && ft != null) break;
            }
            return (gt, ft);
        }

        // ------------------------------------------------------------------ importing old logs

        public sealed class ImportResult { public int Attached, Created, Skipped, Backfilled; }

        /// <summary>
        /// Goes through Hearthstone's log folders: attaches each finished game's log to its record (matched by start
        /// time, within 3 minutes), and creates records, reviews included, for games played before HearthStoner recorded
        /// them, by replaying them through the tracker. Games that already have their log are left alone.
        /// </summary>
        public static ImportResult Import(string installDir, CardDb cards, MetaStats meta, Action<string> log = null)
        {
            var result = new ImportResult();
            var logsDir = installDir == null ? null : Path.Combine(installDir, "Logs");
            if (logsDir == null || !Directory.Exists(logsDir)) return result;
            var records = GameRecord.LoadAll();
            foreach (var path in Directory.GetDirectories(logsDir, "Hearthstone_*").Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).OrderBy(p => p))
            {
                List<Slice> slices;
                try { slices = Split(ReadShared(path), SessionStart(path), path); }
                catch (Exception ex) { log?.Invoke($"[import] {path}: {ex.Message}"); continue; }
                foreach (var slice in slices.Where(s => s.Complete))
                {
                    var match = records.FirstOrDefault(r => Math.Abs((r.Start - slice.Start).TotalMinutes) <= 3);
                    if (match != null && !string.IsNullOrEmpty(match.LogFile) && File.Exists(Path.Combine(GameRecord.Dir, match.LogFile))) { result.Skipped++; continue; }
                    try
                    {
                        if (match != null)
                        {
                            Attach(match, slice);
                            match.Save();
                            result.Attached++;
                        }
                        else
                        {
                            var rec = Record(slice, DecksLog.LastQueuedDeck(path, slice.Start), cards, meta);
                            if (rec == null || !rec.Decided) { result.Skipped++; continue; }
                            Attach(rec, slice);
                            rec.Save();
                            records.Add(rec);
                            result.Created++;
                        }
                    }
                    catch (Exception ex) { log?.Invoke($"[import] game at {slice.Start:g}: {ex.Message}"); }
                }
            }
            // Games recorded before HearthStoner saved its per-turn deck guesses: fill them in from their logs.
            foreach (var rec in GameRecord.LoadAll().Where(r => !string.IsNullOrEmpty(r.LogFile) && r.Snapshots.Count > 0 && r.Snapshots.All(t => t.OpponentGuess == null)))
            {
                try { if (PredictionScore.Backfill(rec, Load(rec), cards, meta)) { rec.Save(); result.Backfilled++; } }
                catch (Exception ex) { log?.Invoke($"[import] guesses for {rec.Start:g}: {ex.Message}"); }
            }
            return result;
        }

        /// <summary>Replays a game through the tracker, advisor and recorder to make its record (times from the log).</summary>
        public static GameRecord Record(Slice slice, QueuedDeck deck, CardDb cards, MetaStats meta)
        {
            var list = deck == null ? null : DeckCode.Decode(deck.DeckCode, cards);
            GameTracker tracker = null;
            GameRecorder recorder = null;
            GameRecord done = null;
            var day = slice.Start.Date;
            tracker = new GameTracker(cards, ev =>
            {
                if (ev.Kind == GameEventKind.GameStart && list != null) { tracker.FriendlyDeckList = list.ToList(); tracker.FriendlyDeckName = deck.Name; }
                recorder?.OnEvent(ev);
                if (ev.Kind == GameEventKind.GameEnd) done = recorder?.Current;
            });
            var advisor = new Advisor(tracker, cards, meta, new Settings());
            recorder = new GameRecorder(tracker, advisor, cards);
            recorder.Clock = () =>
            {
                var t = day + tracker.LastLineTime;
                return t < slice.Start.AddHours(-1) ? t.AddDays(1) : t;
            };
            tracker.Changed += recorder.OnChanged;
            foreach (var l in slice.Lines) tracker.Feed(l);
            var rec = done ?? recorder.Current;
            if (rec != null) rec.Imported = true;
            return rec;
        }
    }
}
