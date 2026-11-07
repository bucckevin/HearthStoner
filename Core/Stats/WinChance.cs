using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>
    /// An estimate of your chance to win at each moment of a replay, from what both players could see: health and
    /// armor, the boards, cards in hand, mana, who can kill whom next, and (as the starting point) how this matchup
    /// goes in the meta data. It's a hand-tuned score, not a simulation: good for spotting where a game turned.
    /// </summary>
    public sealed class WinChance
    {
        public sealed class Moment
        {
            /// <summary>Frame where the chance dropped the most in this stretch.</summary>
            public int Frame;
            public int Round;
            public bool YourTurn;
            public double Before, After;
            public double Drop => Before - After;
            public string Action;
        }

        /// <summary>One value per replay frame, 0..1.</summary>
        public List<double> Values = new List<double>();
        /// <summary>Where it started (the matchup's win rate, or 50%).</summary>
        public double Prior = 0.5;
        /// <summary>The biggest drops, worst first, at most one per round.</summary>
        public List<Moment> Drops = new List<Moment>();
        /// <summary>The biggest rises, best first.</summary>
        public List<Moment> Rises = new List<Moment>();

        private const double Damping = 0.75;

        public double At(int frame) => Values.Count == 0 ? Prior : Values[Math.Max(0, Math.Min(Values.Count - 1, frame))];

        /// <summary>The matchup's win rate in the meta data (your archetype vs their class), or 0.5 when unknown.</summary>
        public static double MatchupPrior(GameRecord g, MetaStats meta, CardDb cards)
        {
            try
            {
                if (meta == null || g == null || string.IsNullOrEmpty(g.OpponentClass)) return 0.5;
                MetaArchetype arch = null;
                if (g.DeckCards?.Count > 0 && !string.IsNullOrEmpty(g.MyClass)) arch = meta.MatchDeck(g.MyClass.ToLowerInvariant(), g.DeckCards);
                if (arch == null && !string.IsNullOrEmpty(g.MyArchetype)) arch = meta.Archetypes.FirstOrDefault(a => a.Name == g.MyArchetype);
                if (arch == null) return 0.5;
                var det = meta.Detail(arch.Id, null);
                if (det != null && det.Matchups.TryGetValue(g.OpponentClass.ToLowerInvariant(), out var m) && m.Games >= 50) return m.WinRate;
                return arch.Games >= 50 ? arch.WinRate : 0.5;
            }
            catch { return 0.5; }
        }

        public static WinChance For(Replay r, double prior = 0.5)
        {
            var w = new WinChance { Prior = Clamp(prior, 0.3, 0.7) };
            if (r == null || r.Frames.Count == 0) return w;
            foreach (var f in r.Frames) w.Values.Add(Estimate(f, w.Prior));
            // The last frame is the result: a concede ends the game without a hero dying.
            if (r.Result == "WON") w.Values[w.Values.Count - 1] = 1;
            else if (r.Result == "LOST") w.Values[w.Values.Count - 1] = 0;
            w.FindMoments(r);
            return w;
        }

        /// <summary>Your chance to win in one frame.</summary>
        public static double Estimate(Replay.Frame f, double prior = 0.5)
        {
            if (f.Mulligan || f.Turn <= 1 && f.You.Board.Count == 0 && f.Them.Board.Count == 0) return prior;
            var y = f.You; var t = f.Them;
            if (t.Health <= 0 && y.Health > 0) return 0.995;
            if (y.Health <= 0 && t.Health > 0) return 0.005;

            double Hp(Replay.Side s) => s.Health + s.Armor;
            // Minions at 0 or less health are dying (the frame is taken before deaths are processed).
            IEnumerable<Replay.Minion> Alive(Replay.Side s) => s.Board.Where(m => m.Health > 0);
            double Attack(Replay.Side s) => Alive(s).Where(m => !m.Dormant).Sum(m => (double)m.Attack * (m.Windfury ? 2 : 1)) + s.WeaponAttack;
            // Dormant minions wake up in a turn or two: most of their value, none of their attack yet.
            double BoardValue(Replay.Side s) => Alive(s).Sum(m => (m.Dormant ? 0.6 : 1.0) * (m.Attack + m.Health + (m.DivineShield ? m.Attack : 0) + (m.Taunt ? 1 : 0) + (m.Reborn ? 2 : 0)));

            var score = 0.0;
            score += 0.04 * (Hp(y) - Hp(t));
            score += 0.045 * (BoardValue(y) - BoardValue(t));
            score += 0.10 * (Math.Min(y.Hand.Count, 8) - Math.Min(t.Hand.Count, 8));
            // Mana not yet turned into board: the side to act still has this turn's, the other its next turn's (later,
            // so worth less). Without it the line zig-zags, everyone looking ahead right after they play.
            double Potential(Replay.Side s, bool toAct) => toAct ? s.Mana : 0.5 * Math.Min(s.Crystals + 1, 10);
            score += 0.08 * (Potential(y, f.YourTurn) - Potential(t, !f.YourTurn));

            // Who is closer to killing the other, counting turns at the current damage on board.
            var yAtk = Attack(y); var tAtk = Attack(t);
            var yClock = Hp(t) / Math.Max(1, yAtk);
            var tClock = Hp(y) / Math.Max(1, tAtk);
            score += 0.25 * Clamp(Math.Log((tClock + 0.5) / (yClock + 0.5)), -2, 2);

            // Lethal on board: decisive for the side whose turn it is; the other side still gets to answer it.
            var theirTaunt = Alive(t).Any(m => m.Taunt && !m.Stealth);
            var yourTaunt = Alive(y).Any(m => m.Taunt && !m.Stealth);
            if (yAtk >= Hp(t) && !theirTaunt) score += f.YourTurn ? 2.0 : 0.5;
            if (tAtk >= Hp(y) && !yourTaunt) score -= f.YourTurn ? 0.5 : 2.0;

            // Running out of cards.
            if (y.DeckCount == 0 && t.DeckCount > 0) score -= 0.6;
            if (t.DeckCount == 0 && y.DeckCount > 0) score += 0.6;

            // Damped: it can't see the cards in hand that answer a board, so it shouldn't be too sure of anything.
            score = Logit(prior) + Damping * score;
            return Clamp(1 / (1 + Math.Exp(-score)), 0.02, 0.98);
        }

        /// <summary>
        /// Splits the game into rounds (your turn plus theirs) and keeps, for each, the single action that moved the
        /// chance the most. Comparing only within a round keeps one bad turn from showing up twice.
        /// </summary>
        private void FindMoments(Replay r)
        {
            var drops = new List<Moment>();
            var rises = new List<Moment>();
            foreach (var group in Enumerable.Range(1, r.Frames.Count - 1).GroupBy(i => r.Frames[i].Round))
            {
                Moment worst = null, best = null;
                foreach (var i in group)
                {
                    var delta = Values[i] - Values[i - 1];
                    var m = new Moment { Frame = i, Round = r.Frames[i].Round, YourTurn = r.Frames[i].YourTurn, Before = Values[i - 1], After = Values[i], Action = Describe(r, i) };
                    if (delta < 0 && (worst == null || delta < worst.After - worst.Before)) worst = m;
                    if (delta > 0 && (best == null || delta > best.After - best.Before)) best = m;
                }
                if (worst != null) drops.Add(worst);
                if (best != null) rises.Add(best);
            }
            // The final frame is the result itself, not a moment worth pointing at.
            var last = r.Frames.Count - 1;
            Drops = drops.Where(m => m.Frame != last && m.Drop >= 0.12).OrderByDescending(m => m.Drop).Take(3).ToList();
            Rises = rises.Where(m => m.Frame != last && -m.Drop >= 0.12).OrderBy(m => m.Drop).Take(3).ToList();
        }

        /// <summary>What happened at a frame, in words; "Effects resolve" is replaced by the events it covers.</summary>
        public static string Describe(Replay r, int i)
        {
            var f = r.Frames[i];
            if (f.Action != "Effects resolve" && !string.IsNullOrEmpty(f.Action)) return f.Action;
            var events = f.Events.Where(e => !e.StartsWith("---") && !e.Contains("drew a card")).Take(2).ToList();
            if (events.Count > 0) return string.Join("; ", events);
            for (var j = i - 1; j >= 0 && r.Frames[j].Turn == f.Turn; j--)
                if (r.Frames[j].Action != "Effects resolve" && !string.IsNullOrEmpty(r.Frames[j].Action)) return "after " + Lower(r.Frames[j].Action);
            return f.YourTurn ? "During your turn" : "During their turn";
        }

        /// <summary>"The opponent's hero attacked" -> "the opponent's hero attacked" (card names keep their capitals).</summary>
        private static string Lower(string s) =>
            s.StartsWith("The ") || s.StartsWith("You ") || s.StartsWith("Your ") || s.StartsWith("Opponent") ? char.ToLowerInvariant(s[0]) + s.Substring(1) : s;

        private static double Logit(double p) => Math.Log(p / (1 - p));
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
