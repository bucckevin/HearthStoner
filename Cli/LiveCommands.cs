using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HackF5.UnitySpy.HearthstoneLib;
using HackF5.UnitySpy.HearthstoneLib.Detail.Deck;
using Newtonsoft.Json;

namespace HearthStoner
{
    internal static partial class Program
    {

        private static int GameArtCommand(string hsPath, string outDir)
        {
            var hs = HearthstoneLocator.FindInstallDir(hsPath);
            outDir ??= AppPaths.Data("gameart");
            Console.WriteLine($"Game {GameArtBuilder.GameVersion(hs)} in {hs} -> {outDir}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string last = null;
            GameArtBuilder.Log = s => { Console.WriteLine("\r" + s.PadRight(40)); last = null; };
            GameArtBuilder.Run(hs, outDir, (stage, done, total) =>
            {
                var line = $"{stage} {done}/{total}";
                if (line != last) { Console.Write("\r" + line.PadRight(40)); last = line; }
                if (done == total) Console.WriteLine();
            }, CancellationToken.None);
            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:0}s.");
            return 0;
        }


        private static int Setup(string hsPath)
        {
            var changed = HearthstoneLocator.EnsureLogConfig();
            Console.WriteLine(changed
                ? $"Updated {HearthstoneLocator.LogConfigPath}. Restart Hearthstone for logging to take effect."
                : $"Logging already enabled in {HearthstoneLocator.LogConfigPath}.");

            var install = HearthstoneLocator.FindInstallDir(hsPath);
            Console.WriteLine(install != null
                ? $"Hearthstone install: {install}"
                : "Couldn't find the Hearthstone install folder. Pass --hs \"C:\\path\\to\\Hearthstone\" to 'watch'.");
            if (install != null)
                Console.WriteLine(SafeEnsureClientConfig(install)
                    ? $"Removed the 10 MB log limit in {HearthstoneLocator.ClientConfigPath(install)}."
                    : $"Log size limit already off ({HearthstoneLocator.ClientConfigPath(install)}).");
            Console.WriteLine($"Hearthstone running: {(HsMemory.IsHearthstoneRunning ? "yes" : "no")}");
            WarnIfRestartNeeded(install);
            return 0;
        }


        /// <summary>
        /// Hearthstone only reads log.config and client.config at startup. If either is newer than the running game,
        /// the logs won't be complete until Hearthstone is restarted.
        /// </summary>
        private static bool WarnIfRestartNeeded(string install)
        {
            try
            {
                var proc = System.Diagnostics.Process.GetProcessesByName("Hearthstone").FirstOrDefault();
                if (proc == null) return false;
                var started = proc.StartTime;
                var newer = new[] { HearthstoneLocator.LogConfigPath, HearthstoneLocator.ClientConfigPath(install) }
                    .Where(f => f != null && File.Exists(f) && File.GetLastWriteTime(f) > started)
                    .ToList();
                if (newer.Count == 0) return false;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Hearthstone was started at {started:HH:mm}, before {string.Join(" and ", newer.Select(Path.GetFileName))} changed.");
                Console.WriteLine("Restart Hearthstone, or tracking will be missing or stop early this session.");
                Console.ResetColor();
                return true;
            }
            catch { return false; }   // e.g. access denied reading another process's start time
        }


        /// <summary>Feeds a saved Power.log through the tracker and prints the advice at every turn: for testing.</summary>
        private static int Replay(string path, string deckCode, string me, string rank, string period)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("No such log: " + path);
            var cards = CardDb.Load();
            var meta = LoadMeta(rank, period);
            List<string> deck = null;
            if (!string.IsNullOrEmpty(deckCode))
            {
                deck = DeckCode.Decode(deckCode, cards);
                if (deck == null) throw new ArgumentException("Couldn't decode that deck code.");
            }
            else
            {
                var queued = DecksLog.LastQueuedDeck(path);
                if (queued != null) deck = DeckCode.Decode(queued.DeckCode, cards);
                if (deck != null) Console.WriteLine($"Deck from Decks.log: {queued.Name} ({deck.Count} cards)");
            }

            GameTracker tracker = null;
            Advisor advisor = null;
            var games = 0;
            var detailPrepared = false;
            GameRecorder recorder = null;
            tracker = new GameTracker(cards, ev =>
            {
                Console.ForegroundColor = ev.Kind == GameEventKind.Secret ? ConsoleColor.Magenta : ev.Kind == GameEventKind.Turn ? ConsoleColor.DarkGray : ConsoleColor.Gray;
                Console.WriteLine("  " + ev.Text);
                Console.ResetColor();
                recorder?.OnEvent(ev);
                if (ev.Kind == GameEventKind.GameStart)
                {
                    games++;
                    if (deck != null) tracker.FriendlyDeckList = deck.ToList();
                }
                // The previous turn is complete when the next one starts: show the advice as it stood then.
                if (ev.Kind == GameEventKind.Turn || ev.Kind == GameEventKind.GameEnd)
                {
                    if (!detailPrepared && deck != null && tracker.FriendlyPlayerId != 0) { advisor.PrepareFriendlyDeck(Log); detailPrepared = true; }
                    PrintSnapshot(advisor.Compute());
                }
            });
            tracker.FriendlyNameHint = me;
            advisor = new Advisor(tracker, cards, meta);
            recorder = new GameRecorder(tracker, advisor, cards);
            tracker.Changed += recorder.OnChanged;
            var reviews = new List<GameRecord>();

            foreach (var line in File.ReadLines(path))
            {
                lock (tracker.Sync)
                {
                    var before = recorder.Current;
                    tracker.Feed(line);
                    if (before != null && before.End != default(DateTime) && !reviews.Contains(before)) reviews.Add(before);
                }
            }
            foreach (var r in reviews) PrintSnapshot(new AdviceSnapshot { Sections = { Advisor.ReviewSection(r) } });
            PrintSnapshot(advisor.Compute());
            Console.WriteLine($"{games} game(s) in the log.");
            return 0;
        }


        private static bool SafeEnsureClientConfig(string install)
        {
            try { return HearthstoneLocator.EnsureClientConfig(install); }
            catch (Exception ex)
            {
                Console.WriteLine($"Couldn't update {HearthstoneLocator.ClientConfigPath(install)} ({ex.Message}). Hearthstone will stop logging after ~10 MB per session.");
                return false;
            }
        }


        private static void PrintSummary(GameTracker t, CardDb cards)
        {
            string Fmt(IEnumerable<string> ids) => string.Join(", ",
                ids.Where(i => !string.IsNullOrEmpty(i)).GroupBy(i => i)
                   .Select(g => g.Count() > 1 ? $"{cards.NameOf(g.Key)} x{g.Count()}" : cards.NameOf(g.Key)));

            Console.WriteLine();
            Console.WriteLine($"  You played:      {Fmt(t.FriendlyPlayed)}");
            Console.WriteLine($"  Opponent played: {Fmt(t.OpponentPlayed)}");
        }
    }
}
