using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    public class LadderReportTests
    {
        private static readonly DateTime Nov6 = new DateTime(2026, 11, 6, 22, 0, 0);

        private static GameRecord G(DateTime start, string result, int level, int stars = 0, int bonus = 1, string deck = "Aura Paladin",
            int minutes = 10, string type = "GT_RANKED", int oppLevel = 0, int oppLegend = 0) =>
            new GameRecord
            {
                Start = start, End = start.AddMinutes(minutes), Result = result, GameType = type, Format = "FT_STANDARD",
                StarLevel = level, Stars = stars, StarBonus = bonus, DeckName = deck, MyClass = "PALADIN",
                OpponentStarLevel = oppLevel, OpponentLegendRank = oppLegend,
            };

        /// <summary>Your real games of Nov 6: the rank each started from matches the rules (bonus x10 dropping at floors, streak doubling).</summary>
        [Fact]
        public void Rules_match_your_saved_games()
        {
            // Bronze 7 with 1 star, x10: a win is +10 stars, past the Bronze 5 floor, so the bonus drops to x9.
            var from = LadderRules.StarIndex(4, 1);
            var to = from + LadderRules.WinStars(4, 10, 2);
            Assert.Equal(LadderRules.StarIndex(7, 2), to);
            Assert.Equal(9, LadderRules.BonusAfter(10, from, to));
            // The 3rd win in a row doubles: x9 -> +18, to Silver 8 with 2 stars, past the Silver 10 floor: x8.
            var to2 = to + LadderRules.WinStars(7, 9, 3);
            Assert.Equal(LadderRules.StarIndex(13, 2), to2);
            Assert.Equal(8, LadderRules.BonusAfter(9, to, to2));
            // No streak doubling from Diamond 5 up.
            Assert.Equal(1, LadderRules.WinStars(46, 1, 5));
            Assert.True(LadderRules.IsFloor(1) && LadderRules.IsFloor(6) && LadderRules.IsFloor(46) && !LadderRules.IsFloor(7) && !LadderRules.IsFloor(51));
            // x8 at Silver 8 (floor Silver 10): seven more floors to x1, which is Diamond 5.
            Assert.Equal(46, LadderRules.BonusEndsAtLevel(13, 8));
            Assert.Null(LadderRules.BonusEndsAtLevel(13, 1));
            Assert.Null(LadderRules.BonusEndsAtLevel(13, 20));   // lasts to Legend
        }

        [Fact]
        public void A_star_bonus_climbs_even_at_50_percent()
        {
            Assert.Null(LadderEstimate.GamesToReach(13, 46, 0.5));
            var withBonus = LadderEstimate.GamesToReach(13, 46, 0.5, 0, 8);
            Assert.NotNull(withBonus);
            Assert.True(LadderEstimate.GamesToReach(13, 46, 0.55, 0, 8) < LadderEstimate.GamesToReach(13, 46, 0.55, 0, 1));
            Assert.True(LadderEstimate.GamesToReach(13, 16, 0.55, 0, 1, 2) <= LadderEstimate.GamesToReach(13, 16, 0.55, 0, 1, 0));   // a streak going
        }

        [Fact]
        public void Report_paces_goals_against_the_season_end()
        {
            var games = new List<GameRecord>();
            var t = Nov6.AddDays(-5);
            var lvl = 20;
            for (var i = 0; i < 20; i++) games.Add(G(t.AddHours(i * 6), i % 3 == 2 ? "LOST" : "WON", lvl + i / 4, bonus: 1));
            var f = Ladder.From(games).Single();
            var end = new DateTime(2026, 12, 1, 8, 3, 36, DateTimeKind.Utc);
            var r = LadderReport.Make(f, games, new[] { ("Aura Paladin", "PALADIN", (double?)0.58) }, end, Nov6);

            Assert.InRange(r.DaysLeft.Value, 24, 25);
            Assert.InRange(r.GamesPerDay, 3.9, 4.1);   // 20 games over 5 days
            Assert.Equal(new[] { "Gold 5", "Diamond 5", "Legend" }, r.Goals.Select(g => g.Target));   // Gold 6 now: next floor Gold 5
            Assert.All(r.Goals, g => Assert.NotNull(g.Games));
            Assert.True(r.Goals[0].Games < r.Goals[1].Games && r.Goals[1].Games < r.Goals[2].Games);
            var d5 = r.Goals[1];
            Assert.Equal(Nov6.Date.AddDays(Math.Ceiling(d5.Games.Value / r.GamesPerDay)), d5.EtaDate);
            Assert.Equal(d5.Games.Value / r.DaysLeft.Value, d5.PerDayNeeded.Value, 3);
            Assert.DoesNotContain(r.Goals, g => g.InTime && g.EtaDate > end.ToLocalTime());
        }

        [Fact]
        public void Streaks_and_bonus_stars_this_season()
        {
            var games = new List<GameRecord>
            {
                G(Nov6, "WON", 1, bonus: 10),                 // +10: 9 from the bonus
                G(Nov6.AddMinutes(20), "WON", 4, 1, 10),      // +10: 9 from the bonus
                G(Nov6.AddMinutes(40), "WON", 7, 2, 9),       // 3rd in a row: +18, 8 bonus x2 = 16 from the bonus, 9 from the streak
                G(Nov6.AddMinutes(60), "LOST", 13, 2, 8),
                G(Nov6.AddMinutes(80), "WON", 13, 1, 8),
            };
            var (bonus, streak) = LadderReport.ExtraStars(games);
            Assert.Equal(9 + 9 + 16 + 7, bonus);
            Assert.Equal(9, streak);
            var f = Ladder.From(games).Single();
            var r = LadderReport.Make(f, games, null, null, Nov6.AddHours(2));
            Assert.Equal(1, r.Streak);
            Assert.False(r.NextWinDoubles);
            Assert.Equal(8, r.StarBonus);
            Assert.Equal(46, r.BonusEndsAt);
            Assert.Null(r.DaysLeft);
            Assert.All(r.Goals, g => Assert.Null(g.PerDayNeeded));   // no season end known
        }

        [Fact]
        public void Session_net_stars_and_loss_streaks()
        {
            var s = new List<GameRecord>
            {
                G(Nov6, "WON", 4, 1, 10),
                G(Nov6.AddMinutes(20), "WON", 7, 2, 9),
                G(Nov6.AddMinutes(40), "LOST", 13, 2, 8),
                G(Nov6.AddMinutes(60), "LOST", 13, 1, 8),
                G(Nov6.AddMinutes(80), "LOST", 13, 0, 8),   // at 13/0 = 36 stars: not a floor, so it costs a star
            };
            var c = LadderReport.Check(s, s, Nov6.AddMinutes(95));
            Assert.True(c.Current);
            Assert.Equal(3, c.LossStreak);
            Assert.Equal(LadderRules.StarIndex(13, 0) - LadderRules.StarIndex(4, 1) - 1, c.NetStars);
            Assert.False(LadderReport.Check(s, s, Nov6.AddHours(5)).Current);

            // Every game played while 3+ losses in a row: the W after LLL, then 5 more L and the final W after LLLLLLLL.
            var hist = new List<GameRecord>();
            var t = Nov6.AddDays(-3);
            foreach (var res in "LLLWLLLLLLLLW") { hist.Add(G(t, res == 'W' ? "WON" : "LOST", 20)); t = t.AddMinutes(15); }
            var c2 = LadderReport.Check(hist, hist, t);
            Assert.Equal(7, c2.AfterThreeLossesGames);
            Assert.Equal(2 / 7.0, c2.AfterThreeLosses.Value, 6);
        }

        [Fact]
        public void Opponent_ranks_hint_at_hidden_rating()
        {
            var games = Enumerable.Range(0, 12).Select(i => G(Nov6.AddMinutes(i * 15), "WON", 13, oppLevel: i < 2 ? 0 : 42)).ToList();
            games.Add(G(Nov6.AddHours(4), "WON", 13, oppLegend: 500));
            var o = LadderReport.OpponentRanks(games);
            Assert.Equal(10, o.Games);
            Assert.Equal(13, o.AverageYours, 3);
            Assert.Equal((42 * 9 + 51) / 10.0, o.AverageTheirs, 3);
            Assert.True(o.Gap > 25);
            Assert.Equal(0, LadderReport.OpponentRanks(new List<GameRecord> { G(Nov6, "WON", 13) }).Games);
        }

        [Fact]
        public void Climbing_decks_ranked_by_stars_per_hour()
        {
            var games = new List<GameRecord>();
            var t = Nov6.AddDays(-2);
            for (var i = 0; i < 10; i++) games.Add(G(t = t.AddMinutes(30), i < 6 ? "WON" : "LOST", 20, deck: "Slow Control", minutes: 15));   // 60%, 15 min
            for (var i = 0; i < 10; i++) games.Add(G(t = t.AddMinutes(30), i < 6 ? "WON" : "LOST", 20, deck: "Fast Aggro", minutes: 6));     // 60%, 6 min
            var decks = LadderReport.Climbers(games, new[] { ("New Deck", "MAGE", (double?)0.55), ("Fast Aggro", "HUNTER", (double?)0.52) }, Nov6, new Ladder.Rank(20));
            Assert.Equal("Fast Aggro", decks[0].Name);
            Assert.True(decks[0].YourStarsPerHour > decks.First(d => d.Name == "Slow Control").YourStarsPerHour * 2);
            var fresh = decks.First(d => d.Name == "New Deck");
            Assert.Equal(0, fresh.Games);
            Assert.NotNull(fresh.MetaStarsPerHour);   // no games yet: your average game length and the meta's win rate
            Assert.Null(fresh.YourStarsPerHour);
            Assert.Equal(0.6 * (1 + 0.36) - 0.4, LadderReport.StarsPerGame(0.6, true), 6);
            Assert.Equal(0.2, LadderReport.StarsPerGame(0.6, false), 6);
        }

        [Fact]
        public void Win_rate_by_time_of_day_needs_enough_games()
        {
            var games = new List<GameRecord>();
            for (var i = 0; i < 6; i++) games.Add(G(new DateTime(2026, 11, 1 + i, 9, 0, 0), i < 5 ? "WON" : "LOST", 20));   // mornings 5-1
            for (var i = 0; i < 6; i++) games.Add(G(new DateTime(2026, 11, 1 + i, 23, 0, 0), i < 2 ? "WON" : "LOST", 20));  // late nights 2-4
            games.Add(G(new DateTime(2026, 11, 2, 14, 0, 0), "WON", 20));
            games.Add(G(new DateTime(2026, 11, 2, 15, 0, 0), "WON", 20, type: "GT_VS_AI"));   // not counted
            var (blocks, best) = LadderReport.ByTimeOfDay(games);
            Assert.Equal(new[] { 6, 1, 0, 6 }, blocks.Select(b => b.Games));
            Assert.Equal("Morning", best.Name);
            var (_, none) = LadderReport.ByTimeOfDay(games.Take(6).ToList());
            Assert.Null(none);   // nothing to compare with
        }

        [Fact]
        public void Quest_planner_ignores_event_bonus_brackets()
        {
            var q = new QuestEntry { Text = QuestEntry.Fill("Draw $q cards in Traditional Modes.<i>(Ranked +1, Legendary +1)</i>", 50), Quota = 50 };
            Assert.NotNull(QuestPlan.RuleFor(q, TestData.Cards));
        }
    }
}
