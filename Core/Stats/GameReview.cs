using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    /// <summary>
    /// A review of a game worked out from its replay, on top of the notes taken live (missed lethal, unspent mana,
    /// mulligan, Discover, overextending): cards held too long, the hero power left unused, trades that lost a minion
    /// for nothing, going face when their board threatened lethal, and where the win chance dropped. Each note
    /// points at the replay frame where it happened.
    /// </summary>
    public static class GameReview
    {
        public static List<ReviewNote> Analyze(Replay r, CardDb cards, WinChance chance = null)
        {
            var notes = new List<ReviewNote>();
            if (r == null || r.Frames.Count == 0) return notes;
            var myTurns = r.Frames.Where(f => f.YourTurn && !f.Mulligan).Select(f => f.Turn).Distinct().OrderBy(x => x).ToList();
            Replay.Frame EndOf(int turn) => r.Frames.LastOrDefault(f => f.Turn == turn);
            int StartOf(int turn) => r.TurnStarts.TryGetValue(turn, out var i) ? i : r.Frames.FindIndex(f => f.Turn == turn);
            string Round(int turn) => $"turn {(turn + 1) / 2}";

            // ---- Cards held while affordable for several turns, with mana left over on at least one of them.
            var held = new Dictionary<int, (string CardId, int Since, int Turns, bool Floated)>();
            var heldNotes = new List<(ReviewNote Note, int Turns)>();
            foreach (var turn in myTurns)
            {
                var end = EndOf(turn);
                var start = r.Frames[Math.Max(0, StartOf(turn))];
                var inHand = new HashSet<int>();
                foreach (var c in end.You.Hand)
                {
                    if (c.CardId == null || cards.IsCoin(c.CardId)) continue;
                    if (c.Cost < 1 || c.Cost > start.You.Crystals) continue;
                    inHand.Add(c.EntityId);
                    held.TryGetValue(c.EntityId, out var h);
                    held[c.EntityId] = (c.CardId, h.Turns == 0 ? turn : h.Since, h.Turns + 1, h.Floated || end.You.Mana >= c.Cost);
                }
                foreach (var gone in held.Keys.Where(k => !inHand.Contains(k)).ToList())
                {
                    var h = held[gone];
                    if (h.Turns >= 4 && h.Floated)
                        heldNotes.Add((new ReviewNote
                        {
                            Turn = h.Since, Kind = "HeldCard", Severity = 1, Frame = StartOf(h.Since),
                            Text = $"You held {cards.NameOf(h.CardId)} for {h.Turns} turns while you could afford it, and ended at least one of them with the mana to play it. Cards sitting in hand do nothing.",
                        }, h.Turns));
                    held.Remove(gone);
                }
            }
            foreach (var h in held.Values.Where(h => h.Turns >= 4 && h.Floated))
                heldNotes.Add((new ReviewNote
                {
                    Turn = h.Since, Kind = "HeldCard", Severity = 1, Frame = StartOf(h.Since),
                    Text = $"You held {cards.NameOf(h.CardId)} from {Round(h.Since)} to the end, affordable for {h.Turns} turns.",
                }, h.Turns));
            notes.AddRange(heldNotes.OrderByDescending(x => x.Turns).Take(2).Select(x => x.Note));

            // ---- Hero power unused with mana to spare (one note for the whole game).
            var idleTurns = new List<int>();
            foreach (var turn in myTurns.Where(t => (t + 1) / 2 >= 2))
            {
                var end = EndOf(turn);
                if (end.You.HeroPowerCardId == null || end.You.HeroPowerUsed) continue;
                var cost = cards.ById(end.You.HeroPowerCardId)?.Cost ?? 2;
                if (end.You.Mana >= Math.Max(1, cost) && end.You.Health > 0 && end.Them.Health > 0) idleTurns.Add(turn);
            }
            if (idleTurns.Count >= 2)
                notes.Add(new ReviewNote
                {
                    Turn = idleTurns[0], Kind = "HeroPower", Severity = idleTurns.Count >= 4 ? 2 : 1, Frame = Math.Max(0, r.Frames.IndexOf(EndOf(idleTurns[0]))),
                    Text = $"Your hero power went unused with mana to spare on turns {Join(idleTurns.Select(t => ((t + 1) / 2).ToString()))}.",
                });

            // ---- Trades where your minion died and theirs lived through the rest of the turn (no shield popped).
            var trades = new List<ReviewNote>();
            for (var i = 1; i < r.Frames.Count; i++)
            {
                var f = r.Frames[i];
                var prev = r.Frames[i - 1];
                if (!f.YourTurn || f.AttackerId == 0 || f.TargetIsHero) continue;
                var attacker = prev.You.Board.FirstOrDefault(m => m.EntityId == f.AttackerId);
                var target = prev.Them.Board.FirstOrDefault(m => m.EntityId == f.TargetId);
                if (attacker == null || target == null || target.DivineShield) continue;
                if (f.You.Board.Any(m => m.EntityId == f.AttackerId)) continue;            // your minion survived
                var end = EndOf(f.Turn);
                var after = end.Them.Board.FirstOrDefault(m => m.EntityId == f.TargetId);
                if (after == null) continue;                                              // finished off later that turn
                trades.Add(new ReviewNote
                {
                    Turn = f.Turn, Kind = "BadTrade", Severity = 1, Frame = i,
                    Text = $"Your {cards.NameOf(attacker.CardId)} ({attacker.Attack}/{attacker.Health}) died attacking {cards.NameOf(target.CardId)} ({target.Attack}/{target.Health}), which survived the turn.",
                });
            }
            notes.AddRange(trades.Take(2));

            // ---- Went face while their board could kill you, and died on their next turn.
            if (r.Result == "LOST")
                foreach (var turn in myTurns)
                {
                    var end = EndOf(turn);
                    var hp = end.You.Health + end.You.Armor;
                    var threat = end.Them.Board.Where(m => !m.Dormant && !m.Frozen).Sum(m => m.Attack * (m.Windfury ? 2 : 1)) + end.Them.WeaponAttack;
                    if (threat < hp || end.You.Board.Any(m => m.Taunt)) continue;
                    var face = r.Frames.Where(f => f.Turn == turn && f.AttackerId != 0 && f.TargetIsHero).ToList();
                    if (face.Count == 0) continue;
                    var died = r.Frames.Any(f => f.Turn == turn + 1 && f.You.Health <= 0) || turn + 1 >= r.Turns;
                    if (!died) continue;
                    notes.Add(new ReviewNote
                    {
                        Turn = turn, Kind = "IgnoredThreat", Severity = 3, Frame = r.Frames.IndexOf(face[0]),
                        Text = $"Their board had {threat} damage against your {hp} health and you attacked face; you died the next turn. Clearing their minions (or a taunt) first might have kept you alive.",
                    });
                    break;
                }

            // ---- The biggest win-chance drop, if it was large.
            var drop = chance?.Drops.FirstOrDefault();
            if (drop != null && drop.Drop >= 0.2)
                notes.Add(new ReviewNote
                {
                    Turn = r.Frames[drop.Frame].Turn, Kind = "Swing", Severity = 2, Frame = drop.Frame,
                    Text = $"The game turned here: your win chance fell from {drop.Before:P0} to {drop.After:P0} after \"{drop.Action}\".",
                });
            return notes;
        }

        /// <summary>The live notes plus the replay's, without saying the same thing twice about one turn.</summary>
        public static List<ReviewNote> Merge(GameRecord g, List<ReviewNote> fromReplay)
        {
            var all = (g?.Review ?? new List<ReviewNote>()).ToList();
            foreach (var n in fromReplay ?? new List<ReviewNote>())
            {
                // Unused hero power and unspent mana on the same turn are the same mistake.
                if (n.Kind == "HeroPower" && all.Any(a => a.Kind == "UnspentMana" && a.Turn == n.Turn)) continue;
                if (all.Any(a => a.Kind == n.Kind && a.Turn == n.Turn)) continue;
                all.Add(n);
            }
            return all.OrderByDescending(n => n.Severity).ThenBy(n => n.Turn).ToList();
        }

        private static string Join(IEnumerable<string> items)
        {
            var l = items.ToList();
            return l.Count <= 1 ? string.Join("", l) : string.Join(", ", l.Take(l.Count - 1)) + " and " + l.Last();
        }
    }
}
