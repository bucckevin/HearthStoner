using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    /// <summary>
    /// How early HearthStoner names the opponent's deck correctly. For each round, the share of games where its guess at
    /// the start of that round matched the archetype it settled on by the end of the game. Only games it ended up sure
    /// about (80%+) count, since the opponent's full list is never shown; that final guess stands in for the truth.
    /// </summary>
    public sealed class PredictionScore
    {
        public sealed class Round
        {
            public int Number;
            public int Games;
            public int Right;
            public double AverageConfidence;
            public double Accuracy => Games == 0 ? 0 : (double)Right / Games;
        }

        public const double SureEnough = 0.8;
        public int Games;
        public List<Round> Rounds = new List<Round>();

        public static PredictionScore Compute(IEnumerable<GameRecord> games, int maxRound = 8)
        {
            var s = new PredictionScore();
            var counted = games.Where(g => g.OpponentArchetype != null && g.OpponentArchetypeProbability >= SureEnough && g.Snapshots.Any(t => t.OpponentGuess != null)).ToList();
            s.Games = counted.Count;
            for (var r = 1; r <= maxRound; r++)
            {
                var round = new Round { Number = r };
                var conf = new List<double>();
                foreach (var g in counted)
                {
                    // The guess at the start of the round: the first snapshot of that round that has one.
                    var t = g.Snapshots.Where(x => (x.Turn + 1) / 2 == r && x.OpponentGuess != null).OrderBy(x => x.Turn).FirstOrDefault();
                    if (t == null) continue;
                    round.Games++;
                    if (t.OpponentGuess == g.OpponentArchetype) round.Right++;
                    conf.Add(t.OpponentGuessProbability);
                }
                round.AverageConfidence = conf.Count > 0 ? conf.Average() : 0;
                if (round.Games > 0) s.Rounds.Add(round);
            }
            return s;
        }

        /// <summary>
        /// For games recorded before guesses were saved: replays the game's log and fills in the guess at the start
        /// of each turn. Returns true if anything was added (save the record afterwards).
        /// </summary>
        public static bool Backfill(GameRecord g, List<string> lines, CardDb cards, MetaStats meta)
        {
            if (lines == null || g.Snapshots.Count == 0 || g.Snapshots.Any(t => t.OpponentGuess != null)) return false;
            var guesses = new Dictionary<int, (string, double)>();
            GameTracker tracker = null;
            Advisor advisor = null;
            tracker = new GameTracker(cards, ev =>
            {
                if (ev.Kind == GameEventKind.Turn) { try { guesses[tracker.Turn] = advisor.TopOpponentArchetype(); } catch { } }
            });
            advisor = new Advisor(tracker, cards, meta, new Settings());
            foreach (var l in lines) tracker.Feed(l);
            var changed = false;
            foreach (var t in g.Snapshots)
                if (guesses.TryGetValue(t.Turn, out var x) && x.Item1 != null) { t.OpponentGuess = x.Item1; t.OpponentGuessProbability = x.Item2; changed = true; }
            return changed;
        }
    }
}
