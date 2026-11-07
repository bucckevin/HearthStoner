using System.IO;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>The opponent hand marks, checked at the start of every turn of every saved game.</summary>
    public class HandMarkTests
    {
        [Fact]
        public void Marks_match_the_opponents_hand_every_turn()
        {
            int turns = 0, coins = 0, kept = 0, drawn = 0;
            foreach (var log in Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*")
                         .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).Where(f => new FileInfo(f).Length > 20000))
            {
                GameTracker t = null;
                Advisor a = null;
                t = new GameTracker(TestData.Cards, ev =>
                {
                    if (ev.Kind != GameEventKind.Turn || t.OpponentPlayerId == 0 || t.InMulligan) return;
                    var hand = t.Hand(t.OpponentPlayerId);
                    var marks = a.OpponentHandMarks();
                    Assert.Equal(hand.Count, marks.Count);
                    // One mark per slot, slots 0..n-1 in order.
                    Assert.Equal(Enumerable.Range(0, marks.Count), marks.Select(m => m.Slot));
                    // A card can't have arrived after the current turn.
                    Assert.All(marks, m => Assert.InRange(m.Turn, 0, (t.Turn + 1) / 2));
                    // Hidden cards that were simply drawn are never named (no peeking).
                    Assert.All(marks.Where(m => m.Kind == HandMarkKind.Drawn), m =>
                        Assert.True(m.CardId == null || !string.IsNullOrEmpty(hand[m.Slot].CardId), "named a hidden card"));
                    coins += marks.Count(m => m.Kind == HandMarkKind.Coin);
                    kept += marks.Count(m => m.Kind == HandMarkKind.Kept);
                    drawn += marks.Count(m => m.Kind == HandMarkKind.Drawn && m.Turn > 0);
                    turns++;
                });
                a = new Advisor(t, TestData.Cards, null, new Settings());
                foreach (var line in GameLog.ReadShared(log)) t.Feed(line);
            }
            Assert.True(turns > 40, $"{turns} turns checked");
            Assert.True(kept > 0 && drawn > 0, $"kept {kept}, drawn {drawn}");
            Assert.True(coins > 0, "never saw the opponent hold the Coin");
        }
    }
}

namespace HearthStoner.Tests
{
    public class OpponentPlayedTests
    {
        /// <summary>Every card the opponent played is named once the game is over (cards played are public).</summary>
        [Fact]
        public void Opponent_plays_are_named()
        {
            int plays = 0, named = 0, rows = 0;
            foreach (var log in Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*")
                         .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).Where(f => new FileInfo(f).Length > 20000))
            {
                GameTracker t = null;
                Advisor a = null;
                t = new GameTracker(TestData.Cards, ev =>
                {
                    if (ev.Kind != GameEventKind.Turn || t.OpponentPlayerId == 0) return;
                    var secs = a.Compute().Sections;
                    var played = secs.FirstOrDefault(s => s.Lower && s.Opponent);
                    if (played != null) rows += played.Lines.Count(l => l.CardId != null);
                    // Deck panels: a header bar with cards left, then cards only (no next-draw line).
                    foreach (var d in secs.Where(s => s.Lower))
                        Assert.True(d.Bar && d.RightText.EndsWith(" left") && !d.Lines.Any(l => l.Text.StartsWith("Next draw")), $"deck panel '{d.Title}'");
                    Assert.True(secs.Count(s => s.Lower && s.Opponent) <= 1 && secs.Count(s => s.Lower && !s.Opponent) <= 1, "one deck panel per side");
                });
                a = new Advisor(t, TestData.Cards, null, new Settings());
                foreach (var line in GameLog.ReadShared(log))
                {
                    t.Feed(line);
                    if (line.Contains("tag=STATE value=COMPLETE") && t.OpponentPlayerId != 0)
                    {
                        plays += t.OpponentPlayLog.Count;
                        named += t.OpponentPlayLog.Count(p => !string.IsNullOrEmpty(p.CardId));
                    }
                }
            }
            Assert.True(plays > 20, $"{plays} plays");
            Assert.True(named == plays, $"{named} of {plays} opponent plays named");
            Assert.True(rows > 0, "the Opponent played panel never listed a card");
        }
    }
}

namespace HearthStoner.Tests
{
    public class PlayerNameTests
    {
        /// <summary>The health strips show both players' names (no BattleTag number) once the game reveals them.</summary>
        [Fact]
        public void Health_strips_use_player_names()
        {
            string[] titles = null;
            string me = null, them = null;
            // The newest log with a game that reached turn 6 (the newest may hold one just started).
            foreach (var log in Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*")
                         .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).Where(f => new FileInfo(f).Length > 20000).OrderByDescending(f => f))
            {
                GameTracker t = null;
                Advisor a = null;
                t = new GameTracker(TestData.Cards, ev =>
                {
                    if (ev.Kind != GameEventKind.Turn || t.Turn != 6 || titles != null) return;
                    titles = a.Compute().Sections.Select(s => s.Title).Where(x => x.Contains(" HP")).ToArray();
                    me = t.DisplayName(t.FriendlyPlayerId);
                    them = t.DisplayName(t.OpponentPlayerId);
                });
                a = new Advisor(t, TestData.Cards, null, new Settings());
                foreach (var line in GameLog.ReadShared(log)) t.Feed(line);
                if (titles != null) break;
            }
            Assert.NotNull(titles);
            Assert.Equal("Musume", me);
            Assert.False(string.IsNullOrEmpty(them), "the opponent's name was never revealed");
            Assert.Contains(titles, x => x.StartsWith(me + ": "));
            Assert.Contains(titles, x => x.StartsWith(them + ": "));
            Assert.DoesNotContain(titles, x => x.Contains("#") || x.StartsWith("You:") || x.StartsWith("Opponent:"));
        }
    }
}
