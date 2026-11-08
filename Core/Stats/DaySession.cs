using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>
    /// Today's games for the Play page: your record, time played and rank since the day began, and your latest
    /// results. A gaming day runs from 4 AM to 4 AM, so a late-night session stays one session past midnight.
    /// </summary>
    public sealed class DaySession
    {
        /// <summary>The hour a new gaming day starts.</summary>
        public const int DayStartHour = 4;

        /// <summary>When today's gaming day began.</summary>
        public DateTime Since;
        public int Wins, Losses, Games;
        /// <summary>Total length of today's games.</summary>
        public TimeSpan Played;
        /// <summary>Your rank when today's first ranked game started (null without one).</summary>
        public string RankAtStart;
        /// <summary>Your latest decided games, of any day, oldest first.</summary>
        public List<GameRecord> Recent = new List<GameRecord>();

        public double WinRate => Wins + Losses > 0 ? (double)Wins / (Wins + Losses) : 0;

        /// <summary>The start of the gaming day <paramref name="now"/> is in.</summary>
        public static DateTime DayStart(DateTime now) =>
            (now.Hour < DayStartHour ? now.Date.AddDays(-1) : now.Date).AddHours(DayStartHour);

        /// <summary>Today's numbers from <paramref name="games"/> (any order), and the last <paramref name="recent"/> results.</summary>
        public static DaySession For(IEnumerable<GameRecord> games, DateTime now, int recent = 10)
        {
            var all = (games ?? Enumerable.Empty<GameRecord>()).Where(g => g != null).OrderBy(g => g.Start).ToList();
            var since = DayStart(now);
            var today = all.Where(g => g.Start >= since && g.Start <= now).ToList();
            return new DaySession
            {
                Since = since,
                Games = today.Count,
                Wins = today.Count(g => g.Won),
                Losses = today.Count(g => g.Result == "LOST"),
                Played = TimeSpan.FromTicks(today.Where(g => g.End > g.Start).Sum(g => (g.End - g.Start).Ticks)),
                RankAtStart = today.FirstOrDefault(g => GameModes.HasLadderRank(g.GameType) && !string.IsNullOrEmpty(g.Rank))?.Rank,
                Recent = all.Where(g => g.Decided).Skip(Math.Max(0, all.Count(g => g.Decided) - recent)).ToList(),
            };
        }
    }
}