using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace HearthStoner
{
    /// <summary>One of your quests: its definition from the game's Quest table and your progress.</summary>
    public sealed class QuestEntry
    {
        public int Id;
        public string Name;
        /// <summary>The goal as the game words it, with $q filled in ("Cast 20 spells.").</summary>
        public string Text;
        /// <summary>The Quest table's icon (Cards, Games, Damage_Heroes, Battlegrounds, ...): a hint at the kind of goal.</summary>
        public string Icon;
        public int Quota, Progress, Status, Xp;
        /// <summary>"Daily", "Weekly" or "Event", from the quest's pool.</summary>
        public string Pool;
        /// <summary>The weekday the pool hands out new quests (0 = Sunday), -1 for every day.</summary>
        public int ResetDay = -1;
        /// <summary>The reward track the XP goes to: 1 the main one, 2 Battlegrounds.</summary>
        public int RewardTrack = 1;

        [JsonIgnore] public int Left => Math.Max(0, Quota - Progress);
        [JsonIgnore] public bool Done => Quota > 0 && Progress >= Quota;
        /// <summary>
        /// Still to do. The game keeps finished and rerolled quests in memory with their status: 1 new, 2 active,
        /// 3+ completed or rewarded, 6 rerolled. 0 is a quest saved before statuses were read.
        /// </summary>
        [JsonIgnore] public bool Open => !Done && Status <= 2;
        public const int Rerolled = 6;

        public static string Fill(string template, int quota) => Clean((template ?? "").Replace("$q", quota.ToString()));

        /// <summary>Drops the game's text markup ("&lt;i&gt;(2x - Legendary)&lt;/i&gt;") and extra spaces.</summary>
        public static string Clean(string text) =>
            Regex.Replace(Regex.Replace(text ?? "", @"<[^>]*>", " "), @"\s+", " ").Replace(" .", ".").Trim();

        public QuestEntry With(int progress, int status)
        {
            var c = (QuestEntry)MemberwiseClone();
            c.Progress = progress; c.Status = status;
            return c;
        }
    }

    public sealed class TrackState
    {
        public int Level, Xp, XpNeeded, TotalXp, BonusPercent;
        [JsonIgnore] public int ToNext => Math.Max(0, XpNeeded - Xp);
    }

    /// <summary>
    /// Your quests and reward track, saved to %LocalAppData%\HearthStoner\quests.json whenever HearthStoner reads them from
    /// memory at the menu, so the Play page shows them while Hearthstone is closed.
    /// </summary>
    public sealed class QuestLog
    {
        public DateTime Updated;
        public List<QuestEntry> Quests = new List<QuestEntry>();
        public TrackState Track;
        /// <summary>When the ranked season (and the ladder) resets, in UTC, as the game's own countdown has it.</summary>
        public DateTime? SeasonEnd;

        /// <summary>"Season ends in 24 days", or null when unknown or already past (a new season not read yet).</summary>
        public static string SeasonEndsText(DateTime? endUtc, DateTime nowUtc)
        {
            if (endUtc == null || endUtc <= nowUtc) return null;
            var left = endUtc.Value - nowUtc;
            if (left.TotalHours >= 48) return $"Season ends in {(int)left.TotalDays} days";
            if (left.TotalHours >= 1) return $"Season ends in {(int)left.TotalHours} h";
            return $"Season ends in {Math.Max(1, (int)left.TotalMinutes)} min";
        }

        /// <summary>HEARTHSTONER_QUESTS points somewhere else (tests and screenshots with a sample file).</summary>
        public static string Path => Environment.GetEnvironmentVariable("HEARTHSTONER_QUESTS") is string p && p != ""
            ? p : AppPaths.Data("quests.json");

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "Quests"); }
        }

        public static QuestLog Load()
        {
            try
            {
                if (File.Exists(Path))
                {
                    var log = JsonConvert.DeserializeObject<QuestLog>(File.ReadAllText(Path));
                    foreach (var q in log?.Quests ?? new List<QuestEntry>()) q.Text = QuestEntry.Clean(q.Text);   // files saved before the cleanup
                    return log;
                }
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "Quests"); }
            return null;
        }
    }

    /// <summary>How one quest goal moves in a game: a count per game, times the deck's win rate for "win" goals.</summary>
    public sealed class QuestRule
    {
        public Func<GameRecord, double> Count;
        public bool Wins;
    }

    /// <summary>
    /// Which of your decks finishes your quests fastest, from what each deck did in your saved games, and which daily
    /// to reroll. The Quest table only says the goal in words (plus an icon), so a small rule table reads the words.
    /// </summary>
    public sealed class QuestPlan
    {
        public sealed class Estimate
        {
            public QuestEntry Quest;
            public double PerGame;
            public double Games => PerGame > 0 ? Quest.Left / PerGame : double.PositiveInfinity;
        }

        public sealed class DeckScore
        {
            public PlayerDeck Deck;
            public int GamesPlayed;
            public double WinRate;
            /// <summary>Quest XP per game: each quest's XP times the share of it one game finishes (at most all of it).</summary>
            public double XpPerGame;
            public List<Estimate> Quests = new List<Estimate>();
            public string Reason;
        }

        /// <summary>Decks you've played, most quest XP per game first.</summary>
        public List<DeckScore> Decks = new List<DeckScore>();
        public DeckScore Best => Decks.FirstOrDefault(d => d.XpPerGame > 0);
        /// <summary>Games to finish each open quest with the deck best at it; missing: can't estimate.</summary>
        public Dictionary<int, double> GamesFor = new Dictionary<int, double>();
        public QuestEntry Reroll;
        /// <summary>Games to finish every open weekly the rules understand, with one deck; null when there's none.</summary>
        public double? WeekliesGames;
        public DateTime? WeeklyReset;
        /// <summary>Whole days (at least 1) from the time the plan was made until <see cref="WeeklyReset"/>; null without a reset day.</summary>
        public int? DaysUntilWeeklyReset;

        /// <summary>Whole days from <paramref name="now"/> until <paramref name="reset"/>, rounded up, and at least 1.</summary>
        public static int DaysUntil(DateTime reset, DateTime now) => Math.Max(1, (int)Math.Ceiling((reset - now).TotalDays));

        public static QuestPlan Make(QuestLog log, List<PlayerDeck> decks, List<GameRecord> games, CardDb cards, Func<PlayerDeck, double?> expected, DateTime now)
        {
            var plan = new QuestPlan();
            var open = log?.Quests.Where(q => q.Open && (q.Pool == "Daily" || q.Pool == "Weekly")).ToList() ?? new List<QuestEntry>();
            if (open.Count == 0) return plan;
            var rules = open.Select(q => (Quest: q, Rule: RuleFor(q, cards))).Where(x => x.Rule != null).ToList();
            var byDeck = games.Where(g => g.Decided).GroupBy(g => DeckVersions.DeckOf(g, decks, cards)).Where(gr => gr.Key != null).ToDictionary(gr => gr.Key, gr => gr.ToList());
            foreach (var kv in byDeck)
            {
                var played = kv.Value;
                var wr = expected?.Invoke(kv.Key) ?? (double)played.Count(g => g.Won) / played.Count;
                var s = new DeckScore { Deck = kv.Key, GamesPlayed = played.Count, WinRate = wr };
                foreach (var (q, rule) in rules)
                {
                    var per = played.Average(g => rule.Count(g)) * (rule.Wins ? wr : 1);
                    s.Quests.Add(new Estimate { Quest = q, PerGame = per });
                    if (q.Left > 0) s.XpPerGame += q.Xp * Math.Min(1, per / q.Left);
                }
                s.Reason = Reason(s.Quests);
                plan.Decks.Add(s);
            }
            plan.Decks = plan.Decks.OrderByDescending(s => s.XpPerGame).ThenByDescending(s => s.GamesPlayed).ToList();
            if (plan.Decks.Count == 0) return plan;

            foreach (var (q, _) in rules)
                plan.GamesFor[q.Id] = plan.Decks.Min(s => s.Quests.First(e => e.Quest == q).Games);
            // One daily reroll a day: a rerolled daily still in memory means today's is used.
            if (!log.Quests.Any(q => q.Pool == "Daily" && q.Status == QuestEntry.Rerolled))
                plan.Reroll = PickReroll(open.Where(q => q.Pool == "Daily"), plan.GamesFor);

            var weeklies = rules.Where(x => x.Quest.Pool == "Weekly").Select(x => x.Quest).ToList();
            if (weeklies.Count > 0)
            {
                var g = plan.Decks.Min(s => s.Quests.Where(e => weeklies.Contains(e.Quest)).Max(e => e.Games));
                if (!double.IsInfinity(g)) plan.WeekliesGames = g;
                var day = weeklies[0].ResetDay;
                if (day >= 0)
                {
                    var n = (day - (int)now.DayOfWeek + 7) % 7;
                    plan.WeeklyReset = now.Date.AddDays(n == 0 ? 7 : n);
                    plan.DaysUntilWeeklyReset = DaysUntil(plan.WeeklyReset.Value, now);
                }
            }
            return plan;
        }

        /// <summary>The daily that takes the most games to finish (fewest XP per game on a tie); none if every one is quick.</summary>
        public static QuestEntry PickReroll(IEnumerable<QuestEntry> dailies, Dictionary<int, double> gamesFor) =>
            dailies.Where(q => gamesFor.ContainsKey(q.Id) && gamesFor[q.Id] > 2)
                .OrderByDescending(q => gamesFor[q.Id]).ThenBy(q => q.Xp / Math.Max(1, gamesFor[q.Id])).FirstOrDefault();

        /// <summary>"~2 games: Cast 20 spells + Win 3 games": the quests this deck finishes soonest.</summary>
        private static string Reason(List<Estimate> est)
        {
            var quick = est.Where(e => e.PerGame > 0 && e.Quest.Left > 0).OrderBy(e => e.Games).ToList();
            if (quick.Count == 0) return null;
            var limit = Math.Max(quick[0].Games * 2, 3);
            var take = quick.Where(e => e.Games <= limit).ToList();
            var n = Math.Max(1, (int)Math.Ceiling(take.Max(e => e.Games) - 0.05));
            return $"~{n} game{(n == 1 ? "" : "s")}: " + string.Join(" + ", take.Select(e => e.Quest.Text.TrimEnd('.', ' ')));
        }

        // ------------------------------------------------------------------ the rule table

        private static readonly string[] OtherModes = { "Battlegrounds", "Arena", "TavernBrawl", "Social" };

        private static readonly Dictionary<string, string> RaceWords = new Dictionary<string, string>
        {
            ["murloc"] = "MURLOC", ["beast"] = "BEAST", ["dragon"] = "DRAGON", ["naga"] = "NAGA", ["mech"] = "MECHANICAL",
            ["elemental"] = "ELEMENTAL", ["demon"] = "DEMON", ["pirate"] = "PIRATE", ["totem"] = "TOTEM", ["undead"] = "UNDEAD",
            ["draenei"] = "DRAENEI", ["quilboar"] = "QUILBOAR",
        };

        /// <summary>How a quest's goal moves in a game, or null when it can't be estimated from your saved games.</summary>
        public static QuestRule RuleFor(QuestEntry q, CardDb cards)
        {
            if (q == null || OtherModes.Contains(q.Icon)) return null;   // goals outside constructed games
            // "Draw 50 cards in Traditional Modes. (Ranked +1, Legendary +1)": the bracket lists event bonuses, not the goal.
            var t = Regex.Replace((q.Text ?? "").ToLowerInvariant(), @"\([^)]*\)", " ");
            t = Regex.Replace(t, @"\s+in traditional modes", "");
            t = Regex.Replace(t, @"\s+", " ").Trim().TrimEnd('.').Trim();
            Match m;
            QuestRule R(Func<GameRecord, double> f, bool wins = false) => new QuestRule { Count = f, Wins = wins };
            IEnumerable<CardInfo> Plays(GameRecord g) => g.Snapshots.SelectMany(s => s.Played).Select(cards.ById).Where(c => c != null && c.Type != "HERO_POWER");
            QuestRule Cards(Func<CardInfo, bool> f) => R(g => Plays(g).Count(f));
            double Ranked(GameRecord g) => g.GameType == "GT_RANKED" ? 1 : 0;

            if (Regex.IsMatch(t, @"^win \d+ games? of hearthstone's ranked")) return R(Ranked, wins: true);
            if (Regex.IsMatch(t, @"^win \d+ games?$")) return R(g => 1, wins: true);
            if (Regex.IsMatch(t, @"^play \d+ games? of hearthstone's ranked")) return R(Ranked);
            if ((m = Regex.Match(t, @"^play \d+ games? as (.+)$")).Success)
            {
                var classes = new HashSet<string>(Regex.Split(m.Groups[1].Value, @",? or |, ").Select(c => c.Replace(" ", "").ToUpperInvariant()));
                return R(g => classes.Contains(g.MyClass ?? "") ? 1 : 0);
            }
            if (Regex.IsMatch(t, @"^play \d+ games?$")) return R(g => 1);
            if (Regex.IsMatch(t, @"^use your hero power \d+ times?$"))
                return R(g => g.Snapshots.SelectMany(s => s.Played).Count(id => cards.ById(id)?.Type == "HERO_POWER"));
            if (Regex.IsMatch(t, @"^deal \d+ damage to enemy heroes$")) return R(HeroDamage);
            if (Regex.IsMatch(t, @"^spend \d+ mana$")) return R(g => g.Snapshots.Where(s => s.Yours).Sum(s => Math.Max(0, s.ManaAvailable - s.ManaUnspent)));
            if (Regex.IsMatch(t, @"^take \d+ turns$")) return R(g => g.Snapshots.Count(s => s.Yours));
            if (Regex.IsMatch(t, @"^destroy \d+ minions$")) return R(g => g.Snapshots.Sum(s => s.TheirMinionsDied));
            if (Regex.IsMatch(t, @"^draw \d+ cards$")) return R(Drawn);
            if (Regex.IsMatch(t, @"^(play|cast) \d+ spells$")) return Cards(c => c.Type == "SPELL");
            if ((m = Regex.Match(t, @"^cast \d+ (\w+) or (\w+) spells$")).Success)
            {
                var a = m.Groups[1].Value.ToUpperInvariant(); var b = m.Groups[2].Value.ToUpperInvariant();
                return Cards(c => c.Type == "SPELL" && (c.SpellSchool == a || c.SpellSchool == b));
            }
            if ((m = Regex.Match(t, @"^play \d+ minions? that costs? (\d+)$")).Success) { var n = int.Parse(m.Groups[1].Value); return Cards(c => c.Type == "MINION" && c.Cost == n); }
            if (Regex.IsMatch(t, @"^play \d+ minions$")) return Cards(c => c.Type == "MINION");
            if ((m = Regex.Match(t, @"^play \d+ cards? that costs? (\d+) or (more|less)$")).Success)
            {
                var n = int.Parse(m.Groups[1].Value); var more = m.Groups[2].Value == "more";
                return Cards(c => c.Cost is int k && (more ? k >= n : k <= n));
            }
            if ((m = Regex.Match(t, @"^play \d+ cards? that costs? (\d+)$")).Success) { var n = int.Parse(m.Groups[1].Value); return Cards(c => c.Cost == n); }
            if ((m = Regex.Match(t, @"^play \d+ (odd|even)-cost cards$")).Success) { var odd = m.Groups[1].Value == "odd" ? 1 : 0; return Cards(c => c.Cost is int k && k % 2 == odd); }
            if (Regex.IsMatch(t, @"^play \d+ weapons$")) return Cards(c => c.Type == "WEAPON");
            if (Regex.IsMatch(t, @"^play \d+ cards$")) return Cards(c => true);
            if ((m = Regex.Match(t, @"^play \d+ ([a-z]+) cards$")).Success)
            {
                // "Play 50 Battlecry cards": a keyword the card data knows. Set names ("Lost City cards") aren't.
                var mech = m.Groups[1].Value.ToUpperInvariant();
                if (cards.All.Any(c => c.Has(mech))) return Cards(c => c.Has(mech));
                return null;
            }
            if ((m = Regex.Match(t, @"^play \d+ ([a-z ,/]+)$")).Success)
            {
                var words = Regex.Split(m.Groups[1].Value, @" and/or |, | or | and ").Select(w => w.Trim()).ToList();
                var races = words.Select(w => RaceWords.TryGetValue(w, out var r) ? r : w.EndsWith("s") && RaceWords.TryGetValue(w.Substring(0, w.Length - 1), out r) ? r : null).ToList();
                if (races.All(r => r != null)) { var set = new HashSet<string>(races); return Cards(c => c.Type == "MINION" && c.Race != null && set.Contains(c.Race)); }
            }
            return null;
        }

        /// <summary>Damage to the enemy hero: every drop in their health plus armor from one turn's end to the next.</summary>
        private static double HeroDamage(GameRecord g)
        {
            var total = 0;
            var prev = -1;
            foreach (var s in g.Snapshots)
            {
                var now = s.TheirHp + s.TheirArmor;
                if (prev >= 0 && now < prev) total += prev - now;
                prev = now;
            }
            return total;
        }

        /// <summary>Cards drawn after the opening hand: every drop in your deck count.</summary>
        private static double Drawn(GameRecord g)
        {
            var total = 0;
            var prev = -1;
            foreach (var s in g.Snapshots)
            {
                if (prev >= 0 && s.MyDeck < prev) total += prev - s.MyDeck;
                prev = s.MyDeck;
            }
            return total;
        }
    }
}
