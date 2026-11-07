using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    public class SummaryTests
    {
        private static TurnSnapshot T(int turn, bool yours, int myHp, int theirHp) =>
            new TurnSnapshot { Turn = turn, Yours = yours, MyHp = myHp, TheirHp = theirHp };

        [Fact]
        public void Swing_takes_one_point_per_round_after_their_turn()
        {
            // You go first: your turns are odd. Board swings would zig-zag if both sides were plotted.
            var g = new GameRecord { Turns = 5, Result = "LOST", Snapshots = new List<TurnSnapshot>
            {
                T(1, true, 30, 30), T(2, false, 30, 30), T(3, true, 30, 20), T(4, false, 25, 28), T(5, true, 10, 28),
            } };
            var s = GameSummary.From(g);
            Assert.Equal(new[] { 1, 2, 3 }, s.Swing.Select(p => p.Round));
            Assert.Equal(new[] { 0, -3, -18 }, s.Swing.Select(p => p.Value));
            Assert.Equal(3, s.TurningRound);
            Assert.Equal(15, s.TurningDrop);
            Assert.Equal(0, s.RoundsAhead);
        }

        [Fact]
        public void Stats_count_wins_by_class_and_turn_order()
        {
            var games = new List<GameRecord>
            {
                new GameRecord { Result = "WON", WentFirst = true, OpponentClass = "PRIEST", DeckName = "A" },
                new GameRecord { Result = "LOST", WentFirst = false, OpponentClass = "PRIEST", DeckName = "A" },
                new GameRecord { Result = "LOST", WentFirst = false, OpponentClass = "DRUID", DeckName = "B" },
                new GameRecord { Result = null, OpponentClass = "DRUID" },   // unfinished: ignored
            };
            var s = StatsSummary.From(games);
            Assert.Equal(1, s.Wins); Assert.Equal(2, s.Losses);
            Assert.Equal(1, s.First.Wins); Assert.Equal(2, s.Coin.Losses);
            Assert.Equal("Priest", s.ByClass[0].Name); Assert.Equal(2, s.ByClass[0].Games);
            Assert.Equal(new[] { true, false, false }, s.Recent);
        }

        [Fact]
        public void Each_deck_gets_its_own_signature_card()
        {
            var decks = DeckPlanner.FromCodes(DecksLog.SavedDecks(TestData.InstallDir), TestData.Cards);
            Assert.True(decks.Count >= 3);
            var planner = new DeckPlanner(TestData.Meta, TestData.Cards, null, _ => { });
            var picks = planner.PickDecks(decks);
            Assert.All(picks, p => Assert.NotNull(p.SignatureCardId));
            Assert.Equal(picks.Count, picks.Select(p => TestData.Cards.NameOf(p.SignatureCardId)).Distinct().Count());
            // Best first; matched decks have a matchup for every class in the field, adding up to the whole field.
            var matched = picks.Where(p => p.Archetype != null).ToList();
            Assert.Equal(matched.OrderByDescending(p => p.Expected).Select(p => p.Deck.Name), matched.Select(p => p.Deck.Name));
            Assert.All(matched, p => Assert.InRange(p.Matchups.Sum(m => m.Share), 0.99, 1.01));
        }
    }
}
