using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    public class LadderTrackerTests
    {
        private static readonly DateTime Oct = new DateTime(2026, 10, 10, 18, 0, 0, DateTimeKind.Utc);

        private static GameRecord G(DateTime start, string result, int level, int legend = 0, int stars = 0, string type = "GT_RANKED", string format = "FT_STANDARD") =>
            new GameRecord { Start = start, End = start.AddMinutes(10), Result = result, GameType = type, Format = format, StarLevel = level, LegendRank = legend, Stars = stars };

        [Fact]
        public void Star_levels_name_Bronze_10_to_Diamond_1_then_Legend()
        {
            Assert.Equal("Bronze 10", Ladder.Name(1));
            Assert.Equal("Bronze 1", Ladder.Name(10));
            Assert.Equal("Silver 10", Ladder.Name(11));
            Assert.Equal("Diamond 5", Ladder.Name(46));
            Assert.Equal("Diamond 1", Ladder.Name(50));
            Assert.Equal("Legend", Ladder.Name(51));
            Assert.Equal("Legend 12,076", Ladder.Name(51, 12076));
        }

        [Fact]
        public void Legend_ranks_beat_star_levels_and_lower_numbers_are_better()
        {
            var d1 = new Ladder.Rank(50, 2);
            Assert.True(new Ladder.Rank(51, 0, 12076).Score > d1.Score);
            Assert.True(new Ladder.Rank(51, 0, 100).Score > new Ladder.Rank(51, 0, 12076).Score);
            Assert.True(new Ladder.Rank(26, 2).Score > new Ladder.Rank(26, 0).Score);
            var cap = Ladder.LegendCap(new[] { new Ladder.Rank(51, 0, 12076) });
            Assert.True(Ladder.Height(new Ladder.Rank(51, 0, 12076), cap) > Ladder.Height(d1, cap));
            Assert.True(Ladder.Height(new Ladder.Rank(51, 0, 100), cap) > Ladder.Height(new Ladder.Rank(51, 0, 12076), cap));
            Assert.Contains(Ladder.Ticks(49, 52, cap), t => t.Label == "Legend");
            Assert.Contains(Ladder.Ticks(41, 46, cap), t => t.Label == "Diamond 10");
        }

        [Fact]
        public void Seasons_roll_over_at_midnight_Pacific()
        {
            Assert.Equal(new DateTime(2026, 10, 1), Ladder.SeasonOf(new DateTime(2026, 11, 1, 5, 32, 0, DateTimeKind.Utc)));    // 1:32am Eastern
            Assert.Equal(new DateTime(2026, 11, 1), Ladder.SeasonOf(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)));
        }

        [Fact]
        public void Seasons_keep_start_peak_and_finish_per_format()
        {
            var games = new List<GameRecord>
            {
                G(Oct.AddMonths(-1), "WON", 30),
                G(Oct.AddMonths(-1).AddHours(1), "WON", 33),
                G(Oct.AddMonths(-1).AddHours(2), "LOST", 32),
                G(Oct, "WON", 41),
                G(Oct.AddHours(1), "WON", 51, 9000),
                G(Oct.AddHours(2), "LOST", 51, 8000),
                G(Oct.AddHours(3), "LOST", 51, 8500),
                G(Oct.AddHours(4), "WON", 51, 1, type: "GT_CASUAL"),       // casual: not the ladder
                G(Oct.AddHours(5), "WON", 0),                               // no rank read: counts for results only
                G(Oct.AddHours(6), "WON", 20, format: "FT_WILD"),
            };
            var all = Ladder.From(games);
            Assert.Equal(new[] { "Ranked Wild", "Ranked Standard" }, all.Select(f => f.Name));
            var std = all[1];
            Assert.Equal(2, std.Seasons.Count);
            var oct = std.Latest;
            Assert.Equal("Diamond 10", oct.Start.Rank.ToString());
            Assert.Equal("Legend 8,000", oct.Peak.Rank.ToString());
            Assert.Equal("Legend 8,500", std.Current.ToString());
            Assert.Equal(3, oct.Wins);
            var sep = std.Seasons[0];
            Assert.Equal("Platinum 8", sep.Peak.Rank.ToString());
            Assert.Equal("Platinum 9", sep.Last.Rank.ToString());
            Assert.Empty(std.Milestones);    // Legend: no estimate
        }

        [Fact]
        public void Sessions_split_on_breaks_of_two_hours()
        {
            var a = G(Oct, "WON", 30);
            var b = G(a.End.AddMinutes(119), "LOST", 31);
            var c = G(b.End.AddMinutes(121), "WON", 30, stars: 2);
            var d = G(c.End.AddMinutes(5), "WON", 31, stars: 1);
            var sessions = Ladder.Sessions(new List<GameRecord> { d, c, b, a });
            Assert.Equal(new[] { 2, 2 }, sessions.Select(s => s.Games.Count));
            var last = sessions[1];
            Assert.Equal(2, last.Wins);
            Assert.Equal("up 2 stars", Ladder.Change(last.From, last.To));
            Assert.Equal("down 494 places", Ladder.Change(new Ladder.Rank(51, 0, 11582), new Ladder.Rank(51, 0, 12076)));
            Assert.Equal("Diamond 1 to Legend 9,000", Ladder.Change(new Ladder.Rank(50), new Ladder.Rank(51, 0, 9000)));
        }

        [Fact]
        public void Estimates_next_rank_Diamond_5_and_Legend()
        {
            var m = Ladder.Milestones(new Ladder.Rank(40), 0.55, 1);
            Assert.Equal(new[] { "Diamond 10", "Diamond 5", "Legend" }, m.Select(x => x.Target));
            Assert.True(m[0].Games < m[1].Games && m[1].Games < m[2].Games);
            Assert.Equal(new[] { "Diamond 5", "Legend" }, Ladder.Milestones(new Ladder.Rank(45), 0.55, 1).Select(x => x.Target));
            Assert.All(Ladder.Milestones(new Ladder.Rank(45), 0.5, 1), x => Assert.Null(x.Games));
            // Stars already in the rank and a star bonus both shorten the climb.
            Assert.True(LadderEstimate.GamesToReach(31, 41, 0.55, 2) < LadderEstimate.GamesToReach(31, 41, 0.55));
            Assert.True(LadderEstimate.GamesToReach(31, 41, 0.55, 0, 3) < LadderEstimate.GamesToReach(31, 41, 0.55));
        }
    }
}
