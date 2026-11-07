using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>Win chance, the replay review, mana stats, deck versions and meta-change alerts, on made-up data.</summary>
    public class AnalysisTests
    {
        // ------------------------------------------------------------------ helpers

        private static Replay.Side Side(int hp = 30, int armor = 0, int crystals = 5, int mana = 0, int hand = 4, params (int Atk, int Hp)[] board) =>
            new Replay.Side
            {
                Health = hp, Armor = armor, Crystals = crystals, Mana = mana, DeckCount = 15,
                Hand = Enumerable.Range(0, hand).Select(i => new Replay.HandCard { EntityId = 900 + i, CardId = "X" + i, Cost = 3 }).ToList(),
                Board = board.Select((m, i) => new Replay.Minion { EntityId = 100 + i, CardId = "M" + i, Attack = m.Atk, Health = m.Hp, MaxHealth = m.Hp }).ToList(),
            };

        private static Replay.Frame Frame(int turn, bool yours, Replay.Side you, Replay.Side them) =>
            new Replay.Frame { Turn = turn, YourTurn = yours, You = you, Them = them, Action = "" };

        // ------------------------------------------------------------------ win chance

        [Fact]
        public void Even_board_is_the_matchup_prior()
        {
            var f = Frame(9, true, Side(), Side());
            Assert.InRange(WinChance.Estimate(f, 0.5), 0.45, 0.55);
            Assert.True(WinChance.Estimate(f, 0.6) > WinChance.Estimate(f, 0.5));
        }

        [Fact]
        public void Win_chance_moves_the_right_way()
        {
            var even = WinChance.Estimate(Frame(9, true, Side(), Side()));
            Assert.True(WinChance.Estimate(Frame(9, true, Side(), Side(hp: 15))) > even, "they have less health");
            Assert.True(WinChance.Estimate(Frame(9, true, Side(board: new[] { (4, 4), (3, 3) }), Side())) > even, "you have a board");
            Assert.True(WinChance.Estimate(Frame(9, true, Side(hand: 1), Side(hand: 7))) < even, "they have more cards");
            Assert.True(WinChance.Estimate(Frame(9, true, Side(armor: 10), Side())) > even, "armor counts");
        }

        [Fact]
        public void Lethal_on_board_dominates()
        {
            var theirLethal = Frame(10, false, Side(hp: 6), Side(board: new[] { (4, 2), (3, 2) }));
            Assert.True(WinChance.Estimate(theirLethal) < 0.2);
            var yourLethal = Frame(9, true, Side(board: new[] { (5, 5), (5, 5) }), Side(hp: 8));
            Assert.True(WinChance.Estimate(yourLethal) > 0.8);
            // A taunt in the way means it isn't lethal yet.
            var taunt = Side(hp: 6); taunt.Board.Add(new Replay.Minion { EntityId = 50, Attack = 1, Health = 8, Taunt = true });
            Assert.True(WinChance.Estimate(Frame(10, false, taunt, Side(board: new[] { (4, 2), (3, 2) }))) > WinChance.Estimate(theirLethal));
        }

        [Fact]
        public void Drops_are_found_once_per_round()
        {
            var r = new Replay { Result = "LOST" };
            r.Frames.Add(Frame(1, true, Side(), Side()));
            r.Frames.Add(Frame(2, false, Side(), Side(board: new[] { (6, 6), (6, 6) })));   // their big turn
            r.Frames.Add(Frame(2, false, Side(hp: 20), Side(board: new[] { (6, 6), (6, 6) })));
            r.Frames.Add(Frame(3, true, Side(hp: 20), Side(board: new[] { (6, 6), (6, 6) })));
            r.Frames.Add(Frame(4, false, Side(hp: 0), Side(board: new[] { (6, 6), (6, 6) })));
            for (var i = 0; i < r.Frames.Count; i++) r.Frames[i].Index = i;
            var wc = WinChance.For(r);
            Assert.Equal(0.0, wc.Values.Last());
            Assert.NotEmpty(wc.Drops);
            Assert.Equal(1, wc.Drops[0].Frame);   // the turn their board landed, not the final frame
            Assert.Equal(wc.Drops.Count, wc.Drops.Select(d => d.Round).Distinct().Count());
        }

        // ------------------------------------------------------------------ review

        [Fact]
        public void Review_finds_a_trade_that_lost_a_minion()
        {
            var r = new Replay { Result = "WON" };
            var before = Frame(5, true, Side(board: new[] { (2, 2) }), Side(board: new[] { (1, 5) }));
            var after = Frame(5, true, Side(), Side(board: new[] { (1, 3) }));
            after.AttackerId = 100; after.TargetId = 100; after.Action = "M0 attacked M0";
            r.Frames.Add(before); r.Frames.Add(after);
            r.TurnStarts[5] = 0;
            var notes = GameReview.Analyze(r, TestData.Cards);
            var trade = Assert.Single(notes, n => n.Kind == "BadTrade");
            Assert.Equal(1, trade.Frame);
        }

        [Fact]
        public void Review_skips_a_trade_finished_off_later_in_the_turn()
        {
            var r = new Replay { Result = "WON" };
            var before = Frame(5, true, Side(board: new[] { (2, 2), (3, 3) }), Side(board: new[] { (1, 5) }));
            var after = Frame(5, true, Side(board: new[] { (3, 3) }), Side(board: new[] { (1, 3) }));
            after.AttackerId = 100; after.TargetId = 100;
            var end = Frame(5, true, Side(board: new[] { (3, 3) }), Side());
            after.You.Board[0].EntityId = 101; end.You.Board[0].EntityId = 101;   // the 3/3 that survived and finished the job
            r.Frames.AddRange(new[] { before, after, end });
            Assert.DoesNotContain(GameReview.Analyze(r, TestData.Cards), n => n.Kind == "BadTrade");
        }

        [Fact]
        public void Review_finds_a_card_held_too_long()
        {
            var r = new Replay { Result = "LOST" };
            var card = new Replay.HandCard { EntityId = 77, CardId = "CS2_029", Cost = 4 };
            for (var t = 7; t <= 15; t += 2)
            {
                var you = Side(crystals: (t + 1) / 2, mana: 4, hand: 0);
                you.Hand.Add(card);
                r.TurnStarts[t] = r.Frames.Count;
                r.Frames.Add(Frame(t, true, you, Side()));
                r.TurnStarts[t + 1] = r.Frames.Count;
                r.Frames.Add(Frame(t + 1, false, you, Side()));
            }
            Assert.Contains(GameReview.Analyze(r, TestData.Cards), n => n.Kind == "HeldCard");
        }

        [Fact]
        public void Merge_does_not_repeat_a_turn()
        {
            var g = new GameRecord { Review = { new ReviewNote { Turn = 5, Kind = "UnspentMana", Text = "a", Severity = 2 } } };
            var merged = GameReview.Merge(g, new List<ReviewNote>
            {
                new ReviewNote { Turn = 5, Kind = "HeroPower", Text = "b", Severity = 1 },
                new ReviewNote { Turn = 7, Kind = "BadTrade", Text = "c", Severity = 1 },
            });
            Assert.Equal(new[] { "UnspentMana", "BadTrade" }, merged.Select(n => n.Kind));
        }

        // ------------------------------------------------------------------ mana

        [Fact]
        public void Mana_stats_add_up()
        {
            GameRecord Game(string deck, params (int Turn, int Avail, int Left)[] turns)
            {
                var g = new GameRecord { DeckName = deck, MyClass = "MAGE", Result = "WON" };
                foreach (var t in turns) g.Snapshots.Add(new TurnSnapshot { Turn = t.Turn, Yours = true, ManaAvailable = t.Avail, ManaUnspent = t.Left });
                g.Snapshots.Add(new TurnSnapshot { Turn = 99, Yours = false });   // the game's last snapshot is left out
                return g;
            }
            var s = ManaStats.From(new List<GameRecord>
            {
                Game("A", (1, 1, 0), (3, 2, 0), (5, 3, 2)),
                Game("B", (1, 1, 1), (3, 2, 0), (5, 3, 2)),
            });
            Assert.Equal(6, s.Turns);
            Assert.Equal(7.0 / 12, s.UsedRate, 3);
            Assert.Equal(3.0 / 6, s.CurveRate, 3);
            Assert.Equal(3, s.StallRound.Round);
            Assert.Equal(2, s.ByDeck.Count);
        }

        // ------------------------------------------------------------------ deck versions

        [Fact]
        public void Deck_versions_track_changes_and_results()
        {
            var v1 = Enumerable.Repeat("A", 2).Concat(Enumerable.Range(0, 28).Select(i => "C" + i)).ToList();
            var v2 = v1.Take(29).Append("NEW").ToList();
            var deck = new PlayerDeck { Name = "My Deck", Class = "PALADIN", Cards = v2.Take(29).Append("NEWER").ToList() };
            var other = new PlayerDeck { Name = "Other", Class = "PALADIN", Cards = Enumerable.Range(0, 30).Select(i => "Z" + i).ToList() };
            GameRecord G(List<string> list, bool won, int hour, string name = "My Deck") =>
                new GameRecord { DeckName = name, MyClass = "PALADIN", DeckCards = list, Result = won ? "WON" : "LOST", Start = new DateTime(2026, 10, 30, hour, 0, 0) };
            var games = new List<GameRecord> { G(v1, true, 1), G(v1, false, 2), G(v2, true, 3), G(v2, true, 4, name: "Renamed"), G(other.Cards, true, 5, name: "Other") };
            var vs = DeckVersions.For(deck, new List<PlayerDeck> { deck, other }, games);
            Assert.Equal(3, vs.Versions.Count);
            Assert.Equal((1, 1), (vs.Versions[0].Wins, vs.Versions[0].Losses));
            Assert.Equal((2, 0), (vs.Versions[1].Wins, vs.Versions[1].Losses));   // the renamed game still counts (same cards)
            Assert.Equal(1, vs.Versions[1].Added["NEW"]);
            Assert.Equal(1, vs.Versions[1].Removed["C27"]);
            Assert.True(vs.Versions[2].IsCurrent);
            Assert.Equal(0, vs.Versions[2].Games);
            Assert.Equal(1, vs.Versions[2].Added["NEWER"]);
        }

        [Fact]
        public void Copies_joining_the_deck_are_not_a_new_version()
        {
            // Commander Beatrix adds ten copies of her pick when the game starts; older records were saved without them.
            var beatrix = TestData.Cards.All.FirstOrDefault(c => DeckCode.CopiesJoining(c.Text) == 10);
            Assert.NotNull(beatrix);
            var built = Enumerable.Range(0, 29).Select(i => "C" + i).Append(beatrix.Id).ToList();
            var started = built.Concat(Enumerable.Repeat("JAIL_328", 10)).ToList();
            Assert.Equal(DeckVersions.Key(built), DeckVersions.Key(DeckVersions.Built(started, TestData.Cards)));
            var deck = new PlayerDeck { Name = "Aura", Class = "PALADIN", Cards = started };
            GameRecord G(List<string> list) => new GameRecord { DeckName = "Aura", MyClass = "PALADIN", DeckCards = list, Result = "WON", Start = DateTime.Today };
            var vs = DeckVersions.For(deck, new List<PlayerDeck> { deck }, new List<GameRecord> { G(built), G(started) }, TestData.Cards);
            Assert.Single(vs.Versions);
            Assert.Equal(2, vs.Versions[0].Wins);
        }

        // ------------------------------------------------------------------ meta history

        private static MetaHistory.Snapshot Snap(DateTime day, double deck, params (string Key, double Share, int Place)[] arch)
        {
            var s = new MetaHistory.Snapshot { Day = day, Rank = "legend", Period = "past-7" };
            s.Decks["Aura"] = deck;
            foreach (var a in arch) s.Archetypes[a.Key] = new MetaHistory.Arch { Name = a.Key, Share = a.Share, Place = a.Place };
            return s;
        }

        [Fact]
        public void Meta_alerts_point_out_real_changes_only()
        {
            var day = new DateTime(2026, 10, 30);
            var before = Snap(day.AddDays(-4), 0.50, ("dh", 0.06, 6), ("priest", 0.10, 1), ("tiny", 0.012, 12), ("gone", 0.05, 7));
            var now = Snap(day, 0.48, ("dh", 0.09, 3), ("priest", 0.105, 1), ("tiny", 0.013, 11), ("fresh", 0.03, 8));
            var alerts = MetaHistory.Compare(before, now, 10);
            Assert.True(alerts[0].AboutYourDeck);
            Assert.Contains("down from 50.0", alerts[0].Text);
            Assert.Contains(alerts, a => a.Text.StartsWith("dh:") && a.Text.Contains("#3"));
            Assert.Contains(alerts, a => a.Text.StartsWith("fresh is new"));
            Assert.Contains(alerts, a => a.Text.StartsWith("gone has mostly left"));
            Assert.DoesNotContain(alerts, a => a.Text.StartsWith("priest") || a.Text.StartsWith("tiny"));
        }

        [Fact]
        public void Meta_baseline_is_a_few_days_back()
        {
            var day = new DateTime(2026, 10, 30);
            var h = new MetaHistory();
            foreach (var back in new[] { 10, 5, 3, 1 }) h.Add(Snap(day.AddDays(-back), 0.5));
            var now = Snap(day, 0.5);
            Assert.Equal(day.AddDays(-3), h.Baseline(now).Day);
            var fresh = new MetaHistory();
            fresh.Add(Snap(day.AddDays(-1), 0.5));
            Assert.Equal(day.AddDays(-1), fresh.Baseline(now).Day);   // only yesterday: use it
            Assert.Null(new MetaHistory().Baseline(now));
            h.Add(Snap(day, 0.5)); h.Add(Snap(day, 0.51));
            Assert.Single(h.Snapshots, s => s.Day == day);   // one per day
        }

        [Theory]
        [InlineData(8, "this morning")]
        [InlineData(15, "this afternoon")]
        [InlineData(19, "this evening")]
        [InlineData(23, "tonight")]
        [InlineData(2, "tonight")]
        public void Play_page_title_follows_the_clock(int hour, string expected) =>
            Assert.Equal(expected, MetaStats.PartOfDay(new DateTime(2026, 10, 30, hour, 0, 0)));
    }
}
