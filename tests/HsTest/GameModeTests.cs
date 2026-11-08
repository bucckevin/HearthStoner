using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>Game modes: what each mode's record keeps, from the lines Hearthstone writes for it.</summary>
    public class GameModeTests
    {
        [Fact]
        public void Arena_games_take_the_arena_deck_from_Decks_log()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hs-modes-" + Guid.NewGuid().ToString("N"), "Hearthstone_2026_11_07_22_26_01");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllLines(Path.Combine(dir, "Decks.log"), new[]
                {
                    "I 22:35:54.8 Finding Game With Deck:", "I 22:35:54.8 ### Aura Paladin", "I 22:35:54.8 # Deck ID: 1", "I 22:35:54.8 AAEaura",
                    "I 23:23:06.8 Starting Arena Game With Deck:", "I 23:23:06.8 ### ", "I 23:23:06.8 # Deck ID: 2", "I 23:23:06.8 AAEarena",
                });
                var log = Path.Combine(dir, "Power.log");
                var arena = DecksLog.LastQueuedDeck(log, new DateTime(2026, 11, 7, 23, 23, 15));
                Assert.Equal("AAEarena", arena.DeckCode);
                Assert.Null(arena.Name);   // Arena decks have no name
                Assert.Equal("Aura Paladin", DecksLog.LastQueuedDeck(log, new DateTime(2026, 11, 7, 22, 36, 14)).Name);
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(dir), true); } catch { } }
        }

        /// <summary>
        /// Nov 8: Casual at 00:28:52 with the deck queued at 00:28:17, then a solo game at 00:32:49 that logs no deck.
        /// The solo game must not get the Casual game's deck.
        /// </summary>
        [Fact]
        public void A_queued_deck_belongs_to_the_first_game_after_it()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hs-modes-" + Guid.NewGuid().ToString("N"), "Hearthstone_2026_11_07_22_26_01");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllLines(Path.Combine(dir, "Decks.log"), new[]
                {
                    "I 00:05:52.0 Finding Game With Deck:", "I 00:05:52.0 ### Aura Paladin", "I 00:05:52.0 # Deck ID: 1", "I 00:05:52.0 AAEaura",
                    "I 00:28:17.2 Finding Game With Deck:", "I 00:28:17.2 ### Aura Paladin", "I 00:28:17.2 # Deck ID: 1", "I 00:28:17.2 AAEaura2",
                });
                var log = Path.Combine(dir, "Power.log");
                DateTime T(int h, int m, int s) => new DateTime(2026, 11, 8, h, m, s);
                Assert.Equal("AAEaura2", DecksLog.LastQueuedDeck(log, T(0, 28, 52), after: T(0, 6, 15)).DeckCode);   // the Casual game
                Assert.Null(DecksLog.LastQueuedDeck(log, T(0, 32, 49), after: T(0, 28, 52)));                    // the solo game after it
                Assert.Equal("AAEaura2", DecksLog.LastQueuedDeck(log, T(0, 32, 49)).DeckCode);                  // without the rule: the stale one
                // The live loader's times of day, placed in the session (it started Nov 7, 22:26).
                Assert.Equal(T(0, 28, 52), DecksLog.At(log, new TimeSpan(0, 28, 52)));
                Assert.Equal(new DateTime(2026, 11, 7, 23, 23, 15), DecksLog.At(log, new TimeSpan(23, 23, 15)));
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(dir), true); } catch { } }
        }

        [Theory]
        [InlineData("Finding Game With Deck:", true)]
        [InlineData("Starting Arena Game With Deck:", true)]
        [InlineData("Finished Editing Deck:", false)]
        [InlineData("Deck Contents Received:", false)]
        public void Queue_lines(string line, bool queue) => Assert.Equal(queue, DecksLog.IsQueueLine(line));

        [Fact]
        public void Real_classes()
        {
            Assert.True(HeroClasses.IsReal("DEATHKNIGHT"));
            Assert.False(HeroClasses.IsReal("NEUTRAL"));
            Assert.False(HeroClasses.IsReal(null));
            Assert.True(HeroClasses.Same("PALADIN", "PALADIN"));
            Assert.False(HeroClasses.Same("PALADIN", "DEMONHUNTER"));
            Assert.Null(HeroClasses.Same("PALADIN", "NEUTRAL"));
        }

        [Fact]
        public void Ladder_ranks_belong_to_ranked_and_casual()
        {
            Assert.True(GameModes.HasLadderRank("GT_RANKED"));
            Assert.True(GameModes.HasLadderRank("GT_CASUAL"));
            Assert.False(GameModes.HasLadderRank("GT_ARENA"));
            Assert.False(GameModes.HasLadderRank("GT_BATTLEGROUNDS"));
            Assert.False(GameModes.HasLadderRank("GT_TAVERNBRAWL"));
            var r = new GameRecord { Rank = "Bronze 10", StarLevel = 1, Stars = 2, StarBonus = 2, OpponentStarLevel = 3 };
            GameModes.ClearLadderInfo(r);
            Assert.True(r.Rank == null && r.StarLevel == 0 && r.Stars == 0 && r.StarBonus == 0 && r.OpponentStarLevel == 0);
        }

        [Theory]
        [InlineData(1, "1st")]
        [InlineData(2, "2nd")]
        [InlineData(3, "3rd")]
        [InlineData(5, "5th")]
        [InlineData(11, "11th")]
        public void Places(int place, string text) => Assert.Equal(text, GameModes.Ordinal(place));

        [Fact]
        public void Battlegrounds_summary_uses_places()
        {
            var day = new DateTime(2026, 11, 7, 23, 0, 0);
            var one = new List<GameRecord> { new GameRecord { Start = day, Result = "LOST", Place = 5 } };
            Assert.Equal("Battlegrounds: 1 game, finished 5th, last Nov 7", GameModes.Summary("Battlegrounds", one));
            var three = new List<GameRecord>
            {
                new GameRecord { Start = day, Place = 5 }, new GameRecord { Start = day.AddDays(-1), Place = 2 }, new GameRecord { Start = day.AddDays(-2), Place = 3 },
            };
            Assert.Equal("Battlegrounds: 3 games, average place 3.3, top 4 in 2, last Nov 7", GameModes.Summary("Battlegrounds", three));
            var mercs = new List<GameRecord> { new GameRecord { Start = day, Result = "WON" }, new GameRecord { Start = day, Result = "LOST" } };
            Assert.Equal("Mercenaries: 2 games, 1 win, last Nov 7", GameModes.Summary("Mercenaries", mercs));
        }

        [Fact]
        public void Old_records_needing_repair()
        {
            var bg = new GameRecord { Version = "1", GameType = "GT_BATTLEGROUNDS", Result = "LOST" };
            Assert.True(GameLog.NeedsRepair(bg, TestData.Cards));
            var ranked = new GameRecord { Version = "1", GameType = "GT_RANKED", Result = "WON", MyClass = "PALADIN", OpponentClass = "" };
            Assert.True(GameLog.NeedsRepair(ranked, TestData.Cards));
            var arenaRank = new GameRecord { Version = "1", GameType = "GT_ARENA", Result = "WON", MyClass = "DEMONHUNTER", OpponentClass = "ROGUE", Rank = "Bronze 10" };
            Assert.True(GameLog.NeedsRepair(arenaRank, TestData.Cards));
            var solo = new GameRecord { Version = "1", GameType = "GT_VS_AI", Result = "WON", MyClass = "MAGE", OpponentClass = "NEUTRAL" };
            Assert.False(GameLog.NeedsRepair(solo, TestData.Cards));   // adventure bosses really are classless
            var fine = new GameRecord { Version = "1", GameType = "GT_RANKED", Result = "WON", MyClass = "PALADIN", OpponentClass = "MAGE" };
            Assert.False(GameLog.NeedsRepair(fine, TestData.Cards));
            Assert.False(GameLog.NeedsRepair(new GameRecord { GameType = "GT_BATTLEGROUNDS" }, TestData.Cards));   // new records are version 2
        }

        /// <summary>Every game in your Hearthstone logs, replayed: each mode's record keeps what that mode has.</summary>
        [Trait("Category", TestData.MyGames)]
        [Fact]
        public void Each_mode_records_what_it_has()
        {
            int arena = 0, bg = 0, card = 0;
            var logs = Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*")
                .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists);
            foreach (var path in logs)
            {
                var slices = GameLog.Split(GameLog.ReadShared(path), GameLog.SessionStart(path), path);
                foreach (var slice in slices.Where(s => s.Complete))
                {
                    // The deck the way Import picks it: queued after the previous game started.
                    var previous = slices.Where(s => s.Start < slice.Start).Select(s => (DateTime?)s.Start).LastOrDefault();
                    var rec = GameLog.Record(slice, DecksLog.LastQueuedDeck(path, slice.Start, previous), TestData.Cards, TestData.Meta);
                    if (rec == null) continue;
                    var kind = GameModes.Of(rec.GameType);
                    var at = $"{rec.GameType} game at {rec.Start:g}";
                    if (!GameModes.HasLadderRank(rec.GameType)) Assert.True(rec.Rank == null, $"{at} kept a ladder rank");
                    if (kind == GameModes.Kind.Battlegrounds)
                    {
                        Assert.True(rec.Decided, $"{at}: result '{rec.Result}'");
                        Assert.InRange(rec.Place, 1, 8);
                        Assert.False(string.IsNullOrEmpty(rec.Hero), $"{at}: no hero");
                        Assert.Null(rec.OpponentName);
                        Console.WriteLine($"{at}: {rec.Result}, {GameModes.Ordinal(rec.Place)} as {TestData.Cards.NameOf(rec.Hero)}");
                        bg++;
                        continue;
                    }
                    if (!rec.Decided) continue;
                    if (!GameModes.IsCardGame(rec.GameType) || kind == GameModes.Kind.Solo) continue;
                    Assert.True(HeroClasses.IsReal(rec.OpponentClass), $"{at}: opponent class '{rec.OpponentClass}'");
                    Assert.True(HeroClasses.IsReal(rec.MyClass), $"{at}: your class '{rec.MyClass}'");
                    // Modes you queue for log their deck right before the game, so the rule above still finds it.
                    if (kind is GameModes.Kind.Ranked or GameModes.Kind.Casual or GameModes.Kind.Brawl or GameModes.Kind.Arena)
                        Assert.True(rec.DeckCards.Count > 0, $"{at}: no deck");
                    var classes = rec.DeckCards.Select(id => TestData.Cards.ById(id)?.CardClass).Where(HeroClasses.IsReal).ToList();
                    Assert.True(classes.Count(c => c == rec.MyClass) * 2 >= classes.Count, $"{at}: deck '{rec.DeckName}' isn't {rec.MyClass}");
                    if (kind == GameModes.Kind.Arena)
                    {
                        Assert.EndsWith("Arena deck", rec.DeckName);
                        Assert.Null(rec.MyArchetype);
                        arena++;
                    }
                    card++;
                }
            }
            Assert.True(card > 0, "no card games in the logs");
            Console.WriteLine($"Checked {card} card games ({arena} Arena) and {bg} Battlegrounds games.");
        }
    }
}
