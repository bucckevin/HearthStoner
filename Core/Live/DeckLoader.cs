#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HackF5.UnitySpy.HearthstoneLib.Detail.Deck;

namespace HearthStoner.Live
{
    /// <summary>
    /// Finds your deck list for the current game and hands it to the tracker. Tries Decks.log first (the deck you
    /// queued with, logged by Hearthstone), then game memory. Keeps retrying every 2 seconds while a game is on and
    /// no list is known, up to <see cref="MaxAttempts"/> times per game.
    /// </summary>
    public sealed class DeckLoader
    {
        private const int MaxAttempts = 30;
        private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

        private readonly GameTracker _tracker;
        private readonly Advisor _advisor;
        private readonly CardDb _cards;
        private readonly ILiveMemory _live;
        private readonly Func<string?> _powerLog;
        private readonly Action<string> _say;
        private readonly Action<string> _log;
        private volatile bool _reset = true;
        private int _attempts;

        /// <summary>Creates the loader; call <see cref="Loop"/> on a background thread to run it.</summary>
        /// <param name="tracker">The live game; the list is written to it.</param>
        /// <param name="advisor">Told to prepare its deck advice once the list is known.</param>
        /// <param name="cards">Card database for decoding deck codes.</param>
        /// <param name="live">Memory access, for the active deck when Decks.log has nothing.</param>
        /// <param name="powerLog">The Power.log being followed (Decks.log sits next to it); may return null.</param>
        /// <param name="say">Messages for you ("Your deck: ..."); must not be null.</param>
        /// <param name="log">Diagnostics; must not be null.</param>
        public DeckLoader(GameTracker tracker, Advisor advisor, CardDb cards, ILiveMemory live, Func<string?> powerLog,
            Action<string> say, Action<string> log)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _advisor = advisor ?? throw new ArgumentNullException(nameof(advisor));
            _cards = cards ?? throw new ArgumentNullException(nameof(cards));
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _powerLog = powerLog ?? throw new ArgumentNullException(nameof(powerLog));
            _say = say ?? throw new ArgumentNullException(nameof(say));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>A new game started: start counting attempts again.</summary>
        public void Reset() => _reset = true;

        /// <summary>Runs forever; meant for a background thread.</summary>
        public void Loop()
        {
            while (true)
            {
                try { Step(); }
                catch (Exception ex) { _log("[deck] " + ex.Message); }
                Thread.Sleep(Pause);
            }
        }

        private void Step()
        {
            if (!NeedsDeck()) return;
            _attempts++;

            var (list, name, source) = FromDecksLog();
            if (list == null) (list, name, source) = FromMemory();
            if (list == null)
            {
                if (_attempts == 1 || _attempts == MaxAttempts) _log($"[deck] Couldn't find your deck list yet (attempt {_attempts}).");
                return;
            }
            if (!Hand(list, name)) return;

            _say($"{DateTime.Now:HH:mm:ss}  Your deck: {(string.IsNullOrEmpty(name) ? "(unnamed)" : name)} ({list.Count} cards, from {source})");
            _advisor.PrepareFriendlyDeck(_log);
            if (_advisor.FriendlyArchetype != null)
                _say($"{DateTime.Now:HH:mm:ss}  Your deck plays as: {_advisor.FriendlyArchetype.Name} ({_advisor.FriendlyArchetype.WinRate:P1} WR in the meta)");
        }

        /// <summary>True when a game is on, no list is known yet and attempts are left.</summary>
        private bool NeedsDeck()
        {
            bool inGame, haveDeck;
            lock (_tracker.Sync)
            {
                // Battlegrounds and Mercenaries have no deck to load.
                inGame = _tracker.InGame && _tracker.FriendlyPlayerId != 0 && GameModes.IsCardGame(_tracker.GameType);
                haveDeck = _tracker.FriendlyDeckList != null;
            }
            if (_reset) { _reset = false; _attempts = 0; }
            return inGame && !haveDeck && _attempts < MaxAttempts;
        }

        private (List<string>? List, string? Name, string? Source) FromDecksLog()
        {
            var path = _powerLog();
            // Only a deck queued since the previous game started is this game's (practice and solo games log none).
            TimeSpan? previous;
            lock (_tracker.Sync) previous = _tracker.PreviousGameStartedAt;
            var after = previous is TimeSpan p && !string.IsNullOrEmpty(path) ? DecksLog.At(path, p) : (DateTime?)null;
            if (after > DateTime.Now) after = null;   // a game from an earlier Hearthstone session, not this log's
            var queued = DecksLog.LastQueuedDeck(path, null, after);
            if (queued == null) return (null, null, null);
            if (!MatchesHero(queued.DeckCode))
            {
                // A deck from an earlier game (modes that don't log theirs, e.g. solo adventures): memory knows better.
                if (_attempts == 1) _log($"[deck] Decks.log's last deck ({queued.Name ?? "unnamed"}) isn't your hero's class; trying memory.");
                return (null, null, null);
            }
            var list = DeckCode.Decode(queued.DeckCode, _cards);
            return list != null && list.Count > 0 ? (list, queued.Name, "Decks.log") : (null, null, null);
        }

        /// <summary>False when the deck's class and your hero's class are both known and differ.</summary>
        private bool MatchesHero(string? code)
        {
            string? hero;
            lock (_tracker.Sync) hero = _tracker.HeroClass(_tracker.FriendlyPlayerId);
            var deck = code == null ? null : DeckCode.HeroClass(code, _cards);
            return HeroClasses.Same(deck, hero) != false;
        }

        private (List<string>? List, string? Name, string? Source) FromMemory()
        {
            var deck = _live.Use(x => x.GetActiveDeck());
            if (deck?.DeckList == null || deck.DeckList.Count == 0) return (null, null, null);
            var list = deck.DeckList.ToList();
            // Sideboard picks that join the deck (Commander Beatrix: ten copies of her pick).
            if (deck is Deck full && full.Sideboards != null)
                list.AddRange(DeckCode.JoiningCopies(full.Sideboards.SelectMany(sb => (sb.Cards ?? new List<string>())
                    .Select(c => new DeckCode.Sideboard { CardId = c, OwnerId = sb.KeyCardId, Count = 1 })), _cards));
            return (list, deck.Name, "memory");
        }

        /// <summary>Gives the list to the tracker unless the game ended or a list arrived meanwhile.</summary>
        private bool Hand(List<string> list, string? name)
        {
            lock (_tracker.Sync)
            {
                if (!_tracker.InGame || _tracker.FriendlyDeckList != null) return false;
                _tracker.FriendlyDeckList = list;
                _tracker.FriendlyDeckName = name;
                return true;
            }
        }
    }
}
