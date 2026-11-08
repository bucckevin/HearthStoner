using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>
    /// Every game saved with its log is a regression test: replaying the log through today's tracker must give the
    /// same result, length, classes and turn-by-turn health as when it was recorded, and a replay that obeys the
    /// game's rules (hand, board and mana limits). A Hearthstone patch that changes the log shows up here as soon as
    /// you've played a game with it. The test set grows with every game you play.
    /// </summary>
    [Trait("Category", TestData.MyGames)]
    public class SavedGameTests
    {
        public static IEnumerable<object[]> Games()
        {
            _ = TestData.Cards;   // points %LocalAppData% at tests/data on Linux before the games folder is read
            var dir = GameRecord.Dir;
            var any = false;
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
            {
                GameRecord r;
                try { r = JsonConvert.DeserializeObject<GameRecord>(File.ReadAllText(f)); } catch { continue; }
                if (r == null || string.IsNullOrEmpty(r.LogFile) || !File.Exists(Path.Combine(dir, r.LogFile))) continue;
                // Battlegrounds and Mercenaries games keep only their basics: nothing to replay or review.
                if (!GameModes.IsCardGame(r.GameType ?? GameLog.ModeOf(GameLog.Load(r)).GameType)) continue;
                any = true;
                yield return new object[] { Path.GetFileNameWithoutExtension(f) };
            }
            if (!any) yield return new object[] { "" };
        }

        private static GameRecord Load(string stem) =>
            JsonConvert.DeserializeObject<GameRecord>(File.ReadAllText(Path.Combine(GameRecord.Dir, stem + ".json")));

        [Theory]
        [MemberData(nameof(Games))]
        public void Recording_again_gives_the_same_game(string stem)
        {
            if (stem == "") return;   // no saved games on this machine
            var saved = Load(stem);
            var lines = GameLog.Load(saved);
            Assert.NotNull(lines);
            Assert.NotEmpty(lines);
            var again = GameLog.Record(new GameLog.Slice { Lines = lines, Start = saved.Start, Complete = true }, null, TestData.Cards, TestData.Meta);
            Assert.NotNull(again);
            Assert.Equal(saved.Result, again.Result);
            Assert.Equal(saved.Turns, again.Turns);
            // A class saved blank or "NEUTRAL" may be found now (1.4.1 looks behind neutral hero cards and skins); a
            // class that was found must stay the same.
            void SameClass(string was, string now, string who)
            {
                if (HeroClasses.IsReal(was) || !HeroClasses.IsReal(now)) Assert.True(was == now || (string.IsNullOrEmpty(was) && string.IsNullOrEmpty(now)), $"{who}: saved '{was}', now '{now}'");
            }
            SameClass(saved.MyClass, again.MyClass, "your class");
            SameClass(saved.OpponentClass, again.OpponentClass, "opponent's class");
            Assert.Equal(saved.WentFirst, again.WentFirst);
            Assert.Equal(saved.Snapshots.Select(t => (t.Turn, t.Yours)), again.Snapshots.Select(t => (t.Turn, t.Yours)));
            Assert.Equal(saved.Snapshots.Select(t => (t.MyHp, t.MyArmor, t.TheirHp, t.TheirArmor)), again.Snapshots.Select(t => (t.MyHp, t.MyArmor, t.TheirHp, t.TheirArmor)));
            Assert.Equal(saved.Snapshots.Select(t => (t.MyMinions, t.TheirMinions)), again.Snapshots.Select(t => (t.MyMinions, t.TheirMinions)));
        }

        [Theory]
        [MemberData(nameof(Games))]
        public void Replay_follows_the_rules(string stem)
        {
            if (stem == "") return;
            var saved = Load(stem);
            var r = Replay.Build(GameLog.Load(saved), TestData.Cards, TestData.Meta, saved.DeckCards, saved.DeckName);
            Assert.NotEmpty(r.Frames);
            Assert.Equal(saved.Result, r.Result);
            Assert.Equal(saved.Turns, r.Turns);
            var lastTurn = 0;
            foreach (var f in r.Frames)
            {
                Assert.True(f.Turn >= lastTurn, $"frame {f.Index}: turn went back from {lastTurn} to {f.Turn}");
                lastTurn = f.Turn;
                foreach (var s in new[] { f.You, f.Them })
                {
                    Assert.InRange(s.Hand.Count, 0, 10);
                    Assert.InRange(s.Board.Count, 0, 7);
                    Assert.InRange(s.Crystals, 0, 20);   // the cap is MAXRESOURCES: 10 for years, 15 this season
                    Assert.InRange(s.Mana, 0, 30);
                    Assert.InRange(s.DeckCount, 0, 80);
                    Assert.True(s.Health <= 100, $"frame {f.Index}: {s.Health} health");
                    Assert.True(s.Board.All(m => m.Attack >= 0), $"frame {f.Index}: negative attack");
                }
                Assert.NotNull(f.Action);
            }
            // Your own hand is always visible to you.
            Assert.All(r.Frames.Where(f => !f.Mulligan), f => Assert.All(f.You.Hand, c => Assert.False(string.IsNullOrEmpty(c.CardId), $"frame {f.Index}: hidden card in your hand")));
        }

        [Theory]
        [MemberData(nameof(Games))]
        public void Win_chance_and_review_work(string stem)
        {
            if (stem == "") return;
            var saved = Load(stem);
            var r = Replay.Build(GameLog.Load(saved), TestData.Cards, TestData.Meta, saved.DeckCards, saved.DeckName);
            var wc = WinChance.For(r, WinChance.MatchupPrior(saved, TestData.Meta, TestData.Cards));
            Assert.Equal(r.Frames.Count, wc.Values.Count);
            Assert.All(wc.Values, v => Assert.InRange(v, 0.0, 1.0));
            if (saved.Result == "WON") Assert.Equal(1.0, wc.Values.Last());
            if (saved.Result == "LOST") Assert.Equal(0.0, wc.Values.Last());
            Assert.All(wc.Drops, m => Assert.True(m.Drop > 0));
            var notes = GameReview.Analyze(r, TestData.Cards, wc);
            Assert.All(notes, n => Assert.InRange(n.Frame, 0, r.Frames.Count - 1));
            Assert.All(notes, n => Assert.False(string.IsNullOrWhiteSpace(n.Text)));
            var merged = GameReview.Merge(saved, notes);
            Assert.True(merged.Count >= saved.Review.Count);
        }
    }
}
