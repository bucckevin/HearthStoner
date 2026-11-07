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

        private static int StatsCommand(string rank, string period)
        {
            var settings = Settings.Load();
            var cards = CardDb.Load();
            var meta = LoadMeta(rank ?? Ranks.MetaBracket(settings), period ?? settings.MetaPeriod);
            var games = GameRecord.LoadAll();
            Console.WriteLine($"{games.Count} saved game(s) in {GameRecord.Dir}");
            var sections = StatsReport.Build(games, meta, cards, (key, id) => meta.Detail(id, Log));
            PrintSnapshot(new AdviceSnapshot { Sections = sections });
            return 0;
        }


        private static int PuzzlesCommand()
        {
            var puzzles = GameRecord.LoadAll().SelectMany(g => g.Snapshots.Where(t => t.Puzzle != null).Select(t => (Game: g, Turn: t))).ToList();
            if (puzzles.Count == 0) { Console.WriteLine("No lethal puzzles yet: they're saved whenever HearthStoner sees you have lethal."); return 0; }
            Console.WriteLine($"{puzzles.Count} puzzle(s). Press Enter to reveal each solution.\n");
            var n = 0;
            foreach (var (g, t) in puzzles)
            {
                var p = t.Puzzle;
                n++;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Puzzle {n}: {g.Start:MMM d} {MetaStats.ClassName(g.MyClass)} vs {g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass)}, turn {(t.Turn + 1) / 2}" +
                                  (g.Review.Any(r => r.Kind == "MissedLethal" && r.Turn == t.Turn) ? "  (you missed this one)" : ""));
                Console.ResetColor();
                Console.WriteLine($"  Opponent: {p.TheirHealth} HP{(p.TheirArmor > 0 ? $" + {p.TheirArmor} armor" : "")}");
                Console.WriteLine($"  Their board: {(p.TheirMinions.Count > 0 ? string.Join(", ", p.TheirMinions) : "empty")}");
                Console.WriteLine($"  Your board: {(p.YourMinions.Count > 0 ? string.Join(", ", p.YourMinions) : "empty")}{(p.Weapon != null ? $"; weapon {p.Weapon}" : "")}");
                Console.WriteLine($"  Your hand ({p.Mana} mana): {string.Join(", ", p.Hand)}");
                Console.Write("  Find lethal, then press Enter... ");
                Console.ReadLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  {p.Solution}\n");
                Console.ResetColor();
            }
            return 0;
        }


        private static int QuizCommand()
        {
            var cards = CardDb.Load();
            var hands = GameRecord.LoadAll().Where(g => g.Mulligan.Count(m => m.Advice != null) >= 2).ToList();
            if (hands.Count == 0) { Console.WriteLine("No opening hands with data yet. Play a few games with HearthStoner running."); return 0; }
            var rng = new Random();
            int right = 0, total = 0;
            Console.WriteLine("Mulligan quiz: type k (keep) or t (toss) for each card; q quits.\n");
            foreach (var g in hands.OrderBy(_ => rng.Next()).Take(10))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{g.MyArchetype ?? MetaStats.ClassName(g.MyClass)} vs {MetaStats.ClassName(g.OpponentClass)}, {(g.WentFirst ? "going first" : "on the coin")}");
                Console.ResetColor();
                Console.WriteLine("  Hand: " + string.Join(", ", g.Mulligan.Select(m => cards.NameOf(m.CardId))));
                foreach (var m in g.Mulligan.Where(m => m.Advice != null && m.Advice != "either"))
                {
                    Console.Write($"  {cards.NameOf(m.CardId)}? ");
                    var a = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    if (a == "q") { Console.WriteLine($"\nScore: {right}/{total}"); return 0; }
                    var keep = a.StartsWith("k");
                    var correct = keep == (m.Advice == "KEEP");
                    total++; if (correct) right++;
                    Console.ForegroundColor = correct ? ConsoleColor.Green : ConsoleColor.Red;
                    Console.WriteLine($"    {(correct ? "Right" : "Data says " + m.Advice.ToLower())}: {(m.Delta >= 0 ? "+" : "")}{m.Delta * 100:F1}% win rate when kept. You {(m.Kept ? "kept" : "tossed")} it in that game.");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
            Console.WriteLine($"Score: {right}/{total}{(total > 0 ? $" ({(double)right / total:P0})" : "")}");
            return 0;
        }


        private static int ReviewCommand(int count)
        {
            var games = GameRecord.LoadAll();
            if (games.Count == 0) { Console.WriteLine("No saved games yet."); return 0; }
            var cards = CardDb.Load();
            foreach (var g in games.Skip(Math.Max(0, games.Count - count)))
            {
                PrintSnapshot(new AdviceSnapshot { Sections = { Advisor.ReviewSection(g) } });
                if (g.Mulligan.Count > 0)
                    Console.WriteLine("    Mulligan: " + string.Join(", ", g.Mulligan.Select(m => $"{cards.NameOf(m.CardId)} {(m.Kept ? "kept" : "tossed")}{(m.Advice != null ? $" (data: {m.Advice.ToLower()})" : "")}")));
                foreach (var t in g.Snapshots.Where(t => t.Yours))
                    Console.WriteLine($"    T{(t.Turn + 1) / 2}: you {t.MyHp}+{t.MyArmor} hp, board {t.MyBoardAttack}/{t.MyBoardHealth} vs {t.TheirBoardAttack}/{t.TheirBoardHealth}, them {t.TheirHp}+{t.TheirArmor}; mana {t.ManaAvailable - t.ManaUnspent}/{t.ManaAvailable}" +
                                      (t.Played.Count > 0 ? $"; played {string.Join(", ", t.Played.Select(cards.NameOf))}" : ""));
                Console.WriteLine();
            }
            return 0;
        }


        /// <summary>Prints the current meta (archetypes by popularity) to check the stats download.</summary>
        private static int MetaCommand(string heroClass, string rank, string period)
        {
            var meta = LoadMeta(rank, period);
            if (meta.Archetypes.Count == 0) { Console.Error.WriteLine("No meta data available."); return 1; }
            Console.WriteLine($"Standard meta ({meta.Rank}, {meta.Period}), updated {meta.Updated:u}, {meta.TotalGames:N0} games\n");
            var list = heroClass != null ? meta.ForClass(heroClass).ToList() : meta.Archetypes.Take(40).ToList();
            foreach (var a in list)
                Console.WriteLine($"  {a.Share,6:P1}  {a.WinRate,6:P1}  {a.Games,8:N0}  {MetaStats.ClassName(a.Class),-13} {a.Name}");
            return 0;
        }
    }
}
