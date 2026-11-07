using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HearthStoner.Meta
{
    /// <summary>How one card does in an archetype (all games, or one matchup).</summary>
    public sealed class CardStat
    {
        public string CardId;
        public int InStartingDeck;
        public int DrawnBeforeMulligan;
        public int KeptInMulligan;
        public int InHandAfterMulligan;
        public int InHandAfterMulliganThenWin;
        public int Drawn;
        public int DrawnThenWin;

        public double KeepRate => DrawnBeforeMulligan > 0 ? Math.Min(1.0, (double)KeptInMulligan / DrawnBeforeMulligan) : 0;
        public double MulliganWinRate => InHandAfterMulligan > 0 ? (double)InHandAfterMulliganThenWin / InHandAfterMulligan : 0;
        public double DrawnWinRate => Drawn > 0 ? (double)DrawnThenWin / Drawn : 0;

        public void Add(CardStat o)
        {
            InStartingDeck += o.InStartingDeck;
            DrawnBeforeMulligan += o.DrawnBeforeMulligan;
            KeptInMulligan += o.KeptInMulligan;
            InHandAfterMulligan += o.InHandAfterMulligan;
            InHandAfterMulliganThenWin += o.InHandAfterMulliganThenWin;
            Drawn += o.Drawn;
            DrawnThenWin += o.DrawnThenWin;
        }
    }

    public sealed class MetaArchetype
    {
        public int Id;
        public string Key;          // "egg-priest"
        public string Name;         // "Egg Priest"
        public string Class;        // "priest" (Firestone's lowercase class key)
        public int Games;
        public int Wins;
        public double WinRate => Games > 0 ? (double)Wins / Games : 0;
        public double Share;        // of all games in the format
        public double ClassShare;   // of this class's games
        public List<string> CoreCards = new List<string>();
        public Dictionary<string, CardStat> Cards = new Dictionary<string, CardStat>();

        /// <summary>Generic bucket for decks Firestone couldn't classify ("priest").</summary>
        public bool IsCatchAll => string.Equals(Key, Class, StringComparison.OrdinalIgnoreCase);

        /// <summary>Average copies of a card in this archetype's starting decks (0..2).</summary>
        public double Copies(string cardId) =>
            Games > 0 && Cards.TryGetValue(cardId, out var s) ? Math.Min(2.0, (double)s.InStartingDeck / Games) : 0;
    }

    /// <summary>How games went when this card was picked from a Discover (or similar choice).</summary>
    public sealed class DiscoverStat
    {
        public string CardId;
        public int Discovered;
        public int Wins;
        public double WinRate => Discovered > 0 ? (double)Wins / Discovered : 0;
    }

    /// <summary>One popular decklist from the meta.</summary>
    public sealed class MetaDeck
    {
        public string DeckCode;
        public string ArchetypeKey;     // matches MetaArchetype.Key
        public string Class;
        public int Games;
        public int Wins;
        public double WinRate => Games > 0 ? (double)Wins / Games : 0;
    }

    public sealed class Matchup
    {
        public Dictionary<string, DiscoverStat> Discover = new Dictionary<string, DiscoverStat>();
        public string OpponentClass;
        public int Games;
        public int Wins;
        public double WinRate => Games > 0 ? (double)Wins / Games : 0;
        public Dictionary<string, CardStat> Cards = new Dictionary<string, CardStat>();
    }

    public sealed class ArchetypeDetail
    {
        public MetaArchetype Archetype;
        public Dictionary<string, DiscoverStat> Discover = new Dictionary<string, DiscoverStat>();
        public Dictionary<string, Matchup> Matchups = new Dictionary<string, Matchup>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ranked Constructed stats from Firestone's public data (static.zerotoheroes.com): archetypes with win rates,
    /// popularity, per-card mulligan/draw stats and per-matchup splits. Cached locally; refreshed every few hours.
    /// </summary>
    public sealed class MetaStats
    {
        private const string Root = "https://static.zerotoheroes.com/api/constructed/stats/archetypes";
        private static readonly TimeSpan OverviewMaxAge = TimeSpan.FromHours(3);
        private static readonly TimeSpan DetailMaxAge = TimeSpan.FromHours(12);
        public const int MinArchetypeGames = 100;

        public string Format { get; private set; }
        public string Rank { get; private set; }
        public string Period { get; private set; }
        public DateTime Updated { get; private set; }
        public int TotalGames { get; private set; }
        public List<MetaArchetype> Archetypes { get; } = new List<MetaArchetype>();

        private readonly Dictionary<int, ArchetypeDetail> _details = new Dictionary<int, ArchetypeDetail>();
        private List<MetaDeck> _decks;

        /// <summary>The most played decklists for this format/rank/period (downloaded on first use, cached).</summary>
        public List<MetaDeck> Decks(Action<string> log = null)
        {
            if (_decks != null) return _decks;
            var url = $"https://static.zerotoheroes.com/api/constructed/stats/decks/{Format}/{Rank}/{Period}/overview-from-hourly.gz.json";
            var cache = Path.Combine(CacheDir, $"{Format}-{Rank}-{Period}-decks.json");
            var json = Fetch(url, cache, OverviewMaxAge, log);
            var list = new List<MetaDeck>();
            if (json != null)
            {
                foreach (var d in JObject.Parse(json)["deckStats"] as JArray ?? new JArray())
                {
                    var code = (string)d["decklist"];
                    var games = (int?)d["totalGames"] ?? 0;
                    if (string.IsNullOrEmpty(code) || games < 30) continue;
                    list.Add(new MetaDeck
                    {
                        DeckCode = code,
                        ArchetypeKey = (string)d["archetypeName"] ?? "",
                        Class = ((string)d["playerClass"] ?? "").ToLowerInvariant(),
                        Games = games,
                        Wins = (int?)d["totalWins"] ?? 0,
                    });
                }
            }
            _decks = list.OrderByDescending(x => x.Games).ToList();
            return _decks;
        }

        /// <summary>"this morning" (5 AM-noon), "this afternoon" (to 5 PM), "this evening" (to 9 PM), else "tonight".</summary>
        public static string PartOfDay(DateTime t) =>
            t.Hour >= 5 && t.Hour < 12 ? "this morning" : t.Hour >= 12 && t.Hour < 17 ? "this afternoon" : t.Hour >= 17 && t.Hour < 21 ? "this evening" : "tonight";

        public static string CacheDir
        {
            get
            {
                var d = AppPaths.Data("meta");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// <param name="rank">all, bronze-gold, platinum, diamond, legend, legend-diamond, top-2000-legend</param>
        /// <param name="period">past-3, past-7, past-20, last-patch, current-season</param>
        public static MetaStats Load(string format = "standard", string rank = "all", string period = "past-7", Action<string> log = null)
        {
            var url = $"{Root}/{format}/{rank}/{period}/overview-from-hourly.gz.json";
            var cache = Path.Combine(CacheDir, $"{format}-{rank}-{period}-archetypes.json");
            var json = Fetch(url, cache, OverviewMaxAge, log);
            var meta = new MetaStats { Format = format, Rank = rank, Period = period };
            if (json != null) meta.Parse(JObject.Parse(json));
            return meta;
        }

        private void Parse(JObject root)
        {
            Updated = (DateTime?)root["lastUpdated"] ?? DateTime.MinValue;
            var list = new List<MetaArchetype>();
            foreach (var a in root["archetypeStats"] as JArray ?? new JArray())
            {
                var arch = ParseArchetype(a);
                if (arch != null) list.Add(arch);
            }
            var allGames = list.Sum(a => a.Games);
            var minGames = Math.Max(MinArchetypeGames, allGames / 1000);
            Archetypes.AddRange(list.Where(a => a.Games >= minGames).OrderByDescending(a => a.Games));
            // Shares are of the archetypes we keep (tiny ones are too noisy to show), so they add up to 100%.
            TotalGames = Archetypes.Sum(a => a.Games);
            foreach (var a in Archetypes)
            {
                a.Share = TotalGames > 0 ? (double)a.Games / TotalGames : 0;
                var classGames = Archetypes.Where(x => x.Class == a.Class).Sum(x => x.Games);
                a.ClassShare = classGames > 0 ? (double)a.Games / classGames : 0;
            }
        }

        private static MetaArchetype ParseArchetype(JToken a)
        {
            var games = (int?)a["totalGames"] ?? 0;
            if (games <= 0) return null;
            var arch = new MetaArchetype
            {
                Id = (int?)a["id"] ?? 0,
                Key = (string)a["name"] ?? "",
                Class = ((string)a["heroCardClass"] ?? "").ToLowerInvariant(),
                Games = games,
                Wins = (int?)a["totalWins"] ?? 0,
            };
            arch.Name = Prettify(arch.Key, arch.Class);
            foreach (var c in a["coreCards"] as JArray ?? new JArray())
                if (c.Type == JTokenType.String) arch.CoreCards.Add((string)c);

            // Card stats come split by going first / on the coin; add them up. Detail files also have a top-level list.
            var top = a["cardsData"] as JArray;
            if (top != null) MergeCards(arch.Cards, top);
            else
                foreach (var side in a["coinPlayInfo"] as JArray ?? new JArray())
                    MergeCards(arch.Cards, side["cardsData"] as JArray);
            return arch;
        }

        private static void MergeCards(Dictionary<string, CardStat> into, JArray cards)
        {
            if (cards == null) return;
            foreach (var c in cards)
            {
                var id = (string)c["cardId"];
                if (string.IsNullOrEmpty(id)) continue;
                var s = new CardStat
                {
                    CardId = id,
                    InStartingDeck = (int?)c["inStartingDeck"] ?? 0,
                    DrawnBeforeMulligan = (int?)c["drawnBeforeMulligan"] ?? 0,
                    KeptInMulligan = (int?)c["keptInMulligan"] ?? 0,
                    InHandAfterMulligan = (int?)c["inHandAfterMulligan"] ?? 0,
                    InHandAfterMulliganThenWin = (int?)c["inHandAfterMulliganThenWin"] ?? 0,
                    Drawn = (int?)c["drawn"] ?? 0,
                    DrawnThenWin = (int?)c["drawnThenWin"] ?? 0,
                };
                if (into.TryGetValue(id, out var existing)) existing.Add(s);
                else into[id] = s;
            }
        }

        /// <summary>Per-matchup card stats for one archetype (downloaded on demand, cached).</summary>
        public ArchetypeDetail Detail(int archetypeId, Action<string> log = null)
        {
            lock (_details)
            {
                if (_details.TryGetValue(archetypeId, out var cached)) return cached;
            }
            var url = $"{Root}/{Format}/{Rank}/{Period}/archetype/{archetypeId}.gz.json";
            var cache = Path.Combine(CacheDir, $"{Format}-{Rank}-{Period}-archetype-{archetypeId}.json");
            var json = Fetch(url, cache, DetailMaxAge, log);
            if (json == null) return null;

            var root = JObject.Parse(json);
            var detail = new ArchetypeDetail { Archetype = ParseArchetype(root) };
            if (detail.Archetype == null) return null;
            detail.Archetype.Name = Archetypes.FirstOrDefault(a => a.Id == archetypeId)?.Name ?? detail.Archetype.Name;
            ParseDiscover(detail.Discover, root["discoverData"] as JArray);
            foreach (var m in root["matchupInfo"] as JArray ?? new JArray())
            {
                var mu = new Matchup
                {
                    OpponentClass = ((string)m["opponentClass"] ?? "").ToLowerInvariant(),
                    Games = (int?)m["totalGames"] ?? 0,
                    Wins = (int?)m["wins"] ?? 0,
                };
                if (m["cardsData"] is JArray cd) MergeCards(mu.Cards, cd);
                else foreach (var side in m["coinPlayInfo"] as JArray ?? new JArray()) MergeCards(mu.Cards, side["cardsData"] as JArray);
                ParseDiscover(mu.Discover, m["discoverData"] as JArray);
                if (mu.OpponentClass.Length > 0) detail.Matchups[mu.OpponentClass] = mu;
            }
            lock (_details) _details[archetypeId] = detail;
            return detail;
        }

        private static void ParseDiscover(Dictionary<string, DiscoverStat> into, JArray data)
        {
            if (data == null) return;
            foreach (var d in data)
            {
                var id = (string)d["cardId"];
                if (string.IsNullOrEmpty(id)) continue;
                var st = into.TryGetValue(id, out var e) ? e : (into[id] = new DiscoverStat { CardId = id });
                st.Discovered += (int?)d["discovered"] ?? 0;
                st.Wins += (int?)d["discoveredThenWin"] ?? 0;
            }
        }

        // ------------------------------------------------------------------ archetype matching

        public IEnumerable<MetaArchetype> ForClass(string heroClass) =>
            Archetypes.Where(a => string.Equals(a.Class, Key(heroClass), StringComparison.OrdinalIgnoreCase));

        /// <summary>Firestone uses lowercase class keys ("deathknight"); HearthstoneJSON uses "DEATHKNIGHT".</summary>
        public static string Key(string heroClass) => (heroClass ?? "").Replace("_", "").ToLowerInvariant();

        /// <summary>
        /// The archetype a full decklist belongs to: the one of its class whose core cards it holds the largest
        /// share of (at least 60%), otherwise the one whose typical cards overlap most with the list.
        /// </summary>
        public MetaArchetype MatchDeck(string heroClass, IEnumerable<string> deck)
        {
            var cards = new HashSet<string>(deck ?? Enumerable.Empty<string>());
            if (cards.Count == 0) return null;
            MetaArchetype best = null;
            double bestScore = 0;
            foreach (var a in ForClass(heroClass).Where(a => !a.IsCatchAll))
            {
                var core = a.CoreCards.Distinct().ToList();
                if (core.Count == 0) continue;
                var score = (double)core.Count(cards.Contains) / core.Count;
                if (score >= 0.6 && (best == null || score > bestScore || (score == bestScore && a.Games > best.Games))) { best = a; bestScore = score; }
            }
            if (best != null) return best;

            foreach (var a in ForClass(heroClass))
            {
                var score = cards.Average(c => Math.Min(1.0, a.Copies(c)));
                if (score > bestScore) { best = a; bestScore = score; }
            }
            return bestScore >= 0.4 ? best : null;
        }

        // ------------------------------------------------------------------ download + cache

        private static string Fetch(string url, string cachePath, TimeSpan maxAge, Action<string> log)
        {
            var fresh = File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < maxAge;
            if (fresh) return File.ReadAllText(cachePath);
            try
            {
                var text = Decode(Web.GetBytes(url, 60));
                File.WriteAllText(cachePath, text);
                return text;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[meta] Couldn't download {url}: {ex.Message}{(File.Exists(cachePath) ? " (using cached copy)" : "")}");
                return File.Exists(cachePath) ? File.ReadAllText(cachePath) : null;
            }
        }

        /// <summary>The files are gzip; usually the server says so and HttpClient unpacks them, but not always.</summary>
        private static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            {
                using (var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
                using (var r = new StreamReader(gz, Encoding.UTF8))
                    return r.ReadToEnd();
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static readonly HashSet<string> Acronyms = new HashSet<string> { "dk", "dh", "xl", "otk", "bbu", "bub", "ubb", "fff", "uuu", "bbb", "bfu", "bbf", "fuu" };

        /// <summary>"xl-rafaamlock" becomes "XL Rafaamlock"; a bucket named after its class becomes "Other Warlock".</summary>
        public static string Prettify(string key, string heroClass)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            if (string.Equals(key, heroClass, StringComparison.OrdinalIgnoreCase)) return "Other " + ClassName(heroClass);
            return string.Join(" ", key.Split('-').Where(w => w.Length > 0)
                .Select(w => Acronyms.Contains(w) ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        public static string ClassName(string heroClass)
        {
            switch (Key(heroClass))
            {
                case "deathknight": return "Death Knight";
                case "demonhunter": return "Demon Hunter";
                case "": return "?";
                default:
                    var k = Key(heroClass);
                    return char.ToUpperInvariant(k[0]) + k.Substring(1);
            }
        }
    }
}
