using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>
    /// Builds the Practice drills' questions from your saved games and their replays. Only what you could see in the
    /// game is asked; the answers come from what the game revealed later (hidden cards, secrets) or the data saved
    /// with the game (Discover win rates).
    /// </summary>
    public static class PracticeDrills
    {
        /// <summary>A replay turn is asked at most this many times per game in the hand drill.</summary>
        public const int HandTurnsPerGame = 3;

        // ------------------------------------------------------------------ question lists (from the records)

        /// <summary>"What would you play?": your turns with at least 3 mana where you played something (needs the game's log).</summary>
        public static List<string> PlayQuestions(GameRecord g) =>
            string.IsNullOrEmpty(g?.LogFile) ? new List<string>()
                : g.Snapshots.Where(t => t.Yours && t.ManaAvailable >= 3 && t.Played.Count > 0).Select(t => PracticeIds.Make(PracticeIds.Play, g, t.Turn)).ToList();

        /// <summary>"Guess their hand": a few of your turns from round 4 on where they held at least 2 cards (needs the log).</summary>
        public static List<string> HandQuestions(GameRecord g)
        {
            if (string.IsNullOrEmpty(g?.LogFile)) return new List<string>();
            var turns = g.Snapshots.Where(t => t.Yours && t.Turn >= 7 && t.TheirHand >= 2).Select(t => t.Turn).ToList();
            // Spread over the game: every other eligible turn, the latest first.
            return turns.Where((t, i) => i % 2 == 0).Reverse().Take(HandTurnsPerGame).Select(t => PracticeIds.Make(PracticeIds.Hand, g, t)).ToList();
        }

        /// <summary>Games the secret drill looks in: vs a class with secrets, with the log (the secrets are found in the replay).</summary>
        public static bool MayHaveSecrets(GameRecord g) =>
            !string.IsNullOrEmpty(g?.LogFile) && !string.IsNullOrEmpty(g.OpponentClass) && Knowledge.Secrets.ForClass(g.OpponentClass).Any();

        /// <summary>"Discover drill": the game's Discover picks with win-rate data for at least two options.</summary>
        public static List<string> DiscoverQuestions(GameRecord g) =>
            g?.Discovers == null ? new List<string>()
                : g.Discovers.Select((p, i) => (p, i)).Where(x => HasData(x.p)).Select(x => PracticeIds.Make(PracticeIds.Discover, g, x.i)).ToList();

        private static bool HasData(DiscoverPick p) => p.Options.Count >= 2 && p.WinRates.Count(w => w.HasValue) >= 2;

        /// <summary>The option the data favours (highest win rate when picked), or -1 without data.</summary>
        public static int BestOption(DiscoverPick p)
        {
            if (p == null || !HasData(p)) return -1;
            var best = -1;
            for (var i = 0; i < p.Options.Count && i < p.WinRates.Count; i++)
                if (p.WinRates[i].HasValue && (best < 0 || p.WinRates[i] > p.WinRates[best])) best = i;
            return best;
        }

        // ------------------------------------------------------------------ what would you play

        /// <summary>The most mana any set of these cards uses without going over <paramref name="mana"/> (the "This turn" helper's ranking).</summary>
        public static int BestManaUse(IReadOnlyList<int> costs, int mana)
        {
            if (costs == null || costs.Count == 0 || mana <= 0) return 0;
            var n = Math.Min(costs.Count, 12);
            var best = 0;
            for (var mask = 1; mask < (1 << n); mask++)
            {
                var used = 0;
                for (var i = 0; i < n; i++) if ((mask & (1 << i)) != 0) used += Math.Max(0, costs[i]);
                if (used <= mana && used > best) best = used;
            }
            return best;
        }

        /// <summary>
        /// The frame to ask a turn at: the first frame of your turn with your mana refilled (the turn's very first
        /// frame comes before the game gives you your crystals). -1 when the turn isn't in the replay or isn't yours.
        /// </summary>
        public static int TurnFrame(Replay r, int turn)
        {
            if (r == null || !r.TurnStarts.TryGetValue(turn, out var start) || !r.Frames[start].YourTurn) return -1;
            for (var i = start; i < r.Frames.Count && r.Frames[i].Turn == turn; i++)
            {
                if (r.Frames[i].PlayedByYou) break;            // something was played: too late to ask
                if (r.Frames[i].You.Mana > 0) return i;
            }
            return start;
        }

        /// <summary>The cards you played during one game turn of a replay, in order.</summary>
        public static List<string> PlayedInTurn(Replay r, int turn) =>
            r.Frames.Where(f => f.Turn == turn && f.PlayedByYou && !string.IsNullOrEmpty(f.PlayedCardId)).Select(f => f.PlayedCardId).ToList();

        // ------------------------------------------------------------------ guess their hand

        /// <summary>One face-down card in their hand that the game revealed later.</summary>
        public sealed class HandGuess
        {
            public int EntityId;
            /// <summary>Position in their hand, left to right.</summary>
            public int Slot;
            /// <summary>Their turns since it arrived (0 for this turn's draw).</summary>
            public int Held;
            public string Answer;
            /// <summary>The answer plus up to three others, shuffled.</summary>
            public List<string> Choices = new List<string>();
        }

        /// <summary>
        /// Up to <paramref name="max"/> of their face-down cards at a frame that were revealed later, longest held
        /// first, each with choices from <paramref name="pool"/> (their likely and seen cards). The shuffle is seeded,
        /// so a question looks the same each time.
        /// </summary>
        public static List<HandGuess> GuessHand(Replay r, int frame, IEnumerable<string> pool, int seed, int max = 3)
        {
            var result = new List<HandGuess>();
            if (r == null || frame < 0 || frame >= r.Frames.Count) return result;
            var f = r.Frames[frame];
            var others = (pool ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            var rng = new Random(seed);
            for (var i = 0; i < f.Them.Hand.Count; i++)
            {
                var h = f.Them.Hand[i];
                if (h.CardId != null || !r.FinalCardIds.TryGetValue(h.EntityId, out var answer)) continue;
                var held = h.Mark == null ? 0 : Math.Max(0, f.Round - Math.Max(1, h.Mark.Turn));
                var choices = others.Where(id => id != answer).OrderBy(_ => rng.Next()).Take(3).Append(answer).OrderBy(_ => rng.Next()).ToList();
                result.Add(new HandGuess { EntityId = h.EntityId, Slot = i, Held = held, Answer = answer, Choices = choices });
            }
            return result.OrderByDescending(x => x.Held).ThenBy(x => x.Slot).Take(max).OrderBy(x => x.Slot).ToList();
        }

        // ------------------------------------------------------------------ secrets

        /// <summary>A secret they played that the game revealed later, asked at the start of your next turn.</summary>
        public sealed class SecretQuestion
        {
            /// <summary>The frame to ask at.</summary>
            public int Frame;
            /// <summary>The frame where it was played.</summary>
            public int PlayedFrame;
            public string Answer;
            /// <summary>Standard secrets of their class (at most six), the answer among them.</summary>
            public List<string> Choices = new List<string>();
        }

        /// <summary>Every revealed Standard secret the opponent played in a replay, in order.</summary>
        public static List<SecretQuestion> Secrets(Replay r, string opponentClass, int seed)
        {
            var result = new List<SecretQuestion>();
            if (r == null) return result;
            var pool = Knowledge.Secrets.ForClass(opponentClass).Select(s => s.CardId).ToList();
            var seen = new HashSet<int>();
            var rng = new Random(seed);
            foreach (var f in r.Frames)
                for (var i = 0; i < f.Them.SecretEntities.Count; i++)
                {
                    var e = f.Them.SecretEntities[i];
                    if (!seen.Add(e)) continue;
                    if (f.Them.Secrets.ElementAtOrDefault(i) != "" || !r.FinalCardIds.TryGetValue(e, out var answer) || !pool.Contains(answer)) continue;
                    var ask = r.Frames.FirstOrDefault(x => x.Index > f.Index && x.YourTurn && r.TurnStarts.TryGetValue(x.Turn, out var s) && s == x.Index)?.Index ?? f.Index;
                    var choices = pool.Where(id => id != answer).OrderBy(_ => rng.Next()).Take(5).Append(answer).OrderBy(_ => rng.Next()).ToList();
                    result.Add(new SecretQuestion { Frame = ask, PlayedFrame = f.Index, Answer = answer, Choices = choices });
                }
            return result;
        }

        // ------------------------------------------------------------------ mulligan by matchup

        /// <summary>How you mulligan one card in a matchup, and what the data says it's worth.</summary>
        public sealed class KeepRow
        {
            public string CardId;
            public int Seen;
            public double KeepRate;
            /// <summary>Average win-rate change when kept, from the data saved with each game.</summary>
            public double Delta;
        }

        /// <summary>Your opening-hand cards with data against one class, the ones that matter most first.</summary>
        public static List<KeepRow> MatchupKeeps(IEnumerable<GameRecord> games, string opponentClass) =>
            (games ?? Enumerable.Empty<GameRecord>())
                .Where(g => string.Equals(g.OpponentClass, opponentClass, StringComparison.OrdinalIgnoreCase))
                .SelectMany(g => g.Mulligan).Where(m => m.Advice == "KEEP" || m.Advice == "TOSS")
                .GroupBy(m => m.CardId)
                .Select(grp => new KeepRow { CardId = grp.Key, Seen = grp.Count(), KeepRate = (double)grp.Count(m => m.Kept) / grp.Count(), Delta = grp.Average(m => m.Delta) })
                .OrderByDescending(x => Math.Abs(x.Delta)).ToList();
    }
}
