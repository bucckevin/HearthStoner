using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>
    /// Everything the Ladder page shows for one ranked format, from your saved games: pace to your goals before the
    /// season ends, which deck climbs fastest per hour, the star bonus and win streaks, the ranks of your opponents (a
    /// hint at your hidden matchmaking rating), how this session is going, and your win rate by time of day.
    /// </summary>
    public sealed class LadderReport
    {
        public sealed class Goal
        {
            public string Target;
            public int Level;
            /// <summary>Expected games; null when not climbing at your win rate.</summary>
            public int? Games;
            /// <summary>When you'd get there at your usual games a day; null when unknown.</summary>
            public DateTime? EtaDate;
            /// <summary>Games a day it takes to get there before the season ends; null when the end isn't known.</summary>
            public double? PerDayNeeded;
            public bool InTime;
        }

        public sealed class DeckClimb
        {
            public string Name, Class;
            public int Games, Wins;
            public double WinRate => Games == 0 ? 0 : (double)Wins / Games;
            /// <summary>Average game length in minutes; null without timed games.</summary>
            public double? Minutes;
            /// <summary>Expected win rate against the field (meta data); null when the deck isn't a meta archetype.</summary>
            public double? Expected;
            /// <summary>Net stars per hour at your record (5+ games) and at the meta's expected win rate.</summary>
            public double? YourStarsPerHour, MetaStarsPerHour;
            public double? Best => Games >= MinGames ? YourStarsPerHour : MetaStarsPerHour ?? YourStarsPerHour;
        }

        public sealed class Opponents
        {
            public int Games;
            /// <summary>Average star levels over the games where both ranks are known (Legend counts as 51).</summary>
            public double AverageTheirs, AverageYours;
            public double Gap => AverageTheirs - AverageYours;
        }

        public sealed class SessionCheck
        {
            public Ladder.Session Session;
            public bool Current;
            /// <summary>Stars won (or lost) this session; null when a rank is missing or you're in Legend.</summary>
            public int? NetStars;
            public int LossStreak;
            /// <summary>Your win rate in games right after 3+ losses in a row (within a session), all modes; null under 5 such games.</summary>
            public double? AfterThreeLosses;
            public int AfterThreeLossesGames;
        }

        public sealed class TimeBlock
        {
            public string Name;
            public int Games, Wins;
            public double WinRate => Games == 0 ? 0 : (double)Wins / Games;
        }

        public const int MinGames = 5;

        public Ladder.Format Format;
        public DateTime? SeasonEnd;
        public double? DaysLeft;
        /// <summary>Ranked games a day in this format over the last two weeks you played.</summary>
        public double GamesPerDay;
        public List<Goal> Goals = new List<Goal>();
        /// <summary>The win rate the goals use: this season's once it has 10+ finished games, else your last 20.</summary>
        public double WinRate;
        public int WinRateGames;
        public bool WinRateThisSeason;
        public int StarBonus;
        /// <summary>The star level where your bonus runs out; null when there's none or it lasts to Legend.</summary>
        public int? BonusEndsAt;
        public int Streak;
        public bool NextWinDoubles;
        /// <summary>Stars this season from the star bonus and from win streaks, on top of 1 a win.</summary>
        public int BonusStars, StreakStars;
        public List<DeckClimb> Decks = new List<DeckClimb>();
        public Opponents Opp = new Opponents();
        public SessionCheck Session;
        public List<TimeBlock> Times = new List<TimeBlock>();
        public TimeBlock BestTime;

        /// <param name="all">All your saved card games (any mode), any order.</param>
        /// <param name="decks">Your decks with their expected win rate against the field, when known.</param>
        public static LadderReport Make(Ladder.Format f, IEnumerable<GameRecord> all, IEnumerable<(string Name, string Class, double? Expected)> decks,
            DateTime? seasonEndUtc, DateTime now)
        {
            var allList = all.OrderBy(g => g.Start).ToList();
            var ranked = allList.Where(g => GameModes.Of(g.GameType) == GameModes.Kind.Ranked && FormatOf(g) == f.FormatType).ToList();
            var r = new LadderReport { Format = f, StarBonus = Math.Max(1, f.StarBonus) };
            var cur = f.Current;

            // Season end and pace.
            var nowUtc = now.ToUniversalTime();
            if (seasonEndUtc is DateTime end && end > nowUtc) { r.SeasonEnd = end; r.DaysLeft = (end - nowUtc).TotalDays; }
            r.GamesPerDay = PerDay(ranked, now);

            // Streak and bonus.
            r.Streak = ranked.Where(g => g.Decided).Reverse().TakeWhile(g => g.Won).Count();
            r.NextWinDoubles = r.Streak >= 2 && !cur.IsLegend && cur.Level < LadderRules.DiamondFive;
            if (!cur.IsLegend) r.BonusEndsAt = LadderRules.BonusEndsAtLevel(cur.Level, r.StarBonus);
            (r.BonusStars, r.StreakStars) = ExtraStars(f.Latest.Games);

            // Win rate: this season's when there's enough of it (last season's games may be from a much higher rank).
            var season = f.Latest.Games.Where(g => g.Decided).ToList();
            if (season.Count >= 10) { r.WinRate = (double)season.Count(g => g.Won) / season.Count; r.WinRateGames = season.Count; r.WinRateThisSeason = true; }
            else { r.WinRate = f.RecentWinRate; r.WinRateGames = f.RecentGames; }

            // Goals: the next floor, Diamond 5 and Legend, whichever are ahead.
            if (!cur.IsLegend && r.WinRateGames >= MinGames)
            {
                var targets = new List<int>();
                var nextFloor = Enumerable.Range(cur.Level + 1, Math.Max(0, 50 - cur.Level)).FirstOrDefault(LadderRules.IsFloor);
                if (nextFloor > 0 && nextFloor < LadderRules.DiamondFive) targets.Add(nextFloor);
                if (cur.Level < LadderRules.DiamondFive) targets.Add(LadderRules.DiamondFive);
                targets.Add(LadderRules.LegendLevel);
                foreach (var t in targets)
                {
                    var games = LadderEstimate.GamesToReach(cur.Level, t, r.WinRate, cur.Stars, r.StarBonus, r.Streak);
                    var goal = new Goal { Target = Ladder.Name(t), Level = t, Games = games };
                    if (games.HasValue && r.GamesPerDay > 0) goal.EtaDate = now.Date.AddDays(Math.Ceiling(games.Value / r.GamesPerDay));
                    if (games.HasValue && r.DaysLeft.HasValue) goal.PerDayNeeded = games.Value / Math.Max(r.DaysLeft.Value, 0.04);
                    goal.InTime = games.HasValue && r.SeasonEnd.HasValue && goal.EtaDate.HasValue && goal.EtaDate.Value <= r.SeasonEnd.Value.ToLocalTime();
                    r.Goals.Add(goal);
                }
            }

            r.Decks = Climbers(ranked, decks, now, cur);
            r.Opp = OpponentRanks(ranked);
            r.Session = Check(ranked, allList, now);
            (r.Times, r.BestTime) = ByTimeOfDay(allList);
            return r;
        }

        public static string FormatOf(GameRecord g) => string.IsNullOrEmpty(g.Format) || g.Format == "FT_UNKNOWN" ? "FT_STANDARD" : g.Format;

        /// <summary>Ranked games a day over the last 14 days (or since your first game, if that's more recent).</summary>
        public static double PerDay(List<GameRecord> ranked, DateTime now)
        {
            if (ranked.Count == 0) return 0;
            var from = now.AddDays(-14);
            var recent = ranked.Where(g => g.Start >= from).ToList();
            if (recent.Count == 0) return 0;
            var since = ranked[0].Start > from ? ranked[0].Start : from;
            var days = Math.Max(1, (now - since).TotalDays);
            return recent.Count / days;
        }

        /// <summary>Stars from the star bonus and from win streaks, on top of the 1 star a win is worth.</summary>
        public static (int Bonus, int Streak) ExtraStars(IEnumerable<GameRecord> seasonGames)
        {
            int bonusStars = 0, streakStars = 0, run = 0;
            foreach (var g in seasonGames.Where(x => x.Decided).OrderBy(x => x.Start))
            {
                if (!g.Won) { run = 0; continue; }
                run++;
                if (g.StarLevel <= 0 || g.StarLevel >= LadderRules.LegendLevel || g.LegendRank > 0) continue;
                var bonus = Math.Max(1, g.StarBonus);
                var stars = LadderRules.WinStars(g.StarLevel, bonus, run);
                var mult = stars / bonus;
                bonusStars += (bonus - 1) * mult;
                streakStars += bonus * (mult - 1);
            }
            return (bonusStars, streakStars);
        }

        /// <summary>
        /// Net stars a game, below Diamond 5: a win is worth 1 star, 2 on a streak (the two games before it won too,
        /// so about winRate^2 of the time), and a loss costs 1. From Diamond 5 up there's no streak bonus.
        /// </summary>
        public static double StarsPerGame(double winRate, bool streaks) => winRate * (1 + (streaks ? winRate * winRate : 0)) - (1 - winRate);

        public static List<DeckClimb> Climbers(List<GameRecord> ranked, IEnumerable<(string Name, string Class, double? Expected)> decks, DateTime now, Ladder.Rank cur)
        {
            var streaks = !cur.IsLegend && cur.Level < LadderRules.DiamondFive;
            var recent = ranked.Where(g => g.Decided && g.Start >= now.AddDays(-60) && !string.IsNullOrEmpty(g.DeckName)).ToList();
            var timed = recent.Where(Timed).ToList();
            double? overallMinutes = timed.Count > 0 ? timed.Average(g => g.Duration.TotalMinutes) : (double?)null;
            var byName = new Dictionary<string, DeckClimb>(StringComparer.OrdinalIgnoreCase);
            foreach (var grp in recent.GroupBy(g => g.DeckName, StringComparer.OrdinalIgnoreCase))
            {
                var t = grp.Where(Timed).ToList();
                byName[grp.Key] = new DeckClimb
                {
                    Name = grp.Key, Class = grp.Last().MyClass, Games = grp.Count(), Wins = grp.Count(g => g.Won),
                    Minutes = t.Count > 0 ? t.Average(g => g.Duration.TotalMinutes) : (double?)null,
                };
            }
            foreach (var (name, cls, expected) in decks ?? Enumerable.Empty<(string, string, double?)>())
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (!byName.TryGetValue(name, out var d)) byName[name] = d = new DeckClimb { Name = name, Class = cls };
                d.Expected = expected;
                d.Class ??= cls;
            }
            foreach (var d in byName.Values)
            {
                var minutes = d.Minutes ?? overallMinutes;
                if (minutes is double m && m > 0)
                {
                    if (d.Games >= MinGames) d.YourStarsPerHour = StarsPerGame(d.WinRate, streaks) * 60 / m;
                    if (d.Expected is double e) d.MetaStarsPerHour = StarsPerGame(e, streaks) * 60 / m;
                }
            }
            return byName.Values.OrderByDescending(d => d.Best ?? double.MinValue).ThenByDescending(d => d.Games).ToList();
        }

        private static bool Timed(GameRecord g) => g.End > g.Start && g.Duration.TotalMinutes >= 1 && g.Duration.TotalMinutes <= 60;

        /// <summary>Your and your opponents' ranks over the last 10 ranked games where both were recorded.</summary>
        public static Opponents OpponentRanks(List<GameRecord> ranked)
        {
            static double Level(int level, int legend) => legend > 0 || level >= LadderRules.LegendLevel ? LadderRules.LegendLevel : level;
            var known = ranked.Where(g => (g.OpponentStarLevel > 0 || g.OpponentLegendRank > 0) && (g.StarLevel > 0 || g.LegendRank > 0)).ToList();
            var last = known.Skip(Math.Max(0, known.Count - 10)).ToList();
            if (last.Count == 0) return new Opponents();
            return new Opponents
            {
                Games = last.Count,
                AverageTheirs = last.Average(g => Level(g.OpponentStarLevel, g.OpponentLegendRank)),
                AverageYours = last.Average(g => Level(g.StarLevel, g.LegendRank)),
            };
        }

        public static SessionCheck Check(List<GameRecord> ranked, List<GameRecord> all, DateTime now)
        {
            if (ranked.Count == 0) return null;
            var s = Ladder.Sessions(ranked).Last();
            var lastGame = s.Games[s.Games.Count - 1];
            var lastEnd = lastGame.End > lastGame.Start ? lastGame.End : lastGame.Start;
            var c = new SessionCheck { Session = s, Current = now - lastEnd < Ladder.SessionGap };
            c.NetStars = NetStars(s.Games, ranked);
            c.LossStreak = s.Games.Where(g => g.Decided).Reverse().TakeWhile(g => !g.Won).Count();
            int n = 0, wins = 0;
            foreach (var sess in Ladder.Sessions(all.Where(g => g.Decided).ToList()))
            {
                var losses = 0;
                foreach (var g in sess.Games)
                {
                    if (losses >= 3) { n++; if (g.Won) wins++; }
                    losses = g.Won ? 0 : losses + 1;
                }
            }
            c.AfterThreeLossesGames = n;
            if (n >= MinGames) c.AfterThreeLosses = (double)wins / n;
            return c;
        }

        /// <summary>
        /// Stars won or lost over these games: the rank change from the first game to the last, plus the last game's
        /// result by the ladder rules (its after-rank isn't recorded). Null when a rank is missing or in Legend.
        /// </summary>
        public static int? NetStars(List<GameRecord> session, List<GameRecord> ranked)
        {
            if (session.Count == 0 || session.Any(g => g.StarLevel <= 0 || g.StarLevel >= LadderRules.LegendLevel || g.LegendRank > 0)) return null;
            var first = session[0];
            var last = session[session.Count - 1];
            var idx = LadderRules.StarIndex(last.StarLevel, last.Stars);
            var change = idx - LadderRules.StarIndex(first.StarLevel, first.Stars);
            if (!last.Decided) return change;
            if (last.Won)
            {
                var run = ranked.Where(g => g.Decided && g.Start <= last.Start).Reverse().TakeWhile(g => g.Won).Count();
                return change + LadderRules.WinStars(last.StarLevel, last.StarBonus, run);
            }
            return change - (idx > LadderRules.FloorAt(idx) ? 1 : 0);
        }

        /// <summary>
        /// The gaming day (see <see cref="DaySession"/>) with the best record in these games: most wins over losses,
        /// then most wins, then the latest. Null without a decided game.
        /// </summary>
        public static (DateTime Day, int Wins, int Losses)? BestDay(IEnumerable<GameRecord> games)
        {
            var best = (games ?? Enumerable.Empty<GameRecord>()).Where(g => g != null && g.Decided)
                .GroupBy(g => DaySession.DayStart(g.Start).Date)
                .Select(g => (Day: g.Key, Wins: g.Count(x => x.Won), Losses: g.Count(x => !x.Won)))
                .OrderByDescending(x => x.Wins - x.Losses).ThenByDescending(x => x.Wins).ThenByDescending(x => x.Day)
                .ToList();
            return best.Count == 0 ? ((DateTime, int, int)?)null : best[0];
        }

        /// <summary>Ranked and casual games by the hour they started; the best block needs 5+ games and another block to compare.</summary>
        public static (List<TimeBlock>, TimeBlock) ByTimeOfDay(List<GameRecord> all)
        {
            var blocks = new List<(string Name, int From, int To)> { ("Morning", 6, 12), ("Afternoon", 12, 17), ("Evening", 17, 22), ("Late night", 22, 6) };
            bool In(int h, int from, int to) => from < to ? h >= from && h < to : h >= from || h < to;
            var games = all.Where(g => g.Decided && GameModes.Of(g.GameType) is GameModes.Kind.Ranked or GameModes.Kind.Casual).ToList();
            var list = blocks.Select(b =>
            {
                var gs = games.Where(g => In(g.Start.Hour, b.From, b.To)).ToList();
                return new TimeBlock { Name = b.Name, Games = gs.Count, Wins = gs.Count(g => g.Won) };
            }).ToList();
            var enough = list.Where(t => t.Games >= MinGames).ToList();
            var best = enough.Count >= 2 ? enough.OrderByDescending(t => t.WinRate).First() : null;
            return (list, best);
        }
    }
}
