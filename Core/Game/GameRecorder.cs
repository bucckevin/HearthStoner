using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    /// <summary>
    /// Watches a game as it's tracked and turns it into a <see cref="GameRecord"/>: a snapshot per turn, your mulligan
    /// and Discover picks, and a review of likely mistakes. All methods are called with the tracker lock held.
    /// </summary>
    public sealed class GameRecorder
    {
        private readonly GameTracker _game;
        private readonly Advisor _advisor;
        private readonly CardDb _cards;

        private GameRecord _rec;
        private TurnSnapshot _turn;               // turn in progress
        private int _lastOptionsSeen = -1;
        private List<string> _lastPlayable = new List<string>();
        private bool _mulliganDone;
        private Dictionary<string, double> _aoeOddsAtMyTurnEnd = new Dictionary<string, double>();
        private readonly HashSet<int> _discoversRecorded = new HashSet<int>();

        public GameRecorder(GameTracker game, Advisor advisor, CardDb cards)
        {
            _game = game;
            _advisor = advisor;
            _cards = cards;
        }

        public GameRecord Current => _rec;
        /// <summary>The time to stamp on games: now, or (when importing old logs) the time in the log.</summary>
        public Func<DateTime> Clock = () => DateTime.Now;

        /// <summary>Extra details from memory, set when they arrive (match info is read a moment after the game starts).</summary>
        public void SetMatchInfo(string gameType, string format, string rank, int starLevel, int legendRank, string opponentName,
            int stars = 0, int starBonus = 0, int seasonId = 0, int opponentStarLevel = 0, int opponentLegendRank = 0)
        {
            if (_rec == null) return;
            _rec.GameType = gameType;
            _rec.Format = format;
            _rec.Rank = rank;
            _rec.StarLevel = starLevel;
            _rec.LegendRank = legendRank;
            _rec.Stars = Math.Max(0, stars);
            _rec.StarBonus = Math.Max(0, starBonus);
            _rec.SeasonId = Math.Max(0, seasonId);
            _rec.OpponentName = opponentName;
            _rec.OpponentStarLevel = Math.Max(0, opponentStarLevel);
            _rec.OpponentLegendRank = Math.Max(0, opponentLegendRank);
        }

        public void OnEvent(GameEvent ev)
        {
            switch (ev.Kind)
            {
                case GameEventKind.GameStart:
                    _rec = new GameRecord { Start = Clock() };
                    _turn = null;
                    _mulliganDone = false;
                    _lastOptionsSeen = -1;
                    _lastPlayable = new List<string>();
                    _aoeOddsAtMyTurnEnd = new Dictionary<string, double>();
                    _discoversRecorded.Clear();
                    break;

                case GameEventKind.Turn:
                    if (_rec == null) return;
                    CloseTurn();
                    _turn = new TurnSnapshot { Turn = _game.Turn, Yours = IsMyTurnNumber(_game.Turn) };
                    try
                    {
                        var guess = _advisor.TopOpponentArchetype();
                        _turn.OpponentGuess = guess.Name;
                        _turn.OpponentGuessProbability = guess.Probability;
                    }
                    catch (Exception swallowed) { Diag.Ignored(swallowed, "GameRecorder"); }
                    break;

                case GameEventKind.Play:
                    if (_turn != null && !string.IsNullOrEmpty(ev.CardId) && ev.Friendly) _turn.Played.Add(ev.CardId);
                    if (_turn != null && !ev.Friendly && !_turn.Yours && ev.CardId != null && _cards.ById(ev.CardId)?.Profile?.IsAoe == true)
                    {
                        _turn.TheirAoe = ev.CardId;
                        _turn.TheirAoeProbability = _aoeOddsAtMyTurnEnd.TryGetValue(ev.CardId, out var p) ? p : 0;
                    }
                    break;

                case GameEventKind.Died:
                    if (_turn == null) break;
                    if (ev.Friendly) _turn.YourMinionsDied++; else _turn.TheirMinionsDied++;
                    break;

                case GameEventKind.Secret:
                    if (_rec != null && ev.CardId != null && _turn != null && _turn.Yours)
                    {
                        var rule = Secrets.ById(ev.CardId);
                        if (rule != null)
                            Note(_turn.Turn, "Secret", $"You set off {rule.Name}. {rule.Advice}", 1);
                    }
                    break;

                case GameEventKind.GameEnd:
                    if (_rec == null) return;
                    CloseTurn();
                    Finish();
                    break;
            }
        }

        /// <summary>Called whenever the tracker state changes.</summary>
        public void OnChanged()
        {
            if (_rec == null || !_game.InGame || _game.FriendlyPlayerId == 0) return;

            if (!_mulliganDone && !_game.InSetup) RecordMulligan();
            RecordDiscovers();

            // New options = the game is waiting for your action: check lethal and remember what was playable.
            if (_game.IsFriendlyTurn && _turn != null && _turn.Yours && _game.Playable.Count + _game.UsableNow.Count > 0)
            {
                var sig = _game.UsableNow.Count * 1000 + _game.Playable.Sum() % 997;
                if (sig != _lastOptionsSeen)
                {
                    _lastOptionsSeen = sig;
                    _lastPlayable = _game.Hand(_game.FriendlyPlayerId).Where(e => _game.Playable.Contains(e.Id)).Select(e => e.KnownCardId).ToList();
                    if (!_turn.HadLethal)
                    {
                        var l = _advisor.LethalNow();
                        if (l.Lethal)
                        {
                            _turn.HadLethal = true;
                            _turn.Puzzle = CapturePuzzle(l);
                            _turn.LethalNote = $"{l.Damage} damage available vs {l.TheirHealth} health" +
                                               (l.Burn.Count > 0 ? $" (board + {string.Join(" + ", l.Burn.Select(_cards.NameOf))})" : " (on board)");
                        }
                    }
                }
            }
        }

        private LethalPuzzle CapturePuzzle(Advisor.LethalInfo l)
        {
            var me = _game.FriendlyPlayerId;
            var opp = _game.OpponentPlayerId;
            var hero = _game.Hero(opp);
            string Minion(Entity m)
            {
                var ready = !m.Flag("EXHAUSTED") || m.Flag("CHARGE");
                var tags = new List<string>();
                if (m.Flag("TAUNT")) tags.Add("Taunt");
                if (m.Flag("DIVINE_SHIELD")) tags.Add("Shield");
                if (m.Flag("WINDFURY")) tags.Add("Windfury");
                if (m.Flag("FROZEN")) tags.Add("Frozen");
                if (m.Controller == me) tags.Add(ready && !m.Flag("FROZEN") ? "ready" : "can't attack");
                return $"{_game.CardName(m)} {m.Attack}/{m.Health}{(tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "")}";
            }
            var weapon = _game.Weapon(me);
            return new LethalPuzzle
            {
                Mana = _game.AvailableMana(me),
                TheirHealth = hero?.Health ?? 0,
                TheirArmor = hero?.Armor ?? 0,
                YourMinions = _game.Minions(me).Select(Minion).ToList(),
                TheirMinions = _game.Minions(opp).Select(Minion).ToList(),
                Hand = _game.Hand(me).Select(e => $"{_game.CardName(e)} ({e.Int("COST")})").ToList(),
                Weapon = weapon != null ? $"{_game.CardName(weapon)} {weapon.Attack}/{weapon.Int("DURABILITY") - weapon.Int("DAMAGE")}" : null,
                Solution = l.Burn.Count > 0 ? $"Attack face with everything, then {string.Join(" + ", l.Burn.Select(_cards.NameOf))}: {l.Damage} damage." : $"Attack face with everything: {l.Damage} damage.",
            };
        }

        private bool IsMyTurnNumber(int turn)
        {
            var first = _game.PlayerEntity(_game.FriendlyPlayerId)?.Flag("FIRST_PLAYER") ?? false;
            return (turn % 2 == 1) == first;
        }

        private void CloseTurn()
        {
            if (_turn == null) return;
            var me = _game.FriendlyPlayerId;
            var opp = _game.OpponentPlayerId;
            var myHero = _game.Hero(me);
            var theirHero = _game.Hero(opp);
            var mine = _game.Minions(me);
            var theirs = _game.Minions(opp);
            _turn.MyHp = myHero?.Health ?? 0; _turn.MyArmor = myHero?.Armor ?? 0;
            _turn.TheirHp = theirHero?.Health ?? 0; _turn.TheirArmor = theirHero?.Armor ?? 0;
            _turn.MyMinions = mine.Count; _turn.MyBoardAttack = mine.Sum(m => m.Attack); _turn.MyBoardHealth = mine.Sum(m => Math.Max(0, m.Health));
            _turn.TheirMinions = theirs.Count; _turn.TheirBoardAttack = theirs.Sum(m => m.Attack); _turn.TheirBoardHealth = theirs.Sum(m => Math.Max(0, m.Health));
            _turn.MyHand = _game.Hand(me).Count; _turn.TheirHand = _game.Hand(opp).Count;
            _turn.MyDeck = _game.DeckCount(me); _turn.TheirDeck = _game.DeckCount(opp);

            if (_turn.Yours)
            {
                var p = _game.PlayerEntity(me);
                _turn.ManaAvailable = (p?.Int("RESOURCES") ?? 0) + (p?.Int("TEMP_RESOURCES") ?? 0) - (p?.Int("OVERLOAD_LOCKED") ?? 0);
                _turn.ManaUnspent = _game.AvailableMana(me);
                _turn.UnplayedPlayable = _lastPlayable.Where(id => !string.IsNullOrEmpty(id) && !id.Contains("COIN")).ToList();
                // Board clears they could have next turn, and how likely, for judging overextension afterwards.
                try { _aoeOddsAtMyTurnEnd = _advisor.LikelyAoe(); } catch { _aoeOddsAtMyTurnEnd = new Dictionary<string, double>(); }
            }
            _rec.Snapshots.Add(_turn);
            _lastPlayable = new List<string>();
            _lastOptionsSeen = -1;
            _turn = null;
        }

        private void RecordMulligan()
        {
            _mulliganDone = true;
            var me = _game.FriendlyPlayerId;
            var choice = _game.Choices.FirstOrDefault(c => c.Type == "MULLIGAN" && c.PlayerId == me);
            if (choice == null) return;
            var onCoin = _game.PlayerEntity(me)?.Flag("FIRST_PLAYER") == false;
            var oppClass = _game.HeroClass(_game.OpponentPlayerId);
            foreach (var id in choice.Entities)
            {
                if (!_game.Entities.TryGetValue(id, out var e)) continue;
                var cardId = e.KnownCardId;
                if (string.IsNullOrEmpty(cardId) || _cards.IsCoin(cardId)) continue;
                var v = _advisor.Verdict(cardId, oppClass, onCoin);
                var kept = e.Zone == "HAND";
                _rec.Mulligan.Add(new MulliganPick { CardId = cardId, Kept = kept, Advice = v.FromData ? v.Verdict : null, Delta = v.Delta });
                if (v.FromData && Math.Abs(v.Delta) >= 0.02 && ((v.Verdict == "KEEP" && !kept) || (v.Verdict == "TOSS" && kept)))
                    Note(0, "Mulligan", $"You {(kept ? "kept" : "threw back")} {_cards.NameOf(cardId)}; the data says {v.Verdict.ToLower()} ({(v.Delta >= 0 ? "+" : "")}{v.Delta * 100:F1}% win rate when kept).", 1);
            }
        }

        private void RecordDiscovers()
        {
            var oppClass = _game.HeroClass(_game.OpponentPlayerId);
            foreach (var c in _game.Choices.Where(c => c.PlayerId == _game.FriendlyPlayerId && c.Type != "MULLIGAN" && c.Chosen.Count > 0 && !_discoversRecorded.Contains(c.Id)))
            {
                _discoversRecorded.Add(c.Id);
                var pick = new DiscoverPick { Turn = c.Turn, Source = c.SourceCardId };
                for (var i = 0; i < c.Entities.Count; i++)
                {
                    var id = _game.EntityCardId(c.Entities[i]) ?? c.CardIds.ElementAtOrDefault(i);
                    pick.Options.Add(id);
                    pick.WinRates.Add(_advisor.DiscoverStatFor(id, oppClass)?.WinRate);
                }
                pick.Picked = _game.EntityCardId(c.Chosen[0]);
                _rec.Discovers.Add(pick);

                var pickedIdx = pick.Options.IndexOf(pick.Picked);
                var best = pick.WinRates.Select((w, i) => new { w, i }).Where(x => x.w.HasValue).OrderByDescending(x => x.w.Value).FirstOrDefault();
                if (best != null && pickedIdx >= 0 && pick.WinRates[pickedIdx].HasValue && best.i != pickedIdx && best.w.Value - pick.WinRates[pickedIdx].Value >= 0.03)
                    Note(c.Turn, "Discover", $"From {_cards.NameOf(c.SourceCardId)} you picked {_cards.NameOf(pick.Picked)} ({pick.WinRates[pickedIdx]:P0} WR); {_cards.NameOf(pick.Options[best.i])} wins more ({best.w:P0}).", 1);
            }
        }

        private void Finish()
        {
            var me = _game.FriendlyPlayerId;
            var opp = _game.OpponentPlayerId;
            _rec.End = Clock();
            _rec.Result = _game.Result;
            _rec.Turns = _game.Turn;
            // The mode from the log (memory may not have had it, e.g. for imported games).
            if (!GameModes.Known(_rec.GameType)) _rec.GameType = _game.GameType;
            if (string.IsNullOrEmpty(_rec.Format) || _rec.Format == "FT_UNKNOWN") _rec.Format = _game.FormatType;
            if (!GameModes.IsCardGame(_rec.GameType))
            {
                // Battlegrounds and Mercenaries: just the mode, times and result (the card tracking doesn't apply).
                _rec.Snapshots.Clear(); _rec.Mulligan.Clear(); _rec.Discovers.Clear();
                return;
            }
            _rec.DeckName = _game.FriendlyDeckName;
            _rec.DeckCards = _game.FriendlyDeckList?.ToList() ?? new List<string>();
            _rec.DeckCode = DeckCode.Encode(new HackF5.UnitySpy.HearthstoneLib.Detail.Deck.Deck
            {
                DeckList = _rec.DeckCards,
                HeroCardId = _game.Hero(me)?.CardId,
                FormatType = 2,
            }, _cards);
            _rec.MyClass = _game.HeroClass(me);
            _rec.MyArchetype = _advisor.FriendlyArchetype?.Name;
            _rec.WentFirst = _game.PlayerEntity(me)?.Flag("FIRST_PLAYER") ?? false;
            _rec.OpponentClass = _game.HeroClass(opp);
            var top = _advisor.TopOpponentArchetype();
            _rec.OpponentArchetype = top.Name;
            _rec.OpponentArchetypeProbability = top.Probability;
            _rec.OpponentCardsSeen = _game.OpponentOriginalCardsSeen();
            if (string.IsNullOrEmpty(_rec.OpponentName) && _game.PlayerNames.TryGetValue(opp, out var on)) _rec.OpponentName = on;

            Review();
        }

        private void Review()
        {
            var snaps = _rec.Snapshots;
            for (var i = 0; i < snaps.Count; i++)
            {
                var t = snaps[i];
                var next = i + 1 < snaps.Count ? snaps[i + 1] : null;
                if (!t.Yours)
                {
                    // Overextended into a board clear the prediction expected.
                    if (t.TheirAoe != null && t.YourMinionsDied >= 3)
                        Note(t.Turn, "Overextended", $"Their {_cards.NameOf(t.TheirAoe)} killed {t.YourMinionsDied} of your minions" +
                                                    (t.TheirAoeProbability >= 0.25 ? $" - HearthStoner gave {t.TheirAoeProbability:P0} that they had it. Hold back a minion or two." : "."),
                             t.TheirAoeProbability >= 0.25 ? 2 : 1);
                    continue;
                }

                // Lethal was available and the game went on.
                if (t.HadLethal && (i + 1 < snaps.Count))
                    Note(t.Turn, "MissedLethal", $"Possible missed lethal: {t.LethalNote}. Check your attacks and burn before ending the turn.", 3);

                // Unspent mana with something playable, in the early and mid game where tempo matters most.
                if (t.ManaUnspent >= 2 && t.UnplayedPlayable.Count > 0 && t.Turn <= 16)
                {
                    var cheapest = t.UnplayedPlayable.Select(id => _cards.ById(id)).Where(c => c?.Cost != null).OrderBy(c => c.Cost).FirstOrDefault();
                    if (cheapest != null && cheapest.Cost <= t.ManaUnspent)
                        Note(t.Turn, "UnspentMana", $"Ended the turn with {t.ManaUnspent} of {t.ManaAvailable} mana unspent while {cheapest.Name} ({cheapest.Cost}) was playable.", 2);
                }
            }
        }

        private void Note(int turn, string kind, string text, int severity) =>
            _rec?.Review.Add(new ReviewNote { Turn = turn, Kind = kind, Text = text, Severity = severity });
    }
}
