using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace HearthStoner
{
    public sealed class CardInfo
    {
        public string Id;       // e.g. "CORE_EX1_001"
        public int DbfId;
        public string Name;
        public string Set;
        public string Rarity;
        public string CardClass;
        public string Type;
        public int? Cost;
        public bool Collectible;
        public int? Attack;
        public int? Health;
        public int? Durability;
        public string Text = "";            // raw card text (with HTML-ish markup)
        public string[] Mechanics = new string[0];
        public string[] ReferencedTags = new string[0];
        public string Race;
        public string SpellSchool;

        public bool Has(string mechanic) => Array.IndexOf(Mechanics, mechanic) >= 0;
        public bool IsStandard => CardDb.StandardSets.Contains(Set ?? "");

        /// <summary>Profile derived from the card's text and keywords (removal, board clear, burn, ...).</summary>
        public CardProfile Profile => _profile ?? (_profile = CardProfile.Build(this));
        private CardProfile _profile;
    }

    /// <summary>
    /// Card names/metadata from HearthstoneJSON (a free HearthSim service), cached locally.
    /// Both HearthMirror and Power.log refer to cards by ID, so this turns IDs into readable names.
    /// </summary>
    public sealed class CardDb
    {
        private const string Url = "https://api.hearthstonejson.com/v1/latest/enUS/cards.json";

        /// <summary>
        /// Card sets legal in Standard (Year of the Scarab, after Reign of the Black Empire, October 2026).
        /// Update this when Standard rotates (every March). HearthstoneJSON doesn't say which format a set is in.
        /// </summary>
        public static readonly HashSet<string> StandardSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CORE", "EVENT", "EMERALD_DREAM", "THE_LOST_CITY", "TIME_TRAVEL", "CATACLYSM", "ESCAPEFROM_VIOLET_HOLD", "BE",
        };

        public IEnumerable<CardInfo> All => _byId.Values;
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);

        private readonly Dictionary<string, CardInfo> _byId = new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, CardInfo> _byDbf = new Dictionary<int, CardInfo>();

        public int Count => _byId.Count;

        public static CardDb Load()
        {
            var dir = AppPaths.DataDir;
            Directory.CreateDirectory(dir);
            var cache = Path.Combine(dir, "cards.json");

            var stale = !File.Exists(cache) || DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) > MaxAge;
            if (stale)
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("HearthStoner/1.0");
                        var json = http.GetStringAsync(Url).GetAwaiter().GetResult();
                        File.WriteAllText(cache, json);
                    }
                }
                catch (Exception ex)
                {
                    if (!File.Exists(cache))
                    {
                        Console.Error.WriteLine($"[cards] Couldn't download card database ({ex.Message}); card names will show as IDs.");
                        return new CardDb();
                    }
                    Console.Error.WriteLine($"[cards] Refresh failed ({ex.Message}); using cached copy.");
                }
            }

            var db = new CardDb();
            foreach (var c in JArray.Parse(File.ReadAllText(cache)))
            {
                var info = new CardInfo
                {
                    Id = (string)c["id"],
                    DbfId = (int?)c["dbfId"] ?? 0,
                    Name = (string)c["name"],
                    Set = (string)c["set"],
                    Rarity = (string)c["rarity"],
                    CardClass = (string)c["cardClass"],
                    Type = (string)c["type"],
                    Cost = (int?)c["cost"],
                    Collectible = (bool?)c["collectible"] ?? false,
                    Attack = (int?)c["attack"],
                    Health = (int?)c["health"],
                    Durability = (int?)c["durability"],
                    Text = (string)c["text"] ?? "",
                    Mechanics = StringArray(c["mechanics"]),
                    ReferencedTags = StringArray(c["referencedTags"]),
                    Race = (string)c["race"],
                    SpellSchool = (string)c["spellSchool"],
                };
                if (info.Id != null) db._byId[info.Id] = info;
                if (info.DbfId != 0) db._byDbf[info.DbfId] = info;
            }
            return db;
        }

        private static string[] StringArray(JToken t) =>
            t is JArray a ? a.Select(x => (string)x).Where(x => x != null).ToArray() : new string[0];

        public CardInfo ById(string id) => id != null && _byId.TryGetValue(id, out var c) ? c : null;
        public CardInfo ByDbf(int dbf) => _byDbf.TryGetValue(dbf, out var c) ? c : null;

        /// <summary>The Coin, in any of its cosmetic versions (GAME_005, JAIL_COIN5, ...).</summary>
        public bool IsCoin(string id) => !string.IsNullOrEmpty(id) && (id == "GAME_005" || ById(id)?.Name == "The Coin");

        public string NameOf(string id) => ById(id)?.Name ?? id ?? "?";

        /// <summary>HearthMirror cards sometimes carry a string ID, sometimes a numeric dbfId.</summary>
        public CardInfo Resolve(object idOrDbf)
        {
            switch (idOrDbf)
            {
                case null: return null;
                case int i: return ByDbf(i);
                case long l: return ByDbf((int)l);
                case string s when int.TryParse(s, out var n): return ByDbf(n) ?? ById(s);
                default: return ById(idOrDbf.ToString());
            }
        }
    }
}
