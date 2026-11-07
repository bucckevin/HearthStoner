using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>
    /// Your ranked ladder per format: rank game by game, seasons (calendar months: Hearthstone resets the ladder on
    /// the 1st), peaks, the last session and games to the next milestones. Ranks are read from memory as each game
    /// starts, so a game's rank is the one it was played from.
    /// </summary>
    public static class Ladder
    {
        private static readonly string[] Leagues = { "Bronze", "Silver", "Gold", "Platinum", "Diamond" };
        /// <summary>Games further apart than this are separate sessions.</summary>
        public static readonly TimeSpan SessionGap = TimeSpan.FromHours(2);

        /// <summary>Star level 1-50 is Bronze 10 .. Diamond 1 (ten ranks a league, 3 stars a rank); 51 is Legend.</summary>
        public readonly struct Rank
        {
            public readonly int Level, Stars, Legend;
            public Rank(int level, int stars = 0, int legend = 0) { Level = level; Stars = stars; Legend = legend; }
            public bool IsLegend => Legend > 0 || Level > 50;
            public bool Known => Level > 0 || Legend > 0;
            /// <summary>Higher is better; any Legend rank beats any star level.</summary>
            public int Score => IsLegend ? (Legend > 0 ? 1_000_000 - Legend : 500_000) : Level * 3 + Math.Clamp(Stars, 0, 2);
            public override string ToString() => Name(Level, Legend);
        }

        public static Rank RankOf(GameRecord g) => new Rank(g.StarLevel, g.Stars, g.LegendRank);

        /// <summary>"Bronze 10" .. "Diamond 1", "Legend 12,076".</summary>
        public static string Name(int level, int legend = 0)
        {
            if (legend > 0) return "Legend " + legend.ToString("N0", CultureInfo.InvariantCulture);
            if (level > 50) return "Legend";
            if (level <= 0) return "Unranked";
            return $"{Leagues[(level - 1) / 10]} {10 - (level - 1) % 10}";
        }

        /// <summary>
        /// The ranked season a game belongs to, as its first day. The ladder rolls over around midnight Pacific, so a
        /// game at 1am Eastern on the 1st still counts for the month before.
        /// </summary>
        public static DateTime SeasonOf(DateTime start)
        {
            var t = start.ToUniversalTime().AddHours(-8);
            return new DateTime(t.Year, t.Month, 1);
        }

        public sealed class Point
        {
            public GameRecord Game;
            public Rank Rank;
            public DateTime When => Game.Start;
        }

        public sealed class Season
        {
            public DateTime Month;
            /// <summary>Ranked games in this format, oldest first.</summary>
            public List<GameRecord> Games = new List<GameRecord>();
            /// <summary>The games whose rank is known.</summary>
            public List<Point> Points = new List<Point>();
            public int Wins => Games.Count(g => g.Won);
            public int Losses => Games.Count(g => g.Result == "LOST");
            public Point Start => Points.FirstOrDefault();
            public Point Last => Points.LastOrDefault();
            /// <summary>The best rank you played from (the first time you reached it).</summary>
            public Point Peak => Points.OrderByDescending(p => p.Rank.Score).FirstOrDefault();
            public string Name => Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        }

        public sealed class Session
        {
            public List<GameRecord> Games = new List<GameRecord>();
            public int Wins => Games.Count(g => g.Won);
            public int Losses => Games.Count(g => g.Result == "LOST");
            public double WinRate => Wins + Losses == 0 ? 0 : (double)Wins / (Wins + Losses);
            /// <summary>Rank at the start of its first and last ranked games (the rank after the last isn't known yet).</summary>
            public Rank From, To;
        }

        public sealed class Milestone
        {
            public string Target;
            /// <summary>Expected games; null when not climbing at that win rate.</summary>
            public int? Games;
        }

        public sealed class Format
        {
            public string FormatType;
            public string Name;
            /// <summary>Oldest first; only seasons with a known rank.</summary>
            public List<Season> Seasons = new List<Season>();
            public Season Latest => Seasons[Seasons.Count - 1];
            public Rank Current => Latest.Last.Rank;
            public Session LastSession;
            public int RecentGames;
            public double RecentWinRate;
            /// <summary>Star bonus at your latest game (1 = none; 0 when not recorded).</summary>
            public int StarBonus;
            public List<Milestone> Milestones = new List<Milestone>();
        }

        /// <summary>One entry per format with ranked games that recorded a rank, most recently played first.</summary>
        public static List<Format> From(IEnumerable<GameRecord> games, int recent = 20)
        {
            var result = new List<Format>();
            var ranked = games.Where(g => GameModes.Of(g.GameType) == GameModes.Kind.Ranked);
            foreach (var grp in ranked.GroupBy(g => string.IsNullOrEmpty(g.Format) || g.Format == "FT_UNKNOWN" ? "FT_STANDARD" : g.Format))
            {
                var list = grp.OrderBy(g => g.Start).ToList();
                var f = new Format { FormatType = grp.Key, Name = GameModes.Name("GT_RANKED", grp.Key) };
                foreach (var m in list.GroupBy(g => SeasonOf(g.Start)))
                {
                    var s = new Season { Month = m.Key, Games = m.ToList() };
                    s.Points = s.Games.Where(g => RankOf(g).Known).Select(g => new Point { Game = g, Rank = RankOf(g) }).ToList();
                    if (s.Points.Count > 0) f.Seasons.Add(s);
                }
                if (f.Seasons.Count == 0) continue;

                f.LastSession = Sessions(list).Last();
                var decided = list.Where(g => g.Decided).ToList();
                var last = decided.Skip(Math.Max(0, decided.Count - recent)).ToList();
                f.RecentGames = last.Count;
                f.RecentWinRate = last.Count == 0 ? 0 : (double)last.Count(g => g.Won) / last.Count;
                f.StarBonus = f.Latest.Last.Game.StarBonus;
                if (f.RecentGames >= 5) f.Milestones = Milestones(f.Current, f.RecentWinRate, f.StarBonus);
                result.Add(f);
            }
            return result.OrderByDescending(f => f.Latest.Games.Last().Start).ToList();
        }

        /// <summary>Games split wherever there's a break of <see cref="SessionGap"/> or more, oldest first.</summary>
        public static List<Session> Sessions(List<GameRecord> games)
        {
            var sessions = new List<Session>();
            GameRecord prev = null;
            foreach (var g in games.OrderBy(g => g.Start))
            {
                var prevEnd = prev == null ? DateTime.MinValue : prev.End > prev.Start ? prev.End : prev.Start;
                if (prev == null || g.Start - prevEnd >= SessionGap) sessions.Add(new Session());
                sessions[sessions.Count - 1].Games.Add(g);
                prev = g;
            }
            foreach (var s in sessions)
            {
                var known = s.Games.Select(RankOf).Where(r => r.Known).ToList();
                if (known.Count > 0) { s.From = known[0]; s.To = known[known.Count - 1]; }
            }
            return sessions;
        }

        /// <summary>"up 3 stars", "down 494 places", "Diamond 1 to Legend 9,000"; null when either rank is unknown.</summary>
        public static string Change(Rank from, Rank to)
        {
            if (!from.Known || !to.Known) return null;
            if (from.IsLegend && to.IsLegend && from.Legend > 0 && to.Legend > 0)
            {
                var d = from.Legend - to.Legend;
                return d == 0 ? "no change" : $"{(d > 0 ? "up" : "down")} {Math.Abs(d).ToString("N0", CultureInfo.InvariantCulture)} place{(Math.Abs(d) == 1 ? "" : "s")}";
            }
            if (!from.IsLegend && !to.IsLegend)
            {
                var d = to.Score - from.Score;
                return d == 0 ? "no change" : $"{(d > 0 ? "up" : "down")} {Math.Abs(d)} star{(Math.Abs(d) == 1 ? "" : "s")}";
            }
            return $"{from} to {to}";
        }

        /// <summary>Next rank, Diamond 5 and Legend, whichever are still ahead. None in Legend: there are no stars to count.</summary>
        public static List<Milestone> Milestones(Rank cur, double winRate, int starBonus)
        {
            var list = new List<Milestone>();
            if (!cur.Known || cur.IsLegend) return list;
            var targets = new List<int> { cur.Level + 1 };
            if (cur.Level + 1 < 46) targets.Add(46);
            if (cur.Level + 1 < 51) targets.Add(51);
            foreach (var t in targets)
                list.Add(new Milestone { Target = Name(t), Games = LadderEstimate.GamesToReach(cur.Level, t, winRate, cur.Stars, starBonus) });
            return list;
        }

        // ---- chart scale: star levels 1-50 one unit each; Legend sits above, log-scaled so rank 1 is 9 units up

        /// <summary>The worst legend rank the chart's log scale covers: twice the worst seen (at least 1000).</summary>
        public static int LegendCap(IEnumerable<Rank> ranks) => Math.Max(1000, 2 * ranks.Select(r => r.Legend).DefaultIfEmpty(0).Max());

        public static double Height(Rank r, int cap)
        {
            if (!r.IsLegend) return r.Level + Math.Clamp(r.Stars, 0, 2) / 3.0;
            if (r.Legend <= 0) return 51;
            return 51 + 9 * Math.Log(cap / (double)Math.Min(r.Legend, cap)) / Math.Log(cap);
        }

        /// <summary>The legend rank at a chart height of 51 or more.</summary>
        public static double LegendAt(double h, int cap) => Math.Pow(cap, 1 - (h - 51) / 9);

        /// <summary>Gridlines between two chart heights: ranks or leagues below Legend, round legend ranks above.</summary>
        public static List<(double H, string Label)> Ticks(double lo, double hi, int cap)
        {
            var ticks = new List<(double, string)>();
            var top = Math.Min(hi, 50.99);
            if (lo <= top)
            {
                var span = top - lo;
                var step = span <= 6 ? 1 : span <= 15 ? 5 : 10;
                for (var l = Math.Max(1, (int)Math.Ceiling(lo)); l <= top; l++)
                    if ((l - 1) % step == 0) ticks.Add((l, step == 10 ? Leagues[(l - 1) / 10] : Name(l)));
            }
            if (hi >= 51)
            {
                if (lo <= 51) ticks.Add((51, "Legend"));
                double worst = LegendAt(Math.Max(lo, 51), cap), best = Math.Max(1, LegendAt(hi, cap));
                var raw = Math.Max(1, (worst - best) / 3);
                var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
                var unit = (raw / mag <= 1 ? 1 : raw / mag <= 2 ? 2 : raw / mag <= 5 ? 5 : 10) * mag;
                for (var v = Math.Ceiling(best / unit) * unit; v <= worst; v += unit)
                    if (v >= 1 && (lo > 51 || v < worst * 0.8))   // keep clear of the "Legend" line
                        ticks.Add((Height(new Rank(51, 0, (int)v), cap), ((int)v).ToString("N0", CultureInfo.InvariantCulture)));
            }
            return ticks;
        }
    }

    /// <summary>
    /// Hearthstone's ladder rules, as your saved games show them: 3 stars a rank; a win is worth 1 star times the star
    /// bonus, doubled on the 3rd win in a row or more below Diamond 5; a loss costs 1 star; ranks 10 and 5 of each
    /// league are floors you can't fall below once reached; the star bonus drops by 1 at each new floor.
    /// Stars are counted from Bronze 10 with no stars: (star level - 1) * 3 + stars in the rank.
    /// </summary>
    public static class LadderRules
    {
        public const int DiamondFive = 46, LegendLevel = 51, StarsPerFloor = 15;

        public static int StarIndex(int level, int stars = 0) => (Math.Max(1, level) - 1) * 3 + Math.Clamp(stars, 0, 2);
        public static int LevelAt(int starIndex) => starIndex / 3 + 1;
        public static bool IsFloor(int level) => level >= 1 && level <= 50 && (level - 1) % 5 == 0;
        /// <summary>The lowest you can fall to from here.</summary>
        public static int FloorAt(int starIndex) => starIndex / StarsPerFloor * StarsPerFloor;
        /// <summary>Stars a win is worth; <paramref name="streak"/> counts this win.</summary>
        public static int WinStars(int level, int bonus, int streak) => Math.Max(1, bonus) * (streak >= 3 && level < DiamondFive ? 2 : 1);
        /// <summary>The star bonus after climbing from one star count to another (one less per new floor, never below 1).</summary>
        public static int BonusAfter(int bonus, int fromIndex, int toIndex) =>
            Math.Max(1, Math.Max(1, bonus) - Math.Max(0, FloorAt(Math.Min(toIndex, 150)) - FloorAt(fromIndex)) / StarsPerFloor);
        /// <summary>The floor your star bonus runs out at (where it reaches x1), or null with no bonus.</summary>
        public static int? BonusEndsAtLevel(int level, int bonus)
        {
            if (bonus <= 1) return null;
            var floorLevel = FloorAt(StarIndex(level)) / 3 + 1;
            var end = floorLevel + 5 * (bonus - 1);
            return end <= 50 ? end : (int?)null;   // lasts all the way to Legend
        }
    }

    /// <summary>
    /// Expected games to climb, simulated with <see cref="LadderRules"/>. Returns null when the win rate is too low
    /// to climb.
    /// </summary>
    public static class LadderEstimate
    {
        public static int? GamesToReach(int fromStarLevel, int toStarLevel, double winRate, int starsInRank = 0, int starBonus = 1, int streak = 0)
        {
            if (toStarLevel <= fromStarLevel) return 0;
            if (winRate <= 0.5 && starBonus <= 1) return null;   // a star bonus and floors can still lift you at 50%
            if (winRate <= 0) return null;
            var rng = new Random(12345);
            const int runs = 2000;
            long total = 0;
            var start = LadderRules.StarIndex(fromStarLevel, starsInRank);
            var target = LadderRules.StarIndex(toStarLevel);
            for (var r = 0; r < runs; r++)
            {
                var stars = start;
                var floor = LadderRules.FloorAt(stars);
                var bonus = Math.Max(1, starBonus);
                int run = Math.Max(0, streak), games = 0;
                while (stars < target && games < 5000)
                {
                    games++;
                    if (rng.NextDouble() < winRate)
                    {
                        run++;
                        var before = stars;
                        stars += LadderRules.WinStars(LadderRules.LevelAt(stars), bonus, run);
                        bonus = LadderRules.BonusAfter(bonus, before, stars);
                        floor = Math.Max(floor, LadderRules.FloorAt(stars));
                    }
                    else
                    {
                        run = 0;
                        stars = Math.Max(floor, stars - 1);
                    }
                }
                if (stars < target) return null;   // stuck: not climbing at this win rate
                total += games;
            }
            return (int)Math.Round((double)total / runs);
        }
    }
}
