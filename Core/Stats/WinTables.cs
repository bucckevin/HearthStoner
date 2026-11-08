using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>Win-rate tables for the Stats page: a rolling win rate over time, and records split two ways.</summary>
    public static class WinTables
    {
        /// <summary>Wins and losses in one cell of a <see cref="Cross"/> table.</summary>
        public readonly struct Cell
        {
            public Cell(int wins, int losses) { Wins = wins; Losses = losses; }
            public int Wins { get; }
            public int Losses { get; }
            public int Games => Wins + Losses;
            public double WinRate => Games == 0 ? 0 : (double)Wins / Games;
        }

        /// <summary>
        /// The win rate over the last <paramref name="window"/> decided games, at each decided game from the
        /// <paramref name="window"/>th on (oldest first). Empty with fewer games than the window.
        /// </summary>
        public static List<double> Rolling(IEnumerable<GameRecord> games, int window)
        {
            if (window <= 0) throw new ArgumentOutOfRangeException(nameof(window), "The window needs at least one game.");
            var results = (games ?? Enumerable.Empty<GameRecord>()).Where(g => g != null && g.Decided).OrderBy(g => g.Start).Select(g => g.Won ? 1 : 0).ToList();
            var list = new List<double>();
            var wins = 0;
            for (var i = 0; i < results.Count; i++)
            {
                wins += results[i];
                if (i >= window) wins -= results[i - window];
                if (i >= window - 1) list.Add((double)wins / window);
            }
            return list;
        }

        /// <summary>
        /// Decided games split by two keys (say deck and opponent class): (row, column) -> wins and losses. Games with
        /// a null or empty key are left out.
        /// </summary>
        public static Dictionary<(string Row, string Col), Cell> Cross(IEnumerable<GameRecord> games, Func<GameRecord, string> row, Func<GameRecord, string> col)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (col == null) throw new ArgumentNullException(nameof(col));
            return (games ?? Enumerable.Empty<GameRecord>())
                .Where(g => g != null && g.Decided && !string.IsNullOrEmpty(row(g)) && !string.IsNullOrEmpty(col(g)))
                .GroupBy(g => (row(g), col(g)))
                .ToDictionary(gr => gr.Key, gr => new Cell(gr.Count(g => g.Won), gr.Count(g => !g.Won)));
        }
    }
}