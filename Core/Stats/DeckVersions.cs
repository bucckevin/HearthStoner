using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>
    /// The versions of one of your decks: each distinct 30-card list you played with it (from your game records),
    /// plus the current list if you haven't played it yet, with the results of each and the cards that changed.
    /// </summary>
    public sealed class DeckVersions
    {
        public sealed class Version
        {
            public int Number;
            public List<string> Cards = new List<string>();
            public DateTime? First, Last;
            public int Wins, Losses;
            public int Games => Wins + Losses;
            public double WinRate => Games == 0 ? 0 : (double)Wins / Games;
            public bool IsCurrent;
            /// <summary>Compared with the version before: card id -> copies added / removed.</summary>
            public Dictionary<string, int> Added = new Dictionary<string, int>(), Removed = new Dictionary<string, int>();
        }

        public PlayerDeck Deck;
        /// <summary>Oldest first.</summary>
        public List<Version> Versions = new List<Version>();

        /// <summary>Lists that share at least this many cards count as the same deck when the names differ.</summary>
        public const int SameDeckOverlap = 24;

        /// <summary>
        /// The 30 cards you built: without the copies a card adds when the game starts (Commander Beatrix: "Ten copies
        /// join your deck!"). Older records were saved without those copies, so comparing built lists keeps them from
        /// looking like a different version.
        /// </summary>
        public static List<string> Built(IEnumerable<string> list, CardDb cards)
        {
            var l = (list ?? Enumerable.Empty<string>()).ToList();
            if (cards == null || l.Count <= 30) return l;
            foreach (var owner in l.Distinct().ToList())
            {
                var n = DeckCode.CopiesJoining(cards.ById(owner)?.Text);
                if (n <= 0) continue;
                var pick = l.GroupBy(x => x).Where(g => g.Key != owner && g.Count() >= n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                for (var i = 0; i < n && pick != null; i++) l.Remove(pick);
            }
            return l;
        }

        public static string Key(IEnumerable<string> cards) => string.Join(",", (cards ?? Enumerable.Empty<string>()).OrderBy(c => c, StringComparer.Ordinal));

        public static int Overlap(IEnumerable<string> a, IEnumerable<string> b)
        {
            var counts = (a ?? Enumerable.Empty<string>()).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            return (b ?? Enumerable.Empty<string>()).GroupBy(x => x).Sum(g => counts.TryGetValue(g.Key, out var n) ? Math.Min(n, g.Count()) : 0);
        }

        /// <summary>Which of your decks a game was played with: the same name, else the closest list of the same class.</summary>
        public static PlayerDeck DeckOf(GameRecord g, List<PlayerDeck> decks, CardDb cards = null)
        {
            if (g.DeckCards == null || g.DeckCards.Count == 0) return null;
            var sameClass = decks.Where(d => !d.Imported && string.Equals(d.Class, g.MyClass, StringComparison.OrdinalIgnoreCase)).ToList();
            var byName = sameClass.Where(d => d.Name == g.DeckName).ToList();
            if (byName.Count == 1) return byName[0];
            return (byName.Count > 1 ? byName : sameClass)
                .Select(d => (Deck: d, Overlap: Overlap(Built(d.Cards, cards), Built(g.DeckCards, cards))))
                .Where(x => x.Overlap >= SameDeckOverlap || byName.Count > 1)
                .OrderByDescending(x => x.Overlap).Select(x => x.Deck).FirstOrDefault();
        }

        public static DeckVersions For(PlayerDeck deck, List<PlayerDeck> allDecks, List<GameRecord> games, CardDb cards = null)
        {
            var v = new DeckVersions { Deck = deck };
            var mine = games.Where(g => g.Decided && DeckOf(g, allDecks, cards) == deck).OrderBy(g => g.Start).ToList();
            var byKey = new Dictionary<string, Version>();
            foreach (var g in mine)
            {
                var built = Built(g.DeckCards, cards);
                var key = Key(built);
                if (!byKey.TryGetValue(key, out var ver))
                {
                    ver = new Version { Cards = built, First = g.Start };
                    byKey[key] = ver;
                    v.Versions.Add(ver);
                }
                ver.Last = g.Start;
                if (g.Won) ver.Wins++; else ver.Losses++;
            }
            var current = Built(deck.Cards, cards);
            var currentKey = Key(current);
            if (!byKey.ContainsKey(currentKey)) v.Versions.Add(new Version { Cards = current });
            v.Versions = v.Versions.OrderBy(x => x.First ?? DateTime.MaxValue).ToList();
            // A list you went back to sorts by when you first played it; the current one is marked wherever it falls.
            for (var i = 0; i < v.Versions.Count; i++)
            {
                var ver = v.Versions[i];
                ver.Number = i + 1;
                ver.IsCurrent = Key(ver.Cards) == currentKey;
                if (i == 0) continue;
                var before = v.Versions[i - 1].Cards.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
                var now = ver.Cards.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
                foreach (var kv in now) { before.TryGetValue(kv.Key, out var b); if (kv.Value > b) ver.Added[kv.Key] = kv.Value - b; }
                foreach (var kv in before) { now.TryGetValue(kv.Key, out var n); if (kv.Value > n) ver.Removed[kv.Key] = kv.Value - n; }
            }
            return v;
        }
    }
}
