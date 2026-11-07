using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    /// <summary>
    /// How well you use your mana, from the turn snapshots of every recorded game: the share of available mana you
    /// spend, how often you use all of it, and which turns you tend to stall on. Turns 1-10 only: late game, with
    /// a near-empty hand, floating mana is normal.
    /// </summary>
    public sealed class ManaStats
    {
        public const int MaxRound = 10;

        public sealed class RoundRow
        {
            public int Round;
            public int Turns;
            public double AvgAvailable, AvgSpent;
            public double AvgUnspent => AvgAvailable - AvgSpent;
            /// <summary>Share of these turns where you spent every crystal.</summary>
            public double FullRate;
        }

        public sealed class DeckRow
        {
            public string Name;
            public string Class;
            public int Turns;
            public double UsedRate;
            public double AvgUnspent;
            public double CurveRate;
        }

        public int Games;
        public int Turns;
        /// <summary>Mana spent / mana available, over turns 1-10.</summary>
        public double UsedRate;
        public double AvgUnspent;
        /// <summary>Share of turns 1-6 where you spent all your mana ("hit your curve").</summary>
        public double CurveRate;
        public List<RoundRow> ByRound = new List<RoundRow>();
        public List<DeckRow> ByDeck = new List<DeckRow>();
        /// <summary>The early/mid turn with the most mana left over on average (null if nothing stands out).</summary>
        public RoundRow StallRound;

        private sealed class T { public int Round, Available, Spent; public string Deck, Class; }

        public static ManaStats From(List<GameRecord> games)
        {
            var s = new ManaStats();
            var turns = new List<T>();
            foreach (var g in games)
            {
                var mine = g.Snapshots.Where(t => t.Yours && t.ManaAvailable > 0).ToList();
                if (mine.Count == 0) continue;
                s.Games++;
                foreach (var t in mine)
                {
                    var round = (t.Turn + 1) / 2;
                    if (round > MaxRound) continue;
                    // Snapshots of the last turn of a game are taken when it ends, often mid-turn: skip it.
                    if (t == g.Snapshots.LastOrDefault()) continue;
                    var spent = Math.Max(0, Math.Min(t.ManaAvailable, t.ManaAvailable - t.ManaUnspent));
                    turns.Add(new T { Round = round, Available = t.ManaAvailable, Spent = spent, Deck = g.DeckName ?? g.MyArchetype ?? MetaStats.ClassName(g.MyClass), Class = g.MyClass });
                }
            }
            s.Turns = turns.Count;
            if (turns.Count == 0) return s;
            s.UsedRate = (double)turns.Sum(t => t.Spent) / Math.Max(1, turns.Sum(t => t.Available));
            s.AvgUnspent = turns.Average(t => t.Available - t.Spent);
            var early = turns.Where(t => t.Round <= 6).ToList();
            s.CurveRate = early.Count == 0 ? 0 : (double)early.Count(t => t.Spent >= t.Available) / early.Count;
            s.ByRound = turns.GroupBy(t => t.Round).OrderBy(g => g.Key).Select(g => new RoundRow
            {
                Round = g.Key, Turns = g.Count(), AvgAvailable = g.Average(t => t.Available), AvgSpent = g.Average(t => t.Spent),
                FullRate = (double)g.Count(t => t.Spent >= t.Available) / g.Count(),
            }).ToList();
            s.StallRound = s.ByRound.Where(r => r.Round <= 7 && r.Turns >= 2 && r.AvgUnspent >= 1).OrderByDescending(r => r.AvgUnspent).FirstOrDefault();
            s.ByDeck = turns.GroupBy(t => t.Deck).Select(g =>
            {
                var e = g.Where(t => t.Round <= 6).ToList();
                return new DeckRow
                {
                    Name = g.Key, Class = g.First().Class, Turns = g.Count(),
                    UsedRate = (double)g.Sum(t => t.Spent) / Math.Max(1, g.Sum(t => t.Available)),
                    AvgUnspent = g.Average(t => t.Available - t.Spent),
                    CurveRate = e.Count == 0 ? 0 : (double)e.Count(t => t.Spent >= t.Available) / e.Count,
                };
            }).OrderByDescending(d => d.Turns).ToList();
            return s;
        }
    }
}
