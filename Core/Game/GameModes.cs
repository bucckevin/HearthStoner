using System.Linq;

namespace HearthStoner.Game
{
    /// <summary>
    /// What kind of game a record or log is, from the names the game itself uses (its PegasusShared.GameType and
    /// FormatType enums, which Power.log prints at the start of every game: "GameType=GT_RANKED", "FormatType=FT_WILD").
    /// Card modes are the decks-and-minions game HearthStoner tracks and coaches; Battlegrounds and Mercenaries play
    /// differently, so only their mode, times and result are kept.
    /// </summary>
    public static class GameModes
    {
        public enum Kind { Unknown, Ranked, Casual, Friendly, Arena, Brawl, Solo, Duels, Battlegrounds, Mercenaries }

        public static Kind Of(string gameType)
        {
            var t = gameType ?? "";
            if (t.StartsWith("GT_BATTLEGROUNDS")) return Kind.Battlegrounds;
            if (t.StartsWith("GT_MERCENARIES") || t.StartsWith("GT_LETTUCE")) return Kind.Mercenaries;
            if (t.Contains("ARENA")) return Kind.Arena;                                     // Arena, Underground Arena, their vs-AI practice
            if (t.StartsWith("GT_TAVERNBRAWL") || t.StartsWith("GT_TB_") || t.StartsWith("GT_FSG_") || t.StartsWith("GT_HEARTHSTONE_DUOS")) return Kind.Brawl;
            if (t.StartsWith("GT_PVPDR")) return Kind.Duels;
            switch (t)
            {
                case "GT_RANKED": return Kind.Ranked;
                case "GT_CASUAL": return Kind.Casual;
                case "GT_VS_FRIEND": return Kind.Friendly;
                case "GT_VS_AI": case "GT_TUTORIAL": case "GT_TEST": case "GT_TEST_AI_VS_AI": case "GT_ACHIEVEMENT_TEST": return Kind.Solo;
                default: return Kind.Unknown;
            }
        }

        /// <summary>True for the card game (and for old records whose mode was never saved).</summary>
        public static bool IsCardGame(string gameType) => Of(gameType) != Kind.Battlegrounds && Of(gameType) != Kind.Mercenaries;

        /// <summary>Ranked and casual: modes where the rank memory reports is the ladder you play on.</summary>
        public static bool HasLadderRank(string gameType) => Of(gameType) is Kind.Ranked or Kind.Casual;

        /// <summary>Clears your and your opponent's ladder rank, stars and star bonus from a record.</summary>
        public static void ClearLadderInfo(GameRecord r)
        {
            if (r == null) throw new System.ArgumentNullException(nameof(r));
            r.Rank = null;
            r.StarLevel = 0;
            r.LegendRank = 0;
            r.Stars = 0;
            r.StarBonus = 0;
            r.OpponentStarLevel = 0;
            r.OpponentLegendRank = 0;
        }

        /// <summary>"1st", "2nd", "3rd", "5th": a Battlegrounds place.</summary>
        public static string Ordinal(int place) => place + (place % 100 is 11 or 12 or 13 ? "th" : (place % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        /// <summary>
        /// One line for a mode's games, newest first in <paramref name="games"/>: "Battlegrounds: 3 games, average place
        /// 4.3, top 4 in 2, last Nov 7" where places were saved, else "Mercenaries: 2 games, 1 win, last Nov 7".
        /// </summary>
        public static string Summary(string mode, System.Collections.Generic.IReadOnlyList<GameRecord> games)
        {
            if (games == null) throw new System.ArgumentNullException(nameof(games));
            if (games.Count == 0) return $"{mode}: no games";
            var n = games.Count;
            var placed = games.Where(x => x.Place > 0).ToList();
            string how;
            if (placed.Count == 1) how = $"finished {Ordinal(placed[0].Place)}";
            else if (placed.Count > 1) how = $"average place {placed.Average(x => x.Place):0.0}, top 4 in {placed.Count(x => x.Place <= 4)}";
            else
            {
                var w = games.Count(x => x.Won);
                how = $"{w} win{(w == 1 ? "" : "s")}";
            }
            return $"{mode}: {n} game{(n == 1 ? "" : "s")}, {how}, last {games[0].Start:MMM d}";
        }

        /// <summary>Whether the mode is known (not missing or GT_UNKNOWN).</summary>
        public static bool Known(string gameType) => !string.IsNullOrEmpty(gameType) && gameType != "GT_UNKNOWN";

        /// <summary>
        /// A readable name, e.g. "Ranked Wild", "Underground Arena", "Mercenaries"; null when unknown. Brief leaves out
        /// "Standard" (the usual format), for tight spaces.
        /// </summary>
        public static string Name(string gameType, string format, bool brief = false)
        {
            var f = format == "FT_WILD" ? " Wild" : format == "FT_CLASSIC" ? " Classic" : format == "FT_TWIST" ? " Twist" : brief ? "" : " Standard";
            switch (Of(gameType))
            {
                case Kind.Ranked: return "Ranked" + f;
                case Kind.Casual: return "Casual" + f;
                case Kind.Friendly: return "Friendly";
                case Kind.Arena: return gameType.Contains("UNDERGROUND") ? "Underground Arena" : "Arena";
                case Kind.Brawl: return gameType.StartsWith("GT_HEARTHSTONE_DUOS") ? "Duos" : "Tavern Brawl";
                case Kind.Solo: return "Solo";
                case Kind.Duels: return "Duels";
                case Kind.Battlegrounds: return gameType.Contains("DUO") ? "Battlegrounds Duos" : "Battlegrounds";
                case Kind.Mercenaries: return "Mercenaries";
                default: return null;
            }
        }
    }
}
