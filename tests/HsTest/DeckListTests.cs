using System.IO;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>The deck list the overlay draws, checked against a real game replayed to turn 10.</summary>
    public class DeckListTests
    {
        [Fact]
        public void Deck_list_matches_the_game_at_turn_10()
        {
            var logs = Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*")
                .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).Where(f => new FileInfo(f).Length > 20000).ToList();
            Assert.NotEmpty(logs);
            var checkedOne = false;
            foreach (var log in logs)
            foreach (var game in GameLog.Split(GameLog.ReadShared(log), GameLog.SessionStart(log), log))
            {
                var queued = DecksLog.LastQueuedDeck(log, game.Start);
                var deck = queued == null ? null : DeckCode.Decode(queued.DeckCode, TestData.Cards);
                if (deck == null) continue;
                GameTracker tracker = null;
                tracker = new GameTracker(TestData.Cards, ev =>
                {
                    if (ev.Kind == GameEventKind.GameStart) { tracker.FriendlyDeckList = deck.ToList(); tracker.FriendlyDeckName = queued.Name; }
                });
                var advisor = new Advisor(tracker, TestData.Cards, null, new Settings());
                foreach (var line in game.Lines)
                {
                    tracker.Feed(line);
                    if (tracker.InGame && tracker.Turn >= 10) break;
                }
                if (!tracker.InGame || !GameModes.IsCardGame(tracker.GameType)) continue;

                var sec = advisor.Compute().Sections.Single(s => s.Lower && !s.Opponent);
                var left = tracker.DeckCount(tracker.FriendlyPlayerId);
                Assert.Equal(queued.Name, sec.Title);
                Assert.Equal($"{left} left", sec.RightText);

                var rows = sec.Lines.Where(l => l.CardId != null).ToList();
                var distinct = deck.Distinct().ToList();
                Assert.True(rows.Count >= distinct.Count, $"{rows.Count} rows for {distinct.Count} distinct cards");
                var missing = distinct.Where(id => !rows.Any(r => r.CardId == id)).ToList();
                Assert.True(missing.Count == 0, $"{log}: deck '{queued.Name}' rows lack {string.Join(", ", missing.Select(TestData.Cards.NameOf))}");
                // Sorted by cost like the in-game list.
                var costs = rows.Take(distinct.Count).Select(r => r.Cost ?? 99).ToList();
                Assert.Equal(costs.OrderBy(c => c), costs);
                // Drawn-out cards are shaded and show no count or odds.
                Assert.All(rows.Where(r => r.Struck), r => { Assert.Equal("0", r.Badge); Assert.Null(r.Detail); });
                // As many shaded rows as cards with no copies left (there may be none by turn 10).
                var remaining = tracker.FriendlyRemainingDeck();
                Assert.Equal(distinct.Count(id => !remaining.TryGetValue(id, out var n) || n == 0), rows.Count(r => r.Struck));
                // Next-draw odds of the cards still in the deck add up to (at most) 100%.
                Assert.InRange(remaining.Values.Sum() / (double)left, 0.0, 1.0001);
                checkedOne = true;
            }
            Assert.True(checkedOne, "no game reached turn 10 with a known deck");
        }

        /// <summary>At every turn of every saved game, the cards we think are left never outnumber the real deck.</summary>
        [Fact]
        public void Known_remaining_cards_never_exceed_the_deck()
        {
            var problems = new System.Collections.Generic.List<string>();
            var turnsChecked = 0;
            foreach (var log in Directory.GetDirectories(Path.Combine(TestData.InstallDir, "Logs"), "Hearthstone_*")
                         .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).Where(f => new FileInfo(f).Length > 20000))
            foreach (var game in GameLog.Split(GameLog.ReadShared(log), GameLog.SessionStart(log), log))
            {
                var queued = DecksLog.LastQueuedDeck(log, game.Start);
                var deck = queued == null ? null : DeckCode.Decode(queued.DeckCode, TestData.Cards);
                if (deck == null) continue;
                GameTracker t = null;
                t = new GameTracker(TestData.Cards, ev =>
                {
                    if (ev.Kind == GameEventKind.GameStart) t.FriendlyDeckList = deck.ToList();
                    if (ev.Kind != GameEventKind.Turn || t.FriendlyPlayerId == 0 || t.FriendlyDeckList == null) return;
                    turnsChecked++;
                    var known = t.FriendlyRemainingDeck().Values.Sum();
                    var left = t.DeckCount(t.FriendlyPlayerId);
                    if (known > left) problems.Add($"{Path.GetFileName(Path.GetDirectoryName(log))} turn {t.Turn}: {known} known > {left} in deck");
                });
                foreach (var line in game.Lines) t.Feed(line);
            }
            Assert.True(turnsChecked > 40, $"only {turnsChecked} turns checked");
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        /// <summary>A session with two games on different decks: each game gets the deck queued just before it.</summary>
        [Fact]
        public void Each_game_gets_the_deck_queued_before_it()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hs-decks-" + System.Guid.NewGuid().ToString("N"), "Hearthstone_2026_11_04_23_00_00");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllLines(Path.Combine(dir, "Decks.log"), new[]
                {
                    "I 23:10:00.1 Finding Game With Deck:", "I 23:10:00.1 ### Aura Paladin", "I 23:10:00.1 # Deck ID: 1", "I 23:10:00.1 AAEaura",
                    "I 00:20:00.1 Finding Game With Deck:", "I 00:20:00.1 ### Brawl Deck", "I 00:20:00.1 # Deck ID: 2", "I 00:20:00.1 AAEbrawl",
                });
                var log = Path.Combine(dir, "Power.log");
                Assert.Equal("Brawl Deck", DecksLog.LastQueuedDeck(log).Name);
                Assert.Equal("Aura Paladin", DecksLog.LastQueuedDeck(log, new System.DateTime(2026, 11, 4, 23, 11, 0)).Name);
                Assert.Equal("Aura Paladin", DecksLog.LastQueuedDeck(log, new System.DateTime(2026, 11, 5, 0, 19, 0)).Name);   // past midnight
                Assert.Equal("Brawl Deck", DecksLog.LastQueuedDeck(log, new System.DateTime(2026, 11, 5, 0, 21, 0)).Name);
                Assert.Null(DecksLog.LastQueuedDeck(log, new System.DateTime(2026, 11, 4, 23, 5, 0)));
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(dir), true); } catch { } }
        }
    }
}