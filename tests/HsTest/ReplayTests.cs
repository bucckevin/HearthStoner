using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace HearthStoner.Tests
{
    [Trait("Category", TestData.MyGames)]
    public class ReplayTests
    {
        private readonly ITestOutputHelper _out;
        public ReplayTests(ITestOutputHelper o) => _out = o;

        private static Replay Build(string folder)
        {
            var path = Path.Combine(TestData.InstallDir, "Logs", folder, "Power.log");
            if (!File.Exists(path)) return null;   // Hearthstone deletes old log folders (on a real install)
            var slice = GameLog.Split(GameLog.ReadShared(path), GameLog.SessionStart(path), path).Last(s => s.Complete);
            var q = DecksLog.LastQueuedDeck(path, slice.Start);
            return Replay.Build(slice.Lines, TestData.Cards, TestData.Meta, DeckCode.Decode(q.DeckCode, TestData.Cards), q.Name);
        }

        [Fact]
        public void Replays_a_whole_game_frame_by_frame()
        {
            var r = Build("Hearthstone_2026_10_30_02_18_32");   // Aura Paladin vs Priest, lost in 23 turns
            if (r == null) return;
            _out.WriteLine($"{r.Frames.Count} frames");
            foreach (var f in r.Frames.Take(40)) _out.WriteLine($"T{f.Round} {(f.YourTurn ? "you " : "them")} {f.You.Health}/{f.Them.Health} hand {f.You.Hand.Count}/{f.Them.Hand.Count} board {f.You.Board.Count}/{f.Them.Board.Count}: {f.Action}");
            Assert.Equal("LOST", r.Result);
            Assert.InRange(r.Frames.Count, 60, 3000);
            // Every turn has a start frame and advice; turns only move forward.
            Assert.True(r.TurnStarts.Count >= r.Turns - 1, $"{r.TurnStarts.Count} turn starts for {r.Turns} turns");
            Assert.True(r.AdviceByTurn.Count >= r.Turns - 1);
            Assert.Equal(r.Frames.Select(f => f.Turn).OrderBy(x => x), r.Frames.Select(f => f.Turn));
            // Names, heroes and sensible numbers on every frame.
            Assert.All(r.Frames, f =>
            {
                Assert.True(f.You.Name == "Musume" || (f.Mulligan && f.You.Name == null), $"frame {f.Index} T{f.Turn} mulligan={f.Mulligan}: name {f.You.Name ?? "null"}, action {f.Action}");
                Assert.NotNull(f.You.HeroCardId);
                Assert.InRange(f.You.Board.Count, 0, 7);
                Assert.InRange(f.Them.Board.Count, 0, 7);
                Assert.InRange(f.You.Hand.Count, 0, 10);
                Assert.InRange(f.Them.Hand.Count, 0, 10);
                Assert.All(f.You.Hand, h => Assert.NotNull(h.CardId));   // you see your own hand
            });
            // Actions name the cards (revealed by the time the action ends).
            Assert.DoesNotContain(r.Frames, f => f.Action != null && f.Action.Contains("unknown card"));
            Assert.All(r.Frames, f => Assert.False(string.IsNullOrWhiteSpace(f.Action), $"frame {f.Index} has no description"));
            Assert.Contains(r.Frames, f => f.Action != null && f.Action.StartsWith("Opponent played "));
            Assert.Contains(r.Frames, f => f.Action != null && f.Action.Contains(" attacked "));
            // Opponent hand cards are hidden in frames, but some were revealed by the end.
            var hidden = r.Frames.SelectMany(f => f.Them.Hand).Where(h => h.CardId == null).Select(h => h.EntityId).Distinct().ToList();
            Assert.NotEmpty(hidden);
            Assert.Contains(hidden, id => r.FinalCardIds.ContainsKey(id));
            // Played cards show up as actions.
            Assert.Contains(r.Frames, f => f.Action != null && f.Action.Contains("played"));
        }

        [Fact]
        public void Every_saved_game_replays()
        {
            foreach (var dir in Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*"))
            {
                var path = Path.Combine(dir, "Power.log");
                if (!File.Exists(path) || new FileInfo(path).Length < 20000) continue;
                foreach (var slice in GameLog.Split(GameLog.ReadShared(path), GameLog.SessionStart(path), path).Where(s => s.Complete && GameModes.IsCardGame(GameLog.ModeOf(s.Lines).GameType)))
                {
                    var r = Replay.Build(slice.Lines, TestData.Cards, null);
                    if (r.Turns < 3) continue;   // conceded in the first turns: next to nothing to replay
                    Assert.True(r.Frames.Count > 20, $"{dir}: {r.Frames.Count} frames");
                    Assert.True(r.Result == "WON" || r.Result == "LOST", $"{dir}: {r.Result}");
                    // Once a hero is at 0 health for good, the game ends within a few frames (it can also end by concession).
                    // A hero can drop to 0 and come back (effects that save or restore it), so only the last drop counts.
                    var dead = r.Frames.Select(f => f.You.Health <= 0 || f.Them.Health <= 0).ToList();
                    var firstDead = dead.LastIndexOf(false) + 1;
                    if (firstDead >= dead.Count) firstDead = -1;
                    Assert.True(firstDead < 0 || firstDead >= r.Frames.Count - 6, $"{dir}: a hero at 0 health at frame {firstDead} of {r.Frames.Count}: " +
                        string.Join(" | ", r.Frames.Skip(System.Math.Max(0, firstDead - 2)).Take(5).Select(f => $"{f.Index} {f.Action} (you {f.You.Health}+{f.You.Armor}, them {f.Them.Health}+{f.Them.Armor})")));
                    Assert.All(r.Frames, f => Assert.True(char.IsUpper(f.Action[0]), $"{dir}: frame {f.Index} '{f.Action}'"));
                }
            }
        }
    }
}
