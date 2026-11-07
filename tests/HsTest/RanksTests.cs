using System.Collections.Generic;
using Xunit;

namespace HearthStoner.Tests
{
    public class RanksTests
    {
        [Theory]
        [InlineData(1, 0, "Bronze 10")]
        [InlineData(10, 0, "Bronze 1")]
        [InlineData(11, 0, "Silver 10")]
        [InlineData(13, 0, "Silver 8")]
        [InlineData(50, 0, "Diamond 1")]
        [InlineData(0, 0, "unranked")]
        [InlineData(51, 512, "Legend 512")]
        [InlineData(51, 12076, "Legend 12076")]   // saved without a thousands separator: AppData.PrettyRank reads it back
        public void Saved_rank_names_keep_their_format(int starLevel, int legendRank, string expected)
        {
            Assert.Equal(expected, Ranks.Name(starLevel, legendRank));
        }

        [Theory]
        [InlineData(13, 0, "bronze-gold")]
        [InlineData(30, 0, "bronze-gold")]
        [InlineData(31, 0, "platinum")]
        [InlineData(41, 0, "diamond")]
        [InlineData(51, 0, "legend")]
        [InlineData(45, 300, "legend")]
        public void Brackets_follow_the_rank(int starLevel, int legendRank, string expected)
        {
            Assert.Equal(expected, Ranks.BracketFor(starLevel, legendRank));
        }

        [Fact]
        public void Auto_bracket_uses_the_latest_ranked_game_and_a_fixed_setting_wins()
        {
            var games = new List<GameRecord>
            {
                new GameRecord { StarLevel = 45 },
                new GameRecord { StarLevel = 13 },
                new GameRecord { StarLevel = 0 },   // casual: ignored
            };
            Assert.Equal("bronze-gold", Ranks.MetaBracket(new Settings { MetaRank = "auto" }, games));
            Assert.Equal("all", Ranks.MetaBracket(new Settings { MetaRank = "auto" }, new List<GameRecord>()));
            Assert.Equal("diamond", Ranks.MetaBracket(new Settings { MetaRank = "diamond" }, games));
        }
    }
}
