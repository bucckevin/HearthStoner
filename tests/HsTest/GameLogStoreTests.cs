using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    public class GameLogStoreTests
    {
        private static string Log(string folder) => Path.Combine(TestData.InstallDir, "Logs", folder, "Power.log");

        [Fact]
        public void Splits_a_session_into_games_with_their_start_time()
        {
            var path = Log("Hearthstone_2026_10_30_01_18_26");
            if (!File.Exists(path)) return;   // Hearthstone deletes old log folders (on a real install)
            var slices = GameLog.Split(GameLog.ReadShared(path), GameLog.SessionStart(path), path);
            var game = Assert.Single(slices);
            Assert.True(game.Complete);
            Assert.All(game.Lines, l => Assert.Contains("GameState.", l));
            // The live record of this game started at 01:20:22.
            Assert.InRange(game.Start, new DateTime(2026, 10, 30, 1, 19, 0), new DateTime(2026, 10, 30, 1, 22, 0));
        }

        [Theory]
        [InlineData("Hearthstone_2026_10_30_01_18_26", "WON", "DEATHKNIGHT")]
        [InlineData("Hearthstone_2026_10_30_01_53_11", "LOST", "DRUID")]
        [InlineData("Hearthstone_2026_10_30_02_18_32", "LOST", "PRIEST")]
        public void Rebuilds_the_same_record_from_the_log(string folder, string result, string oppClass)
        {
            var path = Log(folder);
            if (!File.Exists(path)) return;
            var slice = GameLog.Split(GameLog.ReadShared(path), GameLog.SessionStart(path), path).Last(s => s.Complete);
            var rec = GameLog.Record(slice, DecksLog.LastQueuedDeck(path, slice.Start), TestData.Cards, TestData.Meta);
            Assert.Equal(result, rec.Result);
            Assert.Equal(oppClass, rec.OpponentClass);
            Assert.Equal("Aura Paladin", rec.DeckName);
            Assert.True(rec.Snapshots.Count > 10);
            Assert.True(Math.Abs((rec.Start - slice.Start).TotalMinutes) < 3, $"start {rec.Start} vs log {slice.Start}");
            Assert.True(rec.End > rec.Start && rec.Duration < TimeSpan.FromHours(1), $"{rec.Start} - {rec.End}");
            Assert.False(string.IsNullOrEmpty(rec.OpponentName));
        }
    }
}
