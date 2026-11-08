using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>The numbers behind the wide-screen additions: today's session, share changes, best day, win tables, practice ahead.</summary>
    public class PageNumbersTests
    {
        private static GameRecord G(DateTime start, bool won, int minutes = 10, string type = "GT_RANKED", string rank = null, string cls = "PALADIN", string opp = "MAGE", bool first = true, string deck = "Aura") =>
            new GameRecord { Start = start, End = start.AddMinutes(minutes), Result = won ? "WON" : "LOST", GameType = type, Rank = rank, MyClass = cls, OpponentClass = opp, WentFirst = first, DeckName = deck };

        [Theory]
        [InlineData("2026-11-08 03:00", "2026-11-07 04:00")]   // before 4 AM: still yesterday's gaming day
        [InlineData("2026-11-08 04:00", "2026-11-08 04:00")]
        [InlineData("2026-11-08 23:30", "2026-11-08 04:00")]
        public void A_gaming_day_runs_4_AM_to_4_AM(string now, string start) =>
            Assert.Equal(DateTime.Parse(start), DaySession.DayStart(DateTime.Parse(now)));

        [Fact]
        public void Today_counts_games_since_4_AM_and_keeps_the_last_ten_results()
        {
            var now = new DateTime(2026, 11, 8, 2, 0, 0);
            var games = new List<GameRecord>
            {
                G(new DateTime(2026, 11, 7, 3, 0, 0), true),                       // the day before
                G(new DateTime(2026, 11, 7, 22, 0, 0), true, 12, rank: "Gold 2"),
                G(new DateTime(2026, 11, 7, 23, 0, 0), false, 8, rank: "Gold 1"),
                G(new DateTime(2026, 11, 8, 1, 0, 0), true, 15, type: "GT_ARENA"),
            };
            var s = DaySession.For(games, now);
            Assert.Equal(3, s.Games);
            Assert.Equal(2, s.Wins);
            Assert.Equal(1, s.Losses);
            Assert.Equal(TimeSpan.FromMinutes(35), s.Played);
            Assert.Equal("Gold 2", s.RankAtStart);
            Assert.Equal(4, s.Recent.Count);
            Assert.Equal(games[0].Start, s.Recent[0].Start);

            var many = Enumerable.Range(0, 14).Select(i => G(now.AddHours(-i - 1), i % 2 == 0)).ToList();
            var last = DaySession.For(many, now).Recent;
            Assert.Equal(10, last.Count);
            Assert.True(last.SequenceEqual(last.OrderBy(g => g.Start)));
            Assert.Equal(now.AddHours(-1), last[9].Start);
        }

        [Fact]
        public void Class_share_change_adds_up_each_class_s_archetypes()
        {
            MetaHistory.Snapshot Snap(params (string Key, double Share)[] a) =>
                new MetaHistory.Snapshot { Archetypes = a.ToDictionary(x => x.Key, x => new MetaHistory.Arch { Name = x.Key, Share = x.Share }) };
            var cls = new Dictionary<string, string> { ["thief-priest"] = "priest", ["egg-priest"] = "priest", ["zoolock"] = "warlock" };
            var change = MetaHistory.ClassShareChange(
                Snap(("thief-priest", 0.10), ("egg-priest", 0.05), ("zoolock", 0.20), ("gone", 0.3)),
                Snap(("thief-priest", 0.15), ("egg-priest", 0.04), ("zoolock", 0.18)),
                k => cls.TryGetValue(k, out var c) ? c : null);
            Assert.Equal(0.04, change["priest"], 6);
            Assert.Equal(-0.02, change["warlock"], 6);
            Assert.Equal(2, change.Count);   // the unknown archetype is left out
            Assert.Empty(MetaHistory.ClassShareChange(null, Snap(), k => k));
        }

        [Fact]
        public void Previous_is_the_newest_snapshot_before_today_at_the_same_rank()
        {
            var h = new MetaHistory();
            foreach (var (day, rank) in new[] { (5, "gold"), (6, "gold"), (6, "legend"), (8, "gold") })
                h.Add(new MetaHistory.Snapshot { Day = new DateTime(2026, 11, day), Rank = rank, Period = "past-7" });
            var today = new MetaHistory.Snapshot { Day = new DateTime(2026, 11, 8), Rank = "gold", Period = "past-7" };
            Assert.Equal(new DateTime(2026, 11, 6), h.Previous(today).Day);
            Assert.Null(h.Previous(new MetaHistory.Snapshot { Day = new DateTime(2026, 11, 8), Rank = "diamond", Period = "past-7" }));
        }

        [Fact]
        public void Best_day_has_the_most_wins_over_losses()
        {
            var d1 = new DateTime(2026, 11, 3, 20, 0, 0);
            var d2 = new DateTime(2026, 11, 5, 20, 0, 0);
            var games = new[] { G(d1, true), G(d1.AddHours(1), true), G(d1.AddHours(2), true), G(d1.AddHours(3), false),
                                G(d2, true), G(d2.AddHours(1), true), G(d2.AddHours(5), true) };   // 01:00 on the 6th: still the 5th's day
            var best = LadderReport.BestDay(games);
            Assert.Equal((new DateTime(2026, 11, 5), 3, 0), best);
            Assert.Null(LadderReport.BestDay(new GameRecord[0]));
        }

        [Fact]
        public void Rolling_win_rate_starts_at_the_window_th_game()
        {
            var t = new DateTime(2026, 11, 1);
            var games = new[] { true, true, false, false, true }.Select((w, i) => G(t.AddHours(i), w)).ToList();
            games.Add(new GameRecord { Start = t.AddHours(10), Result = null });   // unfinished: skipped
            Assert.Equal(new[] { 2 / 3.0, 1 / 3.0, 1 / 3.0 }, WinTables.Rolling(games, 3));
            Assert.Empty(WinTables.Rolling(games.Take(2), 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => WinTables.Rolling(games, 0));
        }

        [Fact]
        public void Cross_splits_records_two_ways()
        {
            var t = new DateTime(2026, 11, 1);
            var games = new[] { G(t, true, opp: "MAGE", first: true), G(t, false, opp: "MAGE", first: true), G(t, true, opp: "MAGE", first: false), G(t, true, opp: null) };
            var c = WinTables.Cross(games, g => g.OpponentClass, g => g.WentFirst ? "first" : "coin");
            Assert.Equal(2, c.Count);
            Assert.Equal(1, c[("MAGE", "first")].Wins);
            Assert.Equal(1, c[("MAGE", "first")].Losses);
            Assert.Equal(0.5, c[("MAGE", "first")].WinRate);
            Assert.Equal(1, c[("MAGE", "coin")].Games);
        }

        [Fact]
        public void Upcoming_counts_questions_due_on_each_coming_day()
        {
            var today = new DateTime(2026, 11, 8);
            var s = new PracticeStore();
            s.Record("play:a", false, today);                  // due tomorrow
            s.Record("play:b", true, today);                   // due in 3 days
            s.Record("play:c", true, today.AddDays(-5));       // due 2 days ago: today's review, not upcoming
            s.Cards["play:d"] = new PracticeCard { Due = today.AddDays(1), Retired = true };
            var next = s.Upcoming(today, 7);
            Assert.Equal(7, next.Count);
            Assert.Equal((today.AddDays(1), 1), next[0]);
            Assert.Equal(1, next[2].Due);
            Assert.Equal(2, next.Sum(x => x.Due));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Upcoming(today, 0));
        }
    }
}