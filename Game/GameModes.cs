namespace HearthStoner
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
