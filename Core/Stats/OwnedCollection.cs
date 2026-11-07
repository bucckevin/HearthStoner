using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HackF5.UnitySpy.HearthstoneLib;
using Newtonsoft.Json;

namespace HearthStoner
{
    /// <summary>
    /// How many copies of each card you own (any finish), saved to %LocalAppData%\HearthStoner\collection.json whenever
    /// HearthStoner reads your collection from memory, so crafting advice works when Hearthstone isn't running.
    /// </summary>
    public sealed class OwnedCollection
    {
        public DateTime Updated { get; set; }
        public Dictionary<string, int> Owned { get; set; } = new Dictionary<string, int>();

        public static string Path =>
            AppPaths.Data("collection.json");

        public int Count(string cardId) => cardId != null && Owned.TryGetValue(cardId, out var n) ? n : 0;

        /// <summary>
        /// Copies owned, also counting other printings of the same card (e.g. the CORE_ reprint of a card) by name,
        /// since the game lets you play whichever version you own.
        /// </summary>
        public int CountByName(CardDb cards, string cardId)
        {
            var name = cards.ById(cardId)?.Name;
            if (name == null) return Count(cardId);
            return Owned.Where(kv => cards.ById(kv.Key)?.Name == name).Sum(kv => kv.Value);
        }

        public static OwnedCollection FromMemory(IEnumerable<ICollectionCard> cards)
        {
            var c = new OwnedCollection { Updated = DateTime.Now };
            foreach (var card in cards)
            {
                var total = card.Count + card.PremiumCount + card.DiamondCount + card.SignatureCount;
                if (total > 0 && card.CardId != null) c.Owned[card.CardId] = (c.Owned.TryGetValue(card.CardId, out var n) ? n : 0) + total;
            }
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonConvert.SerializeObject(this));
            }
            catch { }
        }

        public static OwnedCollection Load()
        {
            try
            {
                if (File.Exists(Path)) return JsonConvert.DeserializeObject<OwnedCollection>(File.ReadAllText(Path));
            }
            catch { }
            return null;
        }

        /// <summary>Dust to craft one copy.</summary>
        public static int CraftCost(string rarity)
        {
            switch (rarity)
            {
                case "LEGENDARY": return 1600;
                case "EPIC": return 400;
                case "RARE": return 100;
                case "COMMON": return 40;
                default: return 0;   // free / core cards can't be crafted
            }
        }
    }
}
