using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>The choices behind the Ladder, History and Stats views: format tabs, mode chips, floor progress.</summary>
    public class LadderViewTests
    {
        private static GameRecord Ranked(string format, DateTime start, int level = 30, string result = "WON") =>
            new GameRecord { GameType = "GT_RANKED", Format = format, Start = start, End = start.AddMinutes(10), StarLevel = level, Result = result };

        [Fact]
        public void Ladder_opens_on_the_format_with_most_games_this_season()
        {
            var now = new DateTime(2026, 11, 8, 1, 0, 0);
            var games = new List<GameRecord>();
            for (var i = 0; i < 5; i++) games.Add(Ranked("FT_STANDARD", now.AddHours(-10 + i)));
            games.Add(Ranked("FT_WILD", now.AddHours(-1), level: 1));   // played last, but only once
            var formats = Ladder.From(games);
            Assert.Equal("FT_STANDARD", Ladder.Pick(formats, null, now).FormatType);
            Assert.Equal("FT_WILD", Ladder.Pick(formats, "FT_WILD", now).FormatType);           // your pick wins
            Assert.Equal("FT_STANDARD", Ladder.Pick(formats, "FT_CLASSIC", now).FormatType);    // a pick with no games doesn't
            Assert.Null(Ladder.Pick(new List<Ladder.Format>(), null, now));
            Assert.Equal("Wild", Ladder.Short(formats.First(f => f.FormatType == "FT_WILD")));
        }

        [Theory]
        [InlineData(30, 0, 26, 31, 0.8)]    // Gold 1: from the Gold 5 floor to Platinum 10
        [InlineData(26, 0, 26, 31, 0.0)]    // on a floor
        [InlineData(1, 0, 1, 6, 0.0)]       // Bronze 10
        [InlineData(50, 2, 46, 51, 14 / 15.0)]   // Diamond 1, 2 stars: from Diamond 5 to Legend
        public void Progress_between_floors(int level, int stars, int from, int to, double fraction)
        {
            var p = LadderRules.FloorProgress(level, stars);
            Assert.Equal(from, p.FromLevel);
            Assert.Equal(to, p.ToLevel);
            Assert.Equal(fraction, p.Fraction, 3);
        }

        [Fact]
        public void History_chips_are_the_modes_you_played()
        {
            var games = new[] { "GT_VS_AI", "GT_RANKED", "GT_ARENA", "GT_TAVERNBRAWL", "GT_RANKED", "GT_UNKNOWN" }
                .Select(t => new GameRecord { GameType = t }).ToList();
            Assert.Equal(new[] { GameModes.Kind.Ranked, GameModes.Kind.Brawl, GameModes.Kind.Arena, GameModes.Kind.Solo }, GameModes.Played(games));
            Assert.Empty(GameModes.Played(new List<GameRecord>()));
        }
    }
}
