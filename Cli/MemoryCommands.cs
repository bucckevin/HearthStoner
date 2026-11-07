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

        private static int Collection(string csvOut, string jsonOut)
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var cards = CardDb.Load();
                if (!WaitFor(() => mem.IsCollectionReady(), "your collection to load (go to the main menu)")) return 1;

                var raw = Retry(() => mem.GetCollection(), r => r != null && r.Count > 0, "collection");
                if (raw == null) return 1;
                OwnedCollection.FromMemory(raw).Save();   // for crafting advice when the game isn't running

                var rows = new List<CollectionRow>();
                foreach (var c in raw)
                {
                    var info = cards.ById(c.CardId);
                    void Add(string finish, int count)
                    {
                        if (count <= 0) return;
                        rows.Add(new CollectionRow
                        {
                            CardId = c.CardId,
                            Name = info?.Name ?? c.CardId,
                            Class = info?.CardClass ?? "",
                            Set = info?.Set ?? "",
                            Rarity = info?.Rarity ?? "",
                            Cost = info?.Cost,
                            Finish = finish,
                            Count = count,
                        });
                    }
                    Add("normal", c.Count);
                    Add("golden", c.PremiumCount);
                    Add("diamond", c.DiamondCount);
                    Add("signature", c.SignatureCount);
                }

                rows = rows.OrderBy(r => ClassOrder(r.Class)).ThenBy(r => r.Cost ?? 99).ThenBy(r => r.Name).ThenBy(r => r.Finish).ToList();

                foreach (var cls in rows.GroupBy(r => r.Class))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n== {Pretty(cls.Key)} ({cls.Sum(r => r.Count)} cards) ==");
                    Console.ResetColor();
                    foreach (var r in cls)
                        Console.WriteLine($"  {r.Count}x  [{(r.Cost?.ToString() ?? "-"),2}] {r.Name}{(r.Finish == "normal" ? "" : $" ({r.Finish})")}  - {Pretty(r.Rarity)}, {r.Set}");
                }

                Console.WriteLine($"\nTotal: {rows.Sum(r => r.Count)} cards, {rows.Select(r => r.CardId).Distinct().Count()} unique.");

                if (csvOut != null)
                {
                    var sb = new StringBuilder("card_id,name,class,set,rarity,cost,finish,count\n");
                    foreach (var r in rows)
                        sb.AppendLine(string.Join(",", Csv(r.CardId), Csv(r.Name), r.Class, r.Set, r.Rarity, r.Cost?.ToString() ?? "", r.Finish, r.Count));
                    File.WriteAllText(csvOut, sb.ToString());
                    Console.WriteLine($"Wrote {csvOut}");
                }
                if (jsonOut != null)
                {
                    File.WriteAllText(jsonOut, JsonConvert.SerializeObject(rows, Formatting.Indented));
                    Console.WriteLine($"Wrote {jsonOut}");
                }
            }
            return 0;
        }


        private static int Decks(string jsonOut)
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var cards = CardDb.Load();
                if (!WaitFor(() => mem.IsCollectionReady(), "your collection to load (go to the main menu)")) return 1;

                var decks = Retry(() => mem.GetAllDecks(), d => d != null && d.Count > 0, "decks");
                if (decks == null) return 1;

                var export = new List<object>();
                foreach (var deck in decks.OrderBy(d => d.Name))
                {
                    var hero = cards.ById(deck.HeroCardId);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n## {deck.Name}  ({Pretty(hero?.CardClass)}, {FormatName(deck.FormatType)}, {deck.DeckList.Count} cards)");
                    Console.ResetColor();

                    foreach (var g in deck.DeckList.GroupBy(id => id).Select(g => new { Info = cards.ById(g.Key), Id = g.Key, Count = g.Count() })
                                 .OrderBy(c => c.Info?.Cost ?? 99).ThenBy(c => c.Info?.Name))
                        Console.WriteLine($"  {g.Count}x [{(g.Info?.Cost?.ToString() ?? "-"),2}] {g.Info?.Name ?? g.Id}");

                    var code = DeckCode.Encode(deck, cards);
                    if (code != null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"  Deck code: {code}");
                        Console.ResetColor();
                    }
                    export.Add(new { deck.DeckId, deck.Name, Class = hero?.CardClass, Format = FormatName(deck.FormatType), deck.HeroCardId, Cards = deck.DeckList, DeckCode = code });
                }

                if (jsonOut != null)
                {
                    File.WriteAllText(jsonOut, JsonConvert.SerializeObject(export, Formatting.Indented));
                    Console.WriteLine($"\nWrote {jsonOut}");
                }
            }
            return 0;
        }


        private static int Status()
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var acct = Safe(() => mem.GetAccountInfo());
                Console.WriteLine($"Account:  {acct?.BattleTag ?? "(not available yet)"}");
                Console.WriteLine($"Screen:   {Safe(() => mem.GetSceneMode())?.ToString() ?? "?"}");
                Console.WriteLine($"Collection loaded: {(Safe(() => mem.IsCollectionReady()) ? "yes" : "no")}");

                var match = Safe(() => mem.GetMatchInfo());
                if (match?.OpposingPlayer != null && !string.IsNullOrEmpty(match.OpposingPlayer.Name))
                {
                    Console.WriteLine($"In game:  {match.GameType} ({match.FormatType})");
                    Console.WriteLine($"  You:      {Ranks.Describe(match.LocalPlayer, match.FormatType)}");
                    Console.WriteLine($"  Opponent: {Ranks.Describe(match.OpposingPlayer, match.FormatType)}");
                }
                else Console.WriteLine("In game:  no");
            }
            return 0;
        }


        private static int Find(string text)
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var hits = mem.FindTypes(text).ToList();
                foreach (var h in hits.Take(200)) Console.WriteLine("  " + h);
                Console.WriteLine($"{hits.Count} match(es){(hits.Count > 200 ? " (showing 200)" : "")}.");
            }
            return 0;
        }


        private static int Explore(string path)
        {
            using (var mem = HsMemory.Attach(_verbose))
                foreach (var line in mem.Explore(path)) Console.WriteLine(line);
            return 0;
        }


        private static int QuestsCommand()
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var log = QuestReader.Read(mem, new Dictionary<int, QuestEntry>(), _verbose ? Console.WriteLine : null);
                if (log == null) { Console.WriteLine("No quests found (open the Quest log in game once, then try again)."); return 0; }
                log.Save();
                foreach (var q in log.Quests.OrderBy(q => q.Pool))
                    Console.WriteLine($"{q.Pool,-7} {q.Text}  {q.Progress}/{q.Quota}  {q.Xp} XP  (quest {q.Id}, status {q.Status}, icon {q.Icon})");
                if (log.Track is TrackState t) Console.WriteLine($"Reward track: level {t.Level}, {t.Xp}/{t.XpNeeded} XP");
                if (_verbose) foreach (var q in log.Quests) foreach (var line in mem.DbfRecord("Quest", q.Id)) Console.WriteLine("    " + line);
            }
            return 0;
        }


        private static bool WaitFor(Func<bool> ready, string what)
        {
            for (var i = 0; i < 30; i++)
            {
                if (Safe(ready)) return true;
                if (i == 0) Console.WriteLine($"Waiting for {what}...");
                Thread.Sleep(1000);
            }
            Console.Error.WriteLine($"Timed out waiting for {what}.");
            return false;
        }


        private static T Retry<T>(Func<T> f, Func<T, bool> ok, string what) where T : class
        {
            Exception last = null;
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    var r = f();
                    if (ok(r)) return r;
                }
                catch (Exception ex) { last = ex; }
                Thread.Sleep(1000);
            }
            Console.Error.WriteLine($"Couldn't read {what} from memory{(last != null ? $" ({last.Message})" : "")}. " +
                                    "If Hearthstone was just patched, field names may have changed - try 'HearthStoner explore CollectionManager.s_instance'.");
            return null;
        }


        private static T Safe<T>(Func<T> f)
        {
            try { return f(); }
            catch (Exception ex) { if (_verbose) Console.Error.WriteLine("[memory] " + ex.Message); return default; }
        }


        private static string FormatName(int formatType) => formatType switch
        {
            1 => "Wild",
            2 => "Standard",
            3 => "Classic",
            4 => "Twist",
            _ => "Format " + formatType,
        };


        private static readonly string[] Classes =
            { "DEATHKNIGHT", "DEMONHUNTER", "DRUID", "HUNTER", "MAGE", "PALADIN", "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR", "NEUTRAL" };

        private static int ClassOrder(string c) { var i = Array.IndexOf(Classes, c); return i < 0 ? 99 : i; }


        private static string Pretty(string s) =>
            string.IsNullOrEmpty(s) ? "Neutral" : char.ToUpper(s[0]) + s.Substring(1).ToLower().Replace('_', ' ');


        private static string Csv(string s) => s != null && (s.Contains(",") || s.Contains("\"")) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;


        private sealed class CollectionRow
        {
            public string CardId { get; set; }
            public string Name { get; set; }
            public string Class { get; set; }
            public string Set { get; set; }
            public string Rarity { get; set; }
            public int? Cost { get; set; }
            public string Finish { get; set; }
            public int Count { get; set; }
        }
    }
}
