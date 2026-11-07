using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    public class PredictionScoreTests
    {
        private static GameRecord Game(string final, double sure, params (int Turn, string Guess)[] guesses) => new GameRecord
        {
            Result = "WON", OpponentArchetype = final, OpponentArchetypeProbability = sure,
            Snapshots = guesses.Select(g => new TurnSnapshot { Turn = g.Turn, OpponentGuess = g.Guess, OpponentGuessProbability = 0.5 }).ToList(),
        };

        [Fact]
        public void Counts_how_often_the_guess_at_each_round_matched_the_final_one()
        {
            var games = new List<GameRecord>
            {
                Game("Zoolock", 0.95, (1, "Cthun Warlock"), (2, "Zoolock"), (3, "Zoolock")),
                Game("Zoolock", 0.9, (1, "Zoolock"), (2, "Zoolock")),
                Game("Dragon Warrior", 0.5, (1, "Dragon Warrior")),   // never sure: not counted
            };
            var s = PredictionScore.Compute(games);
            Assert.Equal(2, s.Games);
            Assert.Equal(1, s.Rounds[0].Number);
            Assert.Equal(0.5, s.Rounds[0].Accuracy);   // turn 1 (round 1): one of two right
            Assert.Equal(1.0, s.Rounds[1].Accuracy);   // round 2 (turns 3-4): only the first game reached it, and it was right
        }

        [Trait("Category", TestData.MyGames)]
        [Fact]
        public void Backfilled_guesses_match_what_the_recorder_saves_live()
        {
            // The newest finished ranked game (the newest log may hold a game still being played).
            var (log, slice) = Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*")
                .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).Where(f => new FileInfo(f).Length > 20000).OrderByDescending(f => f)
                .Select(f => (f, GameLog.Split(GameLog.ReadShared(f), GameLog.SessionStart(f), f).LastOrDefault(x => x.Complete && GameLog.ModeOf(x.Lines).GameType == "GT_RANKED")))
                .First(x => x.Item2 != null);
            var rec = GameLog.Record(slice, DecksLog.LastQueuedDeck(log, slice.Start), TestData.Cards, TestData.Meta);
            Assert.True(rec.Imported);
            var live = rec.Snapshots.Select(t => (t.Turn, t.OpponentGuess)).ToList();
            Assert.Contains(live, x => x.OpponentGuess != null);
            foreach (var t in rec.Snapshots) { t.OpponentGuess = null; t.OpponentGuessProbability = 0; }
            Assert.True(PredictionScore.Backfill(rec, slice.Lines, TestData.Cards, TestData.Meta));
            Assert.Equal(live, rec.Snapshots.Select(t => (t.Turn, t.OpponentGuess)).ToList());
        }
    }

    public class RankHistoryTests
    {
        [Fact]
        public void Keeps_the_rank_of_every_finished_game_in_order()
        {
            var games = new List<GameRecord>
            {
                new GameRecord { Result = "WON", Start = new System.DateTime(2026, 10, 30, 2, 0, 0), LegendRank = 10932 },
                new GameRecord { Result = "LOST", Start = new System.DateTime(2026, 10, 30, 1, 0, 0), LegendRank = 10839 },
                new GameRecord { Result = "WON", Start = new System.DateTime(2026, 10, 30, 3, 0, 0) },   // no rank: skipped
            };
            var s = StatsSummary.From(games);
            Assert.Equal(new[] { 10839, 10932 }, s.RankHistory.Select(r => r.Legend));
        }
    }
}

namespace HearthStoner.Tests
{
    public class DeckImportTests
    {
        private static string AnyCode() => DecksLog.SavedDecks(TestData.InstallDir).First(d => d.DeckCode != null && (DeckCode.Decode(d.DeckCode, TestData.Cards)?.Count ?? 0) > 0).DeckCode;

        [Trait("Category", TestData.MyGames)]
        [Fact]
        public void Finds_a_bare_code() => Assert.Equal(AnyCode(), DeckCode.FindCode("  " + AnyCode() + "  ", TestData.Cards, out _));

        [Trait("Category", TestData.MyGames)]
        [Fact]
        public void Finds_the_code_and_name_in_hearthstones_copy_text()
        {
            var code = AnyCode();
            var text = "### Streamer Paladin\n# Class: Paladin\n# Format: Standard\n#\n# 2x (1) Righteous Protector\n#\n" + code + "\n#\n# To use this deck, copy it to your clipboard and create a new deck in Hearthstone\n";
            Assert.Equal(code, DeckCode.FindCode(text, TestData.Cards, out var name));
            Assert.Equal("Streamer Paladin", name);
        }

        [Theory]
        [InlineData("")]
        [InlineData("hello there")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public void Rejects_text_without_a_valid_code(string text) => Assert.Null(DeckCode.FindCode(text, TestData.Cards, out _));
    }
}
