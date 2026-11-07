using System;
using System.Collections.Generic;
using System.Linq;
using HackF5.UnitySpy.HearthstoneLib;

namespace HearthStoner.Live
{
    /// <summary>Ladder ranks as Hearthstone stores them (star levels 1-50, legend rank), turned into names and meta brackets.</summary>
    public static class Ranks
    {
        private static readonly string[] Leagues = { "Bronze", "Silver", "Gold", "Platinum", "Diamond" };

        /// <summary>The player's rank in the given format, or null when the player is unknown.</summary>
        public static IRank Of(IPlayer player, GameFormat format)
        {
            if (player == null) return null;
            return format switch
            {
                GameFormat.FT_WILD => player.Wild,
                GameFormat.FT_CLASSIC => player.Classic,
                GameFormat.FT_TWIST => player.Twist,
                _ => player.Standard,
            };
        }

        /// <summary>"Legend 512", "Silver 8" or "unranked".</summary>
        /// <param name="rank">The rank; must not be null.</param>
        public static string Name(IRank rank)
        {
            if (rank == null) throw new ArgumentNullException(nameof(rank));
            return Name(rank.StarLevel, rank.LegendRank);
        }

        /// <summary>
        /// "Legend 512", "Silver 8" or "unranked", from a star level (1 = Bronze 10, 50 = Diamond 1) and legend rank.
        /// This is the text saved in <see cref="GameRecord.Rank"/> and read back by the app, so its format must not
        /// change; for display use <see cref="Ladder.Name"/>.
        /// </summary>
        public static string Name(int starLevel, int legendRank)
        {
            if (legendRank > 0) return $"Legend {legendRank}";
            if (starLevel <= 0) return "unranked";
            var league = Math.Min((starLevel - 1) / 10, Leagues.Length - 1);
            var rank = 10 - (starLevel - 1) % 10;
            return $"{Leagues[league]} {rank}";
        }

        /// <summary>"Name#1234 - Gold 3": the player's BattleTag (or name) and rank in the format.</summary>
        public static string Describe(IPlayer player, GameFormat format)
        {
            if (player == null) return "?";
            var rank = Of(player, format);
            var tag = player.BattleTag != null && !string.IsNullOrEmpty(player.BattleTag.Name)
                ? $"{player.BattleTag.Name}#{player.BattleTag.Number}"
                : player.Name;
            return rank == null ? tag : $"{tag} - {Name(rank)}";
        }

        /// <summary>
        /// The meta stats bracket to use. Returns the setting itself unless it is "auto"; then the bracket of the rank
        /// in your most recent ranked game (from <paramref name="games"/>), or "all" when there is none.
        /// </summary>
        /// <param name="settings">Your settings; must not be null.</param>
        /// <param name="games">Saved games, oldest first; when null they are loaded from disk.</param>
        public static string MetaBracket(Settings settings, IReadOnlyList<GameRecord> games = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!string.Equals(settings.MetaRank, "auto", StringComparison.OrdinalIgnoreCase)) return settings.MetaRank;
            var last = (games ?? GameRecord.LoadAll()).LastOrDefault(g => g.StarLevel > 0 || g.LegendRank > 0);
            return last == null ? "all" : BracketFor(last.StarLevel, last.LegendRank);
        }

        /// <summary>The meta stats bracket for a rank: legend, diamond, platinum or bronze-gold.</summary>
        public static string BracketFor(int starLevel, int legendRank)
        {
            if (legendRank > 0 || starLevel > 50) return "legend";
            if (starLevel > 40) return "diamond";
            if (starLevel > 30) return "platinum";
            return "bronze-gold";
        }
    }
}
