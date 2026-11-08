namespace HearthStoner.Game
{
    /// <summary>Class names as the game writes them ("PALADIN", "DEATHKNIGHT"), and which of them are real classes.</summary>
    public static class HeroClasses
    {
        /// <summary>
        /// A playable class: not missing, "NEUTRAL" (Deathwing, Worldbreaker and other hero cards anyone can play,
        /// adventure bosses) or "INVALID".
        /// </summary>
        public static bool IsReal(string cls) => !string.IsNullOrEmpty(cls) && cls != "NEUTRAL" && cls != "INVALID";

        /// <summary>Whether two classes are the same; null when either isn't a real class, so it can't be told.</summary>
        public static bool? Same(string a, string b) => IsReal(a) && IsReal(b) ? a == b : (bool?)null;
    }
}
