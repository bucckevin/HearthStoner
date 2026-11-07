using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>The numbers the History page shows for one game.</summary>
    public sealed class GameSummary
    {
        public sealed class Point
        {
            /// <summary>Your turn number (1, 2, 3...).</summary>
            public int Round;
            /// <summary>You minus them: health + armor + board attack + board health.</summary>
            public int Value;
        }

        public GameRecord Game;
        /// <summary>One point per round, taken after the opponent's turn (when your board has faced theirs), plus
        /// the final state. Taking both sides' turns would zig-zag: everyone looks ahead right after they play.</summary>
        public List<Point> Swing = new List<Point>();
        public int Rounds;
        public int RoundsAhead;
        /// <summary>The round with the biggest drop from the round before, and the drop (positive); 0 if none.</summary>
        public int TurningRound;
        public int TurningDrop;
        public int BestLead;
        public int BestLeadRound;

        public static int Value(TurnSnapshot t) =>
            (t.MyHp + t.MyArmor + t.MyBoardAttack + t.MyBoardHealth) - (t.TheirHp + t.TheirArmor + t.TheirBoardAttack + t.TheirBoardHealth);

        public static GameSummary From(GameRecord g)
        {
            var s = new GameSummary { Game = g, Rounds = (g.Turns + 1) / 2 };
            var snaps = g.Snapshots.OrderBy(t => t.Turn).ToList();
            for (var i = 0; i < snaps.Count; i++)
            {
                var t = snaps[i];
                if (!t.Yours || i == snaps.Count - 1)
                {
                    var round = (t.Turn + 1) / 2;
                    if (s.Swing.Count > 0 && s.Swing[s.Swing.Count - 1].Round == round) s.Swing[s.Swing.Count - 1].Value = Value(t);
                    else s.Swing.Add(new Point { Round = round, Value = Value(t) });
                }
            }
            s.RoundsAhead = s.Swing.Count(p => p.Value > 0);
            for (var i = 1; i < s.Swing.Count; i++)
            {
                var drop = s.Swing[i - 1].Value - s.Swing[i].Value;
                if (drop > s.TurningDrop) { s.TurningDrop = drop; s.TurningRound = s.Swing[i].Round; }
            }
            foreach (var p in s.Swing) if (p.Value > s.BestLead) { s.BestLead = p.Value; s.BestLeadRound = p.Round; }
            return s;
        }
    }

    /// <summary>Headline numbers for the Stats page.</summary>
    public sealed class StatsSummary
    {
        public sealed class Row
        {
            public string Name;
            /// <summary>Class key (e.g. PRIEST) for matchup rows.</summary>
            public string Class;
            public int Wins, Losses;
            public int Games => Wins + Losses;
            public double WinRate => Games == 0 ? 0 : (double)Wins / Games;
        }

        public int Wins, Losses;
        public int Games => Wins + Losses;
        public double WinRate => Games == 0 ? 0 : (double)Wins / Games;
        public Row First = new Row { Name = "Going first" }, Coin = new Row { Name = "On the coin" };
        /// <summary>Results of the most recent games, oldest first (true = win).</summary>
        public List<bool> Recent = new List<bool>();
        /// <summary>The games behind <see cref="Recent"/>, in the same order.</summary>
        public List<GameRecord> RecentGames = new List<GameRecord>();
        public List<Row> ByDeck = new List<Row>();
        public List<Row> ByClass = new List<Row>();
        public string Rank;
        /// <summary>Your rank at each game, oldest first: legend rank (lower is better) or star level (higher is better).</summary>
        public List<(DateTime When, int Legend, int Stars)> RankHistory = new List<(DateTime, int, int)>();

        public static StatsSummary From(List<GameRecord> games)
        {
            var s = new StatsSummary();
            var ranked = games.Where(g => g.Decided).OrderBy(g => g.Start).ToList();
            foreach (var g in ranked)
            {
                if (g.Won) s.Wins++; else s.Losses++;
                var r = g.WentFirst ? s.First : s.Coin;
                if (g.Won) r.Wins++; else r.Losses++;
            }
            s.RecentGames = ranked.Skip(Math.Max(0, ranked.Count - 30)).ToList();
            s.Recent = s.RecentGames.Select(g => g.Won).ToList();
            Row Bump(List<Row> rows, string name, string cls, bool won)
            {
                var row = rows.FirstOrDefault(x => x.Name == name);
                if (row == null) rows.Add(row = new Row { Name = name, Class = cls });
                if (won) row.Wins++; else row.Losses++;
                return row;
            }
            foreach (var g in ranked)
            {
                Bump(s.ByDeck, g.DeckName ?? g.MyArchetype ?? MetaStats.ClassName(g.MyClass), g.MyClass, g.Won);
                Bump(s.ByClass, MetaStats.ClassName(g.OpponentClass ?? "?"), g.OpponentClass, g.Won);
            }
            s.ByDeck = s.ByDeck.OrderByDescending(r => r.Games).ToList();
            s.ByClass = s.ByClass.OrderByDescending(r => r.Games).ToList();
            s.Rank = ranked.LastOrDefault(g => !string.IsNullOrEmpty(g.Rank))?.Rank;
            s.RankHistory = ranked.Where(g => g.LegendRank > 0 || g.StarLevel > 0).Select(g => (g.Start, g.LegendRank, g.StarLevel)).ToList();
            return s;
        }
    }
}
