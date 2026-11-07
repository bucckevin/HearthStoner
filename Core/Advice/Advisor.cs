using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Advice
{
    public enum Tone { Normal, Good, Bad, Warn, Dim, Heading }

    public sealed class AdviceLine
    {
        public string Text;
        public Tone Tone;
        /// <summary>Drawn struck through (e.g. a card no longer in your deck).</summary>
        public bool Struck;

        // ---- card tile rows (the overlay draws these as card bars with art; the console prints Text)
        /// <summary>When set, the overlay draws this line as a card tile.</summary>
        public string CardId;
        public string CardName;
        public int? Cost;
        public string Rarity;
        /// <summary>Badge at the right end of the tile, e.g. "2" (copies left) or "45%".</summary>
        public string Badge;
        /// <summary>Small text shown just left of the badge, e.g. "13% next".</summary>
        public string Detail;
        /// <summary>Briefly highlight (e.g. the card you just drew).</summary>
        public bool Flash;
        /// <summary>Keep pulsing to draw the eye (lethal / danger).</summary>
        public bool Pulse;

        // ---- compact rows (the overlay draws these; the console still prints Text)
        /// <summary>Drawn as a small capitals label above the lines it introduces.</summary>
        public bool IsLabel;
        /// <summary>When set, drawn as a row of small chips (e.g. the turns each card in their hand was held).</summary>
        public List<Chip> Chips;
        /// <summary>When set, drawn as a row: <see cref="CardName"/>, <see cref="Aside"/>, then keyword tags (a minion on the board).</summary>
        public List<Chip> Tags;
        /// <summary>Text at the right end of a chips row; on a tag row, the minion's attack/health after its name (<see cref="CardName"/>).</summary>
        public string Aside;
        /// <summary>A short hint under a tag row (e.g. "ping it first").</summary>
        public string Hint;

        public AdviceLine(string text, Tone tone = Tone.Normal) { Text = text; Tone = tone; }

        /// <summary>A small capitals label line.</summary>
        public static AdviceLine Label(string text) => new AdviceLine(text, Tone.Dim) { IsLabel = true };

        public static AdviceLine Card(CardInfo info, string id, string text, Tone tone, string badge, string detail, bool struck = false) =>
            new AdviceLine(text, tone)
            {
                CardId = id,
                CardName = info?.Name ?? id,
                Cost = info?.Cost,
                Rarity = info?.Rarity,
                Badge = badge,
                Detail = detail,
                Struck = struck,
            };
    }

    /// <summary>One chip or tag in a compact row; <see cref="Hot"/> ones stand out (gold chip, red tag).</summary>
    public sealed class Chip
    {
        public string Text;
        public bool Hot;
        public Chip(string text, bool hot = false) { Text = text; Hot = hot; }
    }

    public sealed class AdviceSection
    {
        public string Title;
        /// <summary>Whose panel this is ("OPPONENT" / "YOU"): the overlay then draws the label, <see cref="HeaderName"/> and <see cref="HeaderRight"/> instead of the title.</summary>
        public string Owner;
        public string HeaderName;
        public string HeaderRight;
        /// <summary>Draw the title as a small capitals label (a sub-section of the panel above it).</summary>
        public bool Minor;
        /// <summary>Shown on the opponent's (left) side of the overlay.</summary>
        public bool Left;
        /// <summary>Draw the title as a header bar (deck-tracker style), with this text at its right end.</summary>
        public bool Bar;
        public string RightText;
        /// <summary>Goes in the deck panel at the bottom of its side; everything else goes in the info panel above it.</summary>
        public bool Lower;
        /// <summary>About the opponent (stays true when the sides are swapped).</summary>
        public bool Opponent;
        public List<AdviceLine> Lines = new List<AdviceLine>();
        public AdviceSection(string title) { Title = title; }
        public void Add(string text, Tone tone = Tone.Normal) => Lines.Add(new AdviceLine(text, tone));
    }

    public sealed class AdviceSnapshot
    {
        public List<AdviceSection> Sections = new List<AdviceSection>();
        public bool InGame;
    }

    public sealed class ArchetypeGuess
    {
        public MetaArchetype Archetype;
        public double Probability;
    }

    public sealed class LikelyCard
    {
        public string CardId;
        public double Probability;      // chance it's still in their hand or deck
    }

    /// <summary>
    /// Turns the tracked game state plus card knowledge and meta stats into advice. Everything is computed from what
    /// you can see: cards they've revealed, the public game state, and aggregate meta stats.
    /// </summary>
    public sealed partial class Advisor
    {
        private readonly GameTracker _game;
        private readonly CardDb _cards;
        private MetaStats _meta;
        private ArchetypeDetail _friendlyDetail;
        private MetaArchetype _friendlyArchetype;

        public Advisor(GameTracker game, CardDb cards, MetaStats meta, Settings settings = null)
        {
            _game = game;
            _cards = cards;
            _meta = meta;
            Settings = settings ?? new Settings();
        }

        public Settings Settings { get; }

        /// <summary>Overlay toggles (hotkeys flip these).</summary>
        public volatile bool HideDeck, HideOpponent, HideAdvice;

        /// <summary>Review of the last finished game, shown between games.</summary>
        public GameRecord LastGame { get; set; }

        public MetaStats Meta { get => _meta; set => _meta = value; }

        /// <summary>Your deck's archetype and its matchup data, looked up once per game (does a download).</summary>
        public void PrepareFriendlyDeck(Action<string> log)
        {
            _friendlyArchetype = null;
            _friendlyDetail = null;
            List<string> deck;
            string cls;
            lock (_game.Sync)
            {
                deck = _game.FriendlyDeckList?.ToList();
                cls = _game.HeroClass(_game.FriendlyPlayerId);
            }
            if (_meta == null || deck == null || deck.Count == 0) return;
            cls = cls ?? _cards.ById(deck.FirstOrDefault(id => _cards.ById(id)?.CardClass != "NEUTRAL"))?.CardClass;
            var arch = _meta.MatchDeck(cls, deck);
            if (arch == null) return;
            _friendlyArchetype = arch;
            _friendlyDetail = _meta.Detail(arch.Id, log);
        }

        public MetaArchetype FriendlyArchetype => _friendlyArchetype;

        // ================================================================== snapshot

        public AdviceSnapshot Compute()
        {
            lock (_game.Sync) return ComputeLocked();
        }

        private AdviceSnapshot ComputeLocked()
        {
            var snap = new AdviceSnapshot { InGame = _game.InGame };
            if (_game.InGame && !GameModes.IsCardGame(_game.GameType))
            {
                // Battlegrounds and Mercenaries: the card coaching doesn't apply.
                var other = new AdviceSection("HearthStoner");
                other.Add($"{GameModes.Name(_game.GameType, _game.FormatType)}: HearthStoner records this game's result. Its coaching is for the card modes.", Tone.Dim);
                snap.Sections.Add(other);
                return snap;
            }
            if (!_game.InGame || _game.FriendlyPlayerId == 0)
            {
                var idle = new AdviceSection("HearthStoner");
                idle.Add(_game.InGame ? "Game starting..." : "Waiting for a game.", Tone.Dim);
                if (_meta != null && _meta.Archetypes.Count > 0)
                    idle.Add($"Meta: {_meta.Archetypes.Count} archetypes, {_meta.TotalGames:N0} games ({_meta.Rank}, {_meta.Period})", Tone.Dim);
                snap.Sections.Add(idle);
                if (!_game.InGame && LastGame != null && Settings.ShowLastGameReview) snap.Sections.Add(ReviewSection(LastGame));
                return snap;
            }

            var me = _game.FriendlyPlayerId;
            var opp = _game.OpponentPlayerId;
            var oppClass = _game.HeroClass(opp);
            var guesses = PredictArchetypes(oppClass);
            var likely = LikelyRemaining(guesses);
            var st = Settings;

            void Add(AdviceSection sec, bool show, bool opponentSide)
            {
                if (sec == null || !show) return;
                if (opponentSide ? HideOpponent : false) return;
                sec.Left = opponentSide != st.SwapSides;
                sec.Opponent = opponentSide;
                snap.Sections.Add(sec);
            }

            var advice = !HideAdvice;
            if (!_game.InMulligan)
            {
                // Health & damage strips go first on each side of the overlay.
                Add(HealthStrip(opp, me, likely, left: true), st.ShowHealth, true);
                Add(HealthStrip(me, opp, likely, left: false), st.ShowHealth, false);
            }
            if (_game.InMulligan) Add(MulliganSection(oppClass), st.ShowMulligan && advice, false);
            if (_game.InMulligan || _game.Turn <= 4) Add(MatchupSection(oppClass, guesses), st.ShowMulligan && advice, false);
            if (!_game.InMulligan) Add(CountersSection(), st.ShowThisTurn && advice, false);
            Add(DiscoverSection(oppClass), st.ShowDiscover && advice, false);
            Add(OpponentSection(oppClass, guesses, likely), st.ShowOpponent, true);
            Add(SecretSection(), st.ShowSecrets, true);
            Add(OpponentPlayedSection(oppClass, guesses), st.ShowOpponentPlayed, true);
            if (!_game.InMulligan)
            {
                Add(ThisTurnSection(), st.ShowThisTurn && advice, false);
                Add(BoardSection(likely), st.ShowBoard && advice, false);
            }
            if (!HideDeck && st.ShowDeck)
            {
                var deck = DeckSection(out var nextDraw);
                Add(deck, true, false);
                // The next-draw odds sit with the rest of your info, so the deck panel is just the list.
                if (nextDraw != null) (snap.Sections.LastOrDefault(x => !x.Lower && !x.Opponent) ?? deck).Lines.Add(nextDraw);
            }
            return snap;
        }

        // ================================================================== last game review

        public static AdviceSection ReviewSection(GameRecord g)
        {
            var s = new AdviceSection($"Last game: {(g.Result == "WON" ? "Win" : g.Result == "LOST" ? "Loss" : "Ended")} vs {g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass)}");
            s.Add($"{g.DeckName ?? MetaStats.ClassName(g.MyClass)}, {(g.Turns + 1) / 2} turns, {(int)g.Duration.TotalMinutes} min, {(g.WentFirst ? "first" : "coin")}", Tone.Dim);
            if (g.Review.Count == 0) s.Add("No mistakes spotted.", Tone.Good);
            foreach (var n in g.Review.OrderByDescending(n => n.Severity).ThenBy(n => n.Turn).Take(8))
                s.Add($"{(n.Turn > 0 ? $"T{(n.Turn + 1) / 2}: " : "")}{n.Text}", n.Severity >= 3 ? Tone.Bad : n.Severity == 2 ? Tone.Warn : Tone.Normal);
            if (g.Review.Count > 8) s.Add($"... {g.Review.Count - 8} more (HearthStoner review)", Tone.Dim);
            return s;
        }
    }
}
