using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace HearthStoner.Tests
{
    public class PracticeTests
    {
        private static readonly DateTime Day = new DateTime(2026, 11, 7);

        [Fact]
        public void A_miss_comes_back_tomorrow_and_three_right_in_a_row_retire_a_question()
        {
            var s = new PracticeStore();
            s.Record("mull:a", false, Day);
            Assert.Empty(s.Due(Day));
            Assert.Equal(new[] { "mull:a" }, s.Due(Day.AddDays(1)));

            s.Record("mull:a", true, Day.AddDays(1));
            Assert.Equal(Day.AddDays(4), s.Cards["mull:a"].Due);       // 3 days after the first right answer
            s.Record("mull:a", true, Day.AddDays(4));
            Assert.Equal(Day.AddDays(11), s.Cards["mull:a"].Due);      // then 7
            s.Record("mull:a", true, Day.AddDays(11));
            Assert.True(s.Cards["mull:a"].Retired);
            Assert.Empty(s.Due(Day.AddDays(100)));

            s.Record("disc:b", true, Day);                              // right the first time: back in 3 days
            Assert.Equal(new[] { "disc:b" }, s.Due(Day.AddDays(3)));
        }

        [Fact]
        public void The_streak_counts_days_in_a_row_up_to_today_or_yesterday()
        {
            var s = new PracticeStore();
            foreach (var d in new[] { 0, 1, 2, 4 }) s.Record("x:" + d, true, Day.AddDays(d));
            Assert.Equal(1, s.StreakDays(Day.AddDays(4)));
            Assert.Equal(1, s.StreakDays(Day.AddDays(5)));   // played yesterday: still on
            Assert.Equal(0, s.StreakDays(Day.AddDays(6)));
            Assert.Equal(3, s.StreakDays(Day.AddDays(2)));
        }

        [Fact]
        public void The_store_round_trips_through_its_file()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hs-practice-{Guid.NewGuid():N}.json");
            var old = Environment.GetEnvironmentVariable("HEARTHSTONER_PRACTICE");
            try
            {
                Environment.SetEnvironmentVariable("HEARTHSTONER_PRACTICE", path);
                var s = new PracticeStore();
                s.Record("hand:g:9", false, Day);
                s.Save();
                var back = PracticeStore.Load();
                Assert.Equal(1, back.Cards["hand:g:9"].Wrong);
                Assert.Equal(Day.AddDays(1), back.Cards["hand:g:9"].Due);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HEARTHSTONER_PRACTICE", old);
                File.Delete(path);
            }
        }

        [Fact]
        public void Question_ids_split_back_into_drill_game_and_turn()
        {
            var g = new GameRecord { Start = new DateTime(2026, 11, 7, 3, 26, 0), MyClass = "PALADIN", OpponentClass = "SHAMAN", Result = "WON" };
            var id = PracticeIds.Make(PracticeIds.Hand, g, 9);
            Assert.Equal(PracticeIds.Hand, PracticeIds.DrillOf(id));
            Assert.Equal(g.FileStem, PracticeIds.GameOf(id));
            Assert.Equal(9, PracticeIds.TurnOf(id));
            Assert.Equal(-1, PracticeIds.TurnOf(PracticeIds.Make(PracticeIds.Secret, g)));
            Assert.Equal(PracticeIds.Seed(id), PracticeIds.Seed(id));
            Assert.NotEqual(PracticeIds.Seed(id), PracticeIds.Seed(id + "x"));
        }

        [Fact]
        public void Best_mana_use_is_the_fullest_set_that_fits()
        {
            Assert.Equal(6, PracticeDrills.BestManaUse(new[] { 4, 2, 5, 2 }, 6));
            Assert.Equal(5, PracticeDrills.BestManaUse(new[] { 3, 3, 5 }, 5));
            Assert.Equal(0, PracticeDrills.BestManaUse(new[] { 7 }, 6));
            Assert.Equal(0, PracticeDrills.BestManaUse(new int[0], 6));
        }

        [Fact]
        public void The_discover_answer_is_the_option_with_the_best_win_rate()
        {
            var p = new DiscoverPick { Options = { "A", "B", "C" }, WinRates = { 0.51, 0.58, null } };
            Assert.Equal(1, PracticeDrills.BestOption(p));
            Assert.Equal(-1, PracticeDrills.BestOption(new DiscoverPick { Options = { "A", "B" }, WinRates = { 0.5, null } }));
            var g = new GameRecord { Discovers = { p, new DiscoverPick { Options = { "A" }, WinRates = { 0.5 } } } };
            Assert.Single(PracticeDrills.DiscoverQuestions(g));
        }

        private static Replay.Side Side() => new Replay.Side();

        [Fact]
        public void Hand_guesses_ask_about_revealed_face_down_cards_with_the_answer_among_the_choices()
        {
            var r = new Replay();
            var f = new Replay.Frame { Index = 0, Turn = 9, YourTurn = true, You = Side(), Them = Side() };
            f.Them.Hand.Add(new Replay.HandCard { EntityId = 10, Mark = new HandMark { Turn = 1 } });   // held since round 1
            f.Them.Hand.Add(new Replay.HandCard { EntityId = 11, Mark = new HandMark { Turn = 4 } });
            f.Them.Hand.Add(new Replay.HandCard { EntityId = 12 });                                      // never revealed
            f.Them.Hand.Add(new Replay.HandCard { EntityId = 13, CardId = "COIN" });                     // you could see it
            r.Frames.Add(f);
            r.FinalCardIds[10] = "REMNANT";
            r.FinalCardIds[11] = "CANNON";
            r.FinalCardIds[13] = "COIN";
            var pool = new[] { "REMNANT", "CANNON", "MURMY", "TOWER", "GRAVE" };
            var qs = PracticeDrills.GuessHand(r, 0, pool, 42);
            Assert.Equal(new[] { 10, 11 }, qs.Select(q => q.EntityId));
            Assert.All(qs, q => { Assert.Contains(q.Answer, q.Choices); Assert.Equal(4, q.Choices.Count); Assert.Equal(q.Choices.Count, q.Choices.Distinct().Count()); });
            Assert.Equal(4, qs[0].Held);   // round 5 minus round 1
            Assert.Equal(qs.Select(q => string.Join(",", q.Choices)), PracticeDrills.GuessHand(r, 0, pool, 42).Select(q => string.Join(",", q.Choices)));   // same seed, same question
        }

        [Fact]
        public void Secret_questions_come_from_hidden_secrets_the_game_revealed_later()
        {
            var r = new Replay();
            Replay.Frame F(int i, int turn, bool yours) { var f = new Replay.Frame { Index = i, Turn = turn, YourTurn = yours, You = Side(), Them = Side() }; r.Frames.Add(f); return f; }
            var played = F(0, 4, false);
            played.Them.Secrets.Add(""); played.Them.SecretEntities.Add(50);
            var mine = F(1, 5, true);
            mine.Them.Secrets.Add(""); mine.Them.SecretEntities.Add(50);
            r.TurnStarts[5] = 1;
            r.FinalCardIds[50] = "CORE_EX1_610";   // Explosive Trap
            var qs = PracticeDrills.Secrets(r, "HUNTER", 7);
            var q = Assert.Single(qs);
            Assert.Equal(1, q.Frame);
            Assert.Equal(0, q.PlayedFrame);
            Assert.Equal("CORE_EX1_610", q.Answer);
            Assert.Contains(q.Answer, q.Choices);
            Assert.All(q.Choices, id => Assert.Equal("HUNTER", Knowledge.Secrets.ById(id).Class));
            Assert.Empty(PracticeDrills.Secrets(r, "WARRIOR", 7));   // not a secret of their class: nothing to ask
        }

        [Fact]
        public void Matchup_keeps_average_your_picks_against_one_class()
        {
            GameRecord G(string cls, bool kept, double delta) => new GameRecord { OpponentClass = cls, Mulligan = { new MulliganPick { CardId = "A", Kept = kept, Advice = "TOSS", Delta = delta } } };
            var rows = PracticeDrills.MatchupKeeps(new[] { G("PRIEST", true, -0.04), G("PRIEST", false, -0.02), G("MAGE", true, 0.1) }, "priest");
            var row = Assert.Single(rows);
            Assert.Equal(2, row.Seen);
            Assert.Equal(0.5, row.KeepRate, 3);
            Assert.Equal(-0.03, row.Delta, 3);
        }
    }

    /// <summary>The replay drills on your own games: every question built has its answer among its choices.</summary>
    [Trait("Category", TestData.MyGames)]
    public class PracticeOnMyGamesTests
    {
        private readonly ITestOutputHelper _out;
        public PracticeOnMyGamesTests(ITestOutputHelper o) => _out = o;

        [Fact]
        public void Replay_drills_ask_answerable_questions()
        {
            int hands = 0, secrets = 0, plays = 0;
            foreach (var dir in Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*"))
            {
                var path = Path.Combine(dir, "Power.log");
                if (!File.Exists(path) || new FileInfo(path).Length < 20000) continue;
                foreach (var slice in GameLog.Split(GameLog.ReadShared(path), GameLog.SessionStart(path), path).Where(s => s.Complete && GameModes.IsCardGame(GameLog.ModeOf(s.Lines).GameType)))
                {
                    var r = Replay.Build(slice.Lines, TestData.Cards, null);
                    foreach (var (turn, fi) in r.TurnStarts.Where(kv => r.Frames[kv.Value].YourTurn).Select(kv => (kv.Key, kv.Value)))
                    {
                        var f = r.Frames[fi];
                        var pool = r.FinalCardIds.Values.Distinct().ToList();
                        foreach (var q in PracticeDrills.GuessHand(r, fi, pool, turn))
                        {
                            hands++;
                            Assert.Contains(q.Answer, q.Choices);
                            Assert.Null(f.Them.Hand[q.Slot].CardId);   // only cards you couldn't see are asked
                        }
                        var costs = f.You.Hand.Where(h => h.CardId != null && h.Cost <= f.You.Mana).Select(h => h.Cost).ToList();
                        if (costs.Count >= 2) { plays++; Assert.InRange(PracticeDrills.BestManaUse(costs, f.You.Mana), 0, f.You.Mana); }
                    }
                    foreach (var cls in new[] { "MAGE", "HUNTER", "PALADIN", "ROGUE" })
                        foreach (var q in PracticeDrills.Secrets(r, cls, 1))
                        {
                            secrets++;
                            Assert.Contains(q.Answer, q.Choices);
                            Assert.True(q.Frame >= q.PlayedFrame);
                        }
                }
            }
            _out.WriteLine($"{hands} hand guesses, {plays} play turns, {secrets} secrets");
            Assert.True(hands > 0, "no hand guesses in any saved game");
            Assert.True(plays > 0, "no play turns in any saved game");
        }
    }
}
