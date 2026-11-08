using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Pages
{
    /// <summary>Everything the app's pages show, loaded in one go off the UI thread.</summary>
    public sealed class AppData
    {
        public AppServices Services;
        public MetaStats Meta;
        public OwnedCollection Collection;
        public List<PlayerDeck> Decks = new List<PlayerDeck>();
        public List<DeckPlanner.DeckPick> Picks = new List<DeckPlanner.DeckPick>();
        public DeckPlanner.CraftPlan Craft;
        /// <summary>Your quests as last read from memory (null before the first read), and which deck finishes them fastest.</summary>
        public QuestLog Quests;
        public QuestPlan QuestPlan = new QuestPlan();
        /// <summary>Newest first.</summary>
        public List<GameRecord> Games = new List<GameRecord>();
        /// <summary>Battlegrounds and Mercenaries games (mode, times and result only), newest first.</summary>
        public List<GameRecord> OtherGames = new List<GameRecord>();
        /// <summary>Stats over every card mode (the same as <see cref="AllStats"/>).</summary>
        public StatsSummary Stats;
        /// <summary>The text parts of the stats report (habits, tilt, ladder, recurring notes), every card mode.</summary>
        public List<AdviceSection> StatsNotes = new List<AdviceSection>();
        /// <summary>The Stats page's two views: ranked and casual games (the ladder), and every card mode.</summary>
        public StatsView LadderStats = new StatsView(), AllStats = new StatsView();

        /// <summary>Everything the Stats page shows for one set of games.</summary>
        public sealed class StatsView
        {
            /// <summary>Newest first.</summary>
            public List<GameRecord> Games = new List<GameRecord>();
            public StatsSummary Stats;
            public ManaStats Mana;
            public List<AdviceSection> Notes = new List<AdviceSection>();
        }
        public DeckPlanner Planner;
        public ManaStats Mana;
        /// <summary>What changed in the meta since <see cref="MetaSince"/> (empty on the first day).</summary>
        public List<MetaHistory.Alert> MetaAlerts = new List<MetaHistory.Alert>();
        public DateTime? MetaSince;
        public string PlayerName;
        public string Rank;
        public string Error;

        public CardDb Cards => Services.Cards;

        public static AppData Load(AppServices svc)
        {
            var d = new AppData { Services = svc };
            try
            {
                d.Meta = svc.Meta();
                d.Collection = OwnedCollection.Load();
                d.Decks = DeckPlanner.FromCodes(DecksLog.SavedDecks(svc.InstallDir), svc.Cards);
                // Decks you pasted in on the Decks page: analyzed the same way, kept out of the Play ranking.
                foreach (var imp in svc.Settings?.ImportedDecks ?? new List<ImportedDeck>())
                {
                    var cardsIn = DeckCode.Decode(imp.Code, svc.Cards);
                    if (cardsIn == null || cardsIn.Count == 0) continue;
                    d.Decks.Add(new PlayerDeck { Name = imp.Name, Code = imp.Code, Class = DeckCode.HeroClass(imp.Code, svc.Cards), Cards = cardsIn, Imported = true });
                }
                // Battlegrounds and Mercenaries games are listed in History only; stats, replays and coaching are about the card game.
                var all = GameRecord.LoadAll();
                d.OtherGames = all.Where(g => !GameModes.IsCardGame(g.GameType)).OrderByDescending(g => g.Start).ToList();
                var games = all.Where(g => GameModes.IsCardGame(g.GameType)).ToList();
                d.Games = games.OrderByDescending(g => g.Start).ToList();
                d.Planner = new DeckPlanner(d.Meta, svc.Cards, d.Collection, svc.Log ?? (_ => { }));
                d.Picks = d.Planner.PickDecks(d.Decks);
                d.Craft = d.Planner.Craft();
                d.Quests = QuestLog.Load();
                d.QuestPlan = QuestPlan.Make(d.Quests, d.Decks, games, svc.Cards,
                    dk => d.PickFor(dk) is DeckPlanner.DeckPick p && p.Archetype != null ? p.Expected : (double?)null, DateTime.Now);
                d.AllStats = View(d, svc, games);
                d.LadderStats = View(d, svc, games.Where(g => GameModes.HasLadderRank(g.GameType)).ToList());
                d.Stats = d.AllStats.Stats;
                d.Mana = d.AllStats.Mana;
                d.StatsNotes = d.AllStats.Notes;
                try
                {
                    var history = MetaHistory.Load();
                    var snap = MetaHistory.Take(d.Meta, d.Picks, DateTime.Today);
                    var since = history.Baseline(snap);
                    d.MetaAlerts = MetaHistory.Compare(since, snap);
                    d.MetaSince = since?.Day;
                    if (d.Meta.Archetypes.Count > 0) { history.Add(snap); history.Save(); }
                }
                catch (Exception ex) { svc.Log?.Invoke("[meta history] " + ex.Message); }
                d.PlayerName = (svc.PlayerName ?? svc.Settings?.LastBattleTag)?.Split('#')[0];
                d.Rank = d.Stats.Rank;
            }
            catch (Exception ex) { d.Error = ex.Message; }
            return d;
        }

        /// <summary>Stats, mana use and the stats report's notes for these games (the report's tables are drawn by the page).</summary>
        private static StatsView View(AppData d, AppServices svc, List<GameRecord> games)
        {
            var notes = StatsReport.Build(games, d.Meta, svc.Cards, (key, id) => d.Meta.Detail(id, svc.Log ?? (_ => { })));
            return new StatsView
            {
                Games = games.OrderByDescending(g => g.Start).ToList(),
                Stats = StatsSummary.From(games),
                Mana = ManaStats.From(games),
                Notes = notes.Where(s => s.Title != "Overall" && s.Title != "By deck" && s.Title != "By matchup").ToList(),
            };
        }

        public DeckPlanner.DeckPick PickFor(PlayerDeck deck) => Picks.FirstOrDefault(p => p.Deck == deck);

        /// <summary>"Legend 10932" -> "Legend 10,932".</summary>
        public static string PrettyRank(string rank)
        {
            if (string.IsNullOrEmpty(rank)) return null;
            var parts = rank.Split(' ');
            return parts.Length == 2 && int.TryParse(parts[1], out var n) && n >= 1000 ? $"{parts[0]} {n:N0}" : rank;
        }

        public static string RankBracketName(string rank)
        {
            switch (rank)
            {
                case "legend": return "Legend";
                case "legend-diamond": return "Diamond and Legend";
                case "top-2000-legend": return "top 2000 Legend";
                case "diamond": return "Diamond";
                case "platinum": return "Platinum";
                case "bronze-gold": return "Bronze to Gold";
                default: return "all ranks";
            }
        }

        public static string PeriodName(string period)
        {
            switch (period)
            {
                case "past-3": return "last 3 days";
                case "past-7": return "last 7 days";
                case "past-20": return "last 20 days";
                case "last-patch": return "since the last patch";
                case "current-season": return "this season";
                default: return period;
            }
        }
    }
}
