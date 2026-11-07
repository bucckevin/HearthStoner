using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
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

        public AdviceLine(string text, Tone tone = Tone.Normal) { Text = text; Tone = tone; }

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

    public sealed class AdviceSection
    {
        public string Title;
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

        // ================================================================== opponent deck prediction

        /// <summary>
        /// Bayesian guess at the opponent's archetype: prior = how often each archetype of their class is played,
        /// likelihood = how often each archetype runs the cards they've shown (only cards that started in their deck).
        /// </summary>
        public List<ArchetypeGuess> PredictArchetypes(string oppClass)
        {
            var result = new List<ArchetypeGuess>();
            if (_meta == null || string.IsNullOrEmpty(oppClass)) return result;
            var archetypes = _meta.ForClass(oppClass).ToList();
            if (archetypes.Count == 0) return result;

            var seen = _game.OpponentOriginalCardsSeen().Distinct().ToList();
            var logs = new List<double>();
            foreach (var a in archetypes)
            {
                double lp = Math.Log(Math.Max(1e-6, a.ClassShare));
                foreach (var c in seen)
                {
                    var f = Math.Min(1.0, a.Copies(c));
                    // A card the archetype (almost) never runs is strong evidence against it, but not proof: tech choices.
                    lp += Math.Log(Math.Max(0.01, Math.Min(0.98, f)));
                }
                logs.Add(lp);
            }
            var max = logs.Max();
            var weights = logs.Select(l => Math.Exp(l - max)).ToList();
            var sum = weights.Sum();
            for (var i = 0; i < archetypes.Count; i++)
                result.Add(new ArchetypeGuess { Archetype = archetypes[i], Probability = weights[i] / sum });
            return result.OrderByDescending(g => g.Probability).ToList();
        }

        /// <summary>Cards the opponent probably still has in hand or deck, weighted over the archetype guesses.</summary>
        public List<LikelyCard> LikelyRemaining(List<ArchetypeGuess> guesses)
        {
            var seenCounts = new Dictionary<string, int>();
            foreach (var c in _game.OpponentOriginalCardsSeen()) seenCounts[c] = (seenCounts.TryGetValue(c, out var n) ? n : 0) + 1;

            var expected = new Dictionary<string, double>();
            foreach (var g in guesses.Where(g => g.Probability >= 0.02))
                foreach (var kv in g.Archetype.Cards)
                {
                    var copies = g.Archetype.Copies(kv.Key);
                    if (copies < 0.05) continue;
                    expected[kv.Key] = (expected.TryGetValue(kv.Key, out var e) ? e : 0) + g.Probability * copies;
                }

            var result = new List<LikelyCard>();
            foreach (var kv in expected)
            {
                var seen = seenCounts.TryGetValue(kv.Key, out var s) ? s : 0;
                var remaining = kv.Value - seen;
                if (remaining <= 0.1) continue;
                result.Add(new LikelyCard { CardId = kv.Key, Probability = Math.Min(0.99, remaining) });
            }
            return result.OrderByDescending(c => c.Probability).ToList();
        }

        private AdviceSection OpponentSection(string oppClass, List<ArchetypeGuess> guesses, List<LikelyCard> likely)
        {
            var s = new AdviceSection($"Opponent - {MetaStats.ClassName(oppClass)}") { Left = true };
            var opp = _game.OpponentPlayerId;

            if (guesses.Count == 0) s.Add(_meta == null ? "No meta data loaded." : "No meta data for this class.", Tone.Dim);
            foreach (var g in guesses.Take(3).Where(g => g.Probability >= 0.05))
                s.Add($"{g.Probability:P0}  {g.Archetype.Name}  ({g.Archetype.WinRate:P0} WR)", g == guesses[0] ? Tone.Heading : Tone.Normal);

            var bigThreats = likely.Where(c => c.Probability >= 0.3).Take(8).ToList();
            if (bigThreats.Count > 0)
            {
                s.Add("Likely still has:", Tone.Dim);
                foreach (var c in bigThreats)
                {
                    var info = _cards.ById(c.CardId);
                    var prof = info?.Profile;
                    var tag = prof != null && prof.IsAoe ? $"  [{prof.Summary()}]" : "";
                    s.Lines.Add(AdviceLine.Card(info, c.CardId, $"  {c.Probability:P0}  ({info?.Cost}) {info?.Name ?? c.CardId}{tag}",
                        prof != null && prof.IsAoe ? Tone.Warn : Tone.Normal, $"{c.Probability:P0}", prof != null && prof.IsAoe ? "AoE" : null));
                }
            }

            // Their hand: where each card came from and how long they've held it.
            var hand = _game.Hand(opp);
            if (hand.Count > 0)
            {
                s.Add($"Hand ({hand.Count}):", Tone.Dim);
                var slot = 1;
                foreach (var e in hand)
                {
                    var age = Math.Max(0, (_game.Turn - e.ZoneTurn + 1) / 2);
                    string what;
                    if (!string.IsNullOrEmpty(e.KnownCardId) && (e.ReturnedToHand || !string.IsNullOrEmpty(e.CardId)))
                        what = $"{_cards.NameOf(e.KnownCardId)}{(e.ReturnedToHand ? " (returned)" : "")}";
                    else if (e.Int("CREATOR") != 0)
                        what = $"created by {_cards.NameOf(_game.EntityCardId(e.Int("CREATOR")))}";
                    else if (e.KeptInMulligan)
                        what = "kept in mulligan";
                    else if (e.StartedInDeck)
                        what = e.StartController == _game.FriendlyPlayerId ? "stolen from your deck" : "from deck";
                    else what = "?";
                    var held = age > 0 ? $", held {age} turn{(age == 1 ? "" : "s")}" : "";
                    s.Add($"  #{slot++}: {what}{held}", e.ReturnedToHand ? Tone.Warn : Tone.Normal);
                }
            }
            s.Add($"Mana next turn: {_game.NextTurnMana(opp)}", Tone.Dim);
            foreach (var read in Reads(likely)) s.Add(read, Tone.Warn);
            return s;
        }

        // ================================================================== secrets

        private AdviceSection SecretSection()
        {
            if (_game.OpponentSecrets.Count == 0) return null;
            var s = new AdviceSection($"Secrets ({_game.OpponentSecrets.Count})") { Left = true };
            var i = 1;
            foreach (var st in _game.OpponentSecrets.Values.OrderBy(x => x.EntityId))
            {
                var label = _game.OpponentSecrets.Count > 1 ? $"Secret {i++}: " : "";
                if (st.CreatedBy != null) s.Add($"{label}created by {_cards.NameOf(st.CreatedBy)} - may be outside the usual pool", Tone.Dim);
                if (st.Candidates.Count == 0) { s.Add($"{label}not a Standard secret we know", Tone.Dim); continue; }
                s.Add($"{label}{st.Candidates.Count} possible:", Tone.Heading);
                foreach (var id in st.Candidates.OrderBy(_cards.NameOf))
                {
                    var rule = Secrets.ById(id);
                    s.Add($"  {_cards.NameOf(id)}", Tone.Warn);
                    if (rule != null) s.Add($"    {rule.Advice}", Tone.Dim);
                }
            }
            return s;
        }

        // ================================================================== health & damage counters

        /// <summary>Damage a side's board can deal to the enemy hero: minions able to attack (x2 Windfury) + weapon.</summary>
        private int BoardDamage(int playerId, bool nextTurn)
        {
            var dmg = 0;
            foreach (var m in _game.Minions(playerId))
            {
                if (m.Attack <= 0 || m.Flag("CANT_ATTACK") || m.Flag("DORMANT")) continue;
                if (m.Flag("FROZEN")) continue;   // frozen now = misses its next attack
                // On their next turn every minion is ready; this turn only those not exhausted (Charge ignores it,
                // Rush can't hit heroes the turn it's played).
                if (!nextTurn && !CanAttackFace(m)) continue;
                dmg += m.Attack * (m.Flag("WINDFURY") ? 2 : 1);
            }
            var hero = _game.Hero(playerId);
            var weapon = _game.Weapon(playerId);
            if (weapon != null && hero != null && !hero.Flag("FROZEN") && (nextTurn || !hero.Flag("EXHAUSTED")))
                dmg += weapon.Attack * (weapon.Flag("WINDFURY") ? 2 : 1);
            else if (hero != null && !nextTurn && hero.Attack > 0 && !hero.Flag("EXHAUSTED") && !hero.Flag("FROZEN"))
                dmg += hero.Attack;   // hero attack from a buff this turn
            return dmg;
        }

        /// <summary>
        /// The counter strip for one side: effective health, what the other side's board threatens against it,
        /// reach from hand (yours) or likely reach (theirs), board totals and fatigue.
        /// </summary>
        private AdviceSection HealthStrip(int side, int other, List<LikelyCard> likely, bool left)
        {
            var friendly = side == _game.FriendlyPlayerId;
            var hero = _game.Hero(side);
            var hp = hero?.Health ?? 30;
            var armor = hero?.Armor ?? 0;
            var effective = hp + armor;
            // Player names, falling back to You / Opponent until the game reveals the opponent's name.
            var who = _game.DisplayName(side) ?? (friendly ? "You" : "Opponent");
            var s = new AdviceSection($"{who}: {hp} HP{(armor > 0 ? $" + {armor} armor = {effective}" : "")}") { Left = left };

            var myTurn = _game.IsFriendlyTurn;
            // The attacking side is "other". If it's the attacker's turn now, count only what can attack now.
            var attackerActsNow = (other == _game.FriendlyPlayerId) == myTurn;
            var incoming = BoardDamage(other, nextTurn: !attackerActsNow);
            var taunts = _game.Minions(side).Where(m => m.Flag("TAUNT") && !m.Flag("STEALTH")).ToList();
            var tauntHp = taunts.Sum(m => Math.Max(0, m.Health));

            if (friendly)
            {
                // Their board against you, on their next turn (or now, if it's their turn).
                var when = attackerActsNow ? "now" : "next turn";
                var tone = incoming >= effective ? Tone.Bad : incoming + 5 >= effective ? Tone.Warn : Tone.Normal;
                s.Add($"Their board hits for {incoming} {when}{(tauntHp > 0 ? $" (your Taunts: {tauntHp} HP in the way)" : "")}", tone);
                if (incoming >= effective)
                    s.Lines.Add(new AdviceLine(tauntHp > 0 && incoming - tauntHp < effective ? "Lethal on board unless your Taunts hold" : "LETHAL ON BOARD - clear, heal or Taunt up", Tone.Bad) { Pulse = true });

                // Burn/charge they probably hold that they can afford next turn.
                var mana = _game.NextTurnMana(other);
                var reach = likely.Where(c => c.Probability >= 0.3)
                    .Select(c => new { Card = _cards.ById(c.CardId), c.Probability })
                    .Where(x => x.Card != null && (x.Card.Cost ?? 99) <= mana)
                    .Select(x => new { x.Card, x.Probability, Dmg = FaceDamage(x.Card) })
                    .Where(x => x.Dmg > 0)
                    .OrderByDescending(x => x.Dmg).Take(2).ToList();
                var theirHp = HeroPowerDamage(other, attackerActsNow);
                if (theirHp.Damage > 0 && theirHp.Cost <= mana)
                {
                    incoming += theirHp.Damage;
                    s.Add($"  + their hero power: {theirHp.Damage} = {incoming}", incoming >= effective ? Tone.Bad : Tone.Dim);
                }
                foreach (var r in reach)
                    s.Add($"  + {r.Card.Name} ({r.Probability:P0}): {r.Dmg} more{(incoming + r.Dmg >= effective ? " = lethal" : "")}", incoming + r.Dmg >= effective ? Tone.Bad : Tone.Warn);

                // Facing lethal: which cards in your deck would save you, and the odds of drawing one.
                if (incoming >= effective && !attackerActsNow)
                {
                    var outs = YourOuts(other, incoming - effective + 1);
                    var deckCount = Math.Max(1, _game.DeckCount(side));
                    if (outs.Count > 0)
                    {
                        var n = outs.Sum(o => o.Value);
                        s.Add($"Outs in your deck: {n} card{(n == 1 ? "" : "s")}, {(double)n / deckCount:P0} next draw", Tone.Warn);
                        s.Add("  " + string.Join(", ", outs.Keys.Select(_cards.NameOf).Take(6)), Tone.Dim);
                    }
                    else if (_game.FriendlyDeckList != null) s.Add("No outs left in your deck: play to your hand.", Tone.Bad);
                }
            }
            else
            {
                // Your board (+ burn in hand) against them.
                var mana = myTurn ? _game.AvailableMana(_game.FriendlyPlayerId) : _game.NextTurnMana(_game.FriendlyPlayerId);
                var burn = BestBurn(_game.Hand(_game.FriendlyPlayerId), mana);
                var total = incoming + burn.Damage;
                var when = attackerActsNow ? "now" : "next turn";
                var iceBarrier = _game.OpponentSecrets.Values.Any(x => x.Candidates.Contains("CORE_EX1_289"));
                var need = effective + (iceBarrier ? 8 : 0);
                var tone = total >= need && tauntHp == 0 ? Tone.Good : Tone.Normal;
                s.Add($"Your board hits for {incoming} {when}{(burn.Damage > 0 ? $", +{burn.Damage} burn in hand = {total}" : "")}", tone);
                if (burn.Damage > 0) s.Add($"  burn: {string.Join(" + ", burn.Cards.Select(_cards.NameOf))} ({mana} mana)", Tone.Dim);
                var myPower = HeroPowerDamage(_game.FriendlyPlayerId, attackerActsNow);
                if (myPower.Damage > 0 && myPower.Cost <= mana - burn.Cards.Sum(id => _cards.ById(id)?.Cost ?? 0))
                {
                    total += myPower.Damage;
                    s.Add($"  + hero power {myPower.Damage} = {total}", Tone.Dim);
                }
                if (total >= effective)
                {
                    if (tauntHp > 0 && attackerActsNow)
                    {
                        // Work out whether the attacks can clear the Taunts and still reach face.
                        var plan = SolveLethal(mana, effective);
                        if (plan.Lethal)
                        {
                            s.Lines.Add(new AdviceLine($"LETHAL through Taunt ({plan.FaceDamage} to face){(iceBarrier ? " - Ice Barrier may add 8" : "")}", Tone.Good) { Pulse = true });
                            foreach (var step in plan.Steps) s.Add("  " + step, Tone.Good);
                        }
                        else s.Add($"Not lethal: the Taunt{(taunts.Count > 1 ? "s" : "")} ({tauntHp} HP) can't be cleared with enough left for face (best {plan.FaceDamage})", Tone.Warn);
                    }
                    else if (tauntHp > 0) s.Add($"Lethal next turn if you get through {taunts.Count} Taunt ({tauntHp} HP)", Tone.Warn);
                    else s.Lines.Add(new AdviceLine($"LETHAL{(iceBarrier ? " - but Ice Barrier may add 8 armor" : "")}", iceBarrier && total < need ? Tone.Warn : Tone.Good) { Pulse = true });
                }
                else if (total > 0) s.Add($"  {effective - total} short of lethal", Tone.Dim);

                // You threaten lethal next turn: what they'd need to survive, from their likely cards.
                if (!attackerActsNow && incoming >= effective)
                {
                    var theirOuts = likely.Where(c => c.Probability >= 0.2)
                        .Select(c => new { c.CardId, c.Probability, P = _cards.ById(c.CardId)?.Profile })
                        .Where(x => x.P != null && (x.P.IsAoe || x.P.Heal >= incoming - effective + 1 || x.P.Armor >= incoming - effective + 1 || x.P.Taunt))
                        .Take(4).ToList();
                    if (theirOuts.Count > 0)
                        s.Add("Their likely outs: " + string.Join(", ", theirOuts.Select(o => $"{_cards.NameOf(o.CardId)} {o.Probability:P0}")), Tone.Warn);
                }
            }

            // Board totals: useful for judging trades and whether a board clear is worth it.
            var minions = _game.Minions(side);
            if (minions.Count > 0)
                s.Add($"Board: {minions.Count} minion{(minions.Count == 1 ? "" : "s")}, {minions.Sum(m => m.Attack)} attack / {minions.Sum(m => Math.Max(0, m.Health))} health", Tone.Dim);

            // Fatigue: player entity FATIGUE = fatigue damage already taken.
            var deck = _game.DeckCount(side);
            var fatigue = _game.PlayerEntity(side)?.Int("FATIGUE") ?? 0;
            if (deck == 0) s.Add($"Deck empty: next draw deals {fatigue + 1} fatigue", Tone.Bad);
            else if (deck <= 3) s.Add($"{deck} card{(deck == 1 ? "" : "s")} left before fatigue", Tone.Warn);
            return s;
        }

        /// <summary>Damage a card in hand could deal to a hero this turn: burn spells, or a Charge minion's attack.</summary>
        private static int FaceDamage(CardInfo c)
        {
            var p = c.Profile;
            if (p.DamageHitsFace) return p.Damage;
            if (p.AoeHitsFace) return p.AoeDamage;
            if (p.Charge && c.Type == "MINION") return (c.Attack ?? 0) * (p.Windfury ? 2 : 1);
            return 0;
        }

        // ================================================================== board: threats, answers, play-arounds

        private AdviceSection BoardSection(List<LikelyCard> likely)
        {
            var s = new AdviceSection("Board");
            var me = _game.FriendlyPlayerId;
            var opp = _game.OpponentPlayerId;
            var myMinions = _game.Minions(me);
            var theirMinions = _game.Minions(opp);
            var myMana = _game.IsFriendlyTurn ? _game.AvailableMana(me) : _game.NextTurnMana(me);
            var myHand = _game.Hand(me);

            // ---- their threats, most dangerous first, with your answers
            foreach (var m in theirMinions.OrderByDescending(Threat).Take(4))
            {
                var info = _cards.ById(m.KnownCardId);
                var notes = ThreatNotes(m, info);
                s.Add($"{info?.Name ?? "?"} {m.Attack}/{m.Health}{(notes.Length > 0 ? " - " + notes : "")}", Threat(m) >= 8 ? Tone.Bad : Tone.Normal);

                var answers = new List<string>();
                foreach (var h in myHand)
                {
                    var hi = _cards.ById(h.KnownCardId);
                    if (hi == null || (hi.Cost ?? 99) > myMana) continue;
                    if (Kills(hi.Profile, m)) answers.Add($"{hi.Name} ({hi.Cost})");
                }
                var traders = myMinions.Where(x => CanAttackMinion(x) && x.Attack >= m.Health && !m.Flag("DIVINE_SHIELD") && !m.Flag("STEALTH") && !m.Flag("IMMUNE"))
                    .OrderBy(x => x.Attack + x.Health).ToList();
                var cleanTrade = traders.FirstOrDefault(x => m.Attack < x.Health && !m.Flag("POISONOUS"));
                if (cleanTrade != null) answers.Insert(0, $"trade {_game.CardName(cleanTrade)} (survives)");
                else if (traders.Count > 0) answers.Add($"trade {_game.CardName(traders[0])}");
                if (answers.Count > 0) s.Add($"  answer: {string.Join(", ", answers.Take(3))}", Tone.Good);
            }

            // ---- board clears they may have
            var nextMana = _game.NextTurnMana(opp);
            foreach (var c in likely.Where(c => c.Probability >= 0.25))
            {
                var info = _cards.ById(c.CardId);
                var p = info?.Profile;
                if (p == null || !p.IsAoe || (info.Cost ?? 99) > nextMana) continue;
                var exposed = p.BoardClear ? myMinions.Count : myMinions.Count(x => x.Health <= p.AoeDamage);
                if (exposed < 2) continue;
                s.Add($"Play around {info.Name} ({c.Probability:P0}, {p.Summary()}): {exposed} of your minions would die", Tone.Warn);
            }

            if (s.Lines.Count == 0) s.Add("Nothing urgent.", Tone.Dim);
            return s;
        }

        private static bool CanAttackFace(Entity m) =>
            m.Attack > 0 && !m.Flag("FROZEN") && !m.Flag("CANT_ATTACK") &&
            (!m.Flag("EXHAUSTED") || m.Flag("CHARGE")) && !(m.Flag("JUST_PLAYED") && m.Flag("RUSH") && !m.Flag("CHARGE"));

        private static bool CanAttackMinion(Entity m) =>
            m.Attack > 0 && !m.Flag("FROZEN") && !m.Flag("CANT_ATTACK") && (!m.Flag("EXHAUSTED") || m.Flag("CHARGE") || m.Flag("RUSH"));

        /// <summary>Rough danger score of a minion on the board.</summary>
        private double Threat(Entity m)
        {
            var info = _cards.ById(m.KnownCardId);
            var p = info?.Profile;
            double t = m.Attack * 1.5 + Math.Max(0, m.Health) * 0.5;
            if (m.Flag("WINDFURY")) t += m.Attack;
            if (m.Flag("POISONOUS")) t += 3;
            if (m.Flag("LIFESTEAL")) t += m.Attack * 0.5;
            if (m.Flag("DIVINE_SHIELD")) t += 2;
            if (m.Int("SPELLPOWER") > 0) t += 2 * m.Int("SPELLPOWER");
            if (p != null && !m.Flag("SILENCED"))
            {
                if (p.Aura) t += 3;
                if (p.Trigger) t += 3;
                if (p.Deathrattle) t += 1;
            }
            return t;
        }

        private string ThreatNotes(Entity m, CardInfo info)
        {
            var notes = new List<string>();
            var p = info?.Profile;
            if (m.Flag("TAUNT")) notes.Add("Taunt");
            if (m.Flag("DIVINE_SHIELD")) notes.Add("Divine Shield: ping it first");
            if (m.Flag("POISONOUS")) notes.Add("Poisonous: trade a token into it");
            if (m.Flag("STEALTH")) notes.Add("Stealth: needs AoE");
            if (m.Flag("WINDFURY")) notes.Add("Windfury");
            if (m.Flag("LIFESTEAL")) notes.Add("Lifesteal");
            if (m.Flag("REBORN")) notes.Add("Reborn: kill twice");
            if (p != null && !m.Flag("SILENCED"))
            {
                if (p.Deathrattle) notes.Add("Deathrattle: silence/transform beats killing");
                if (p.Aura) notes.Add("aura: kill first");
                if (p.Trigger) notes.Add("triggers each turn: kill first");
            }
            return string.Join("; ", notes);
        }

        /// <summary>Can this card in your hand deal with that minion by itself?</summary>
        private static bool Kills(CardProfile p, Entity target)
        {
            if (target.Flag("IMMUNE")) return false;
            if (target.Flag("STEALTH")) return p.AoeDamage >= target.Health && !target.Flag("DIVINE_SHIELD") || p.BoardClear;
            if (p.Destroy || p.Transform || p.Bounce || p.BoardClear) return !target.Flag("ELUSIVE");
            var dmg = target.Flag("ELUSIVE") ? p.AoeDamage : Math.Max(p.Damage, p.AoeDamage);
            return dmg >= target.Health && !target.Flag("DIVINE_SHIELD");
        }

        private sealed class Burn { public int Damage; public List<string> Cards = new List<string>(); }

        /// <summary>Most face damage you can deal with direct-damage cards in hand within your mana (small knapsack).</summary>
        private Burn BestBurn(List<Entity> hand, int mana)
        {
            var items = hand.Select(h => _cards.ById(h.KnownCardId)).Where(ci => ci != null && ci.Cost.HasValue)
                .Select(ci => new { ci.Id, Cost = ci.Cost.Value, Dmg = FaceDamage(ci) })
                .Where(x => x.Dmg > 0 && x.Cost <= mana).ToList();
            var best = new Burn();
            var n = items.Count;
            if (n == 0 || n > 12) return best;
            for (var mask = 1; mask < (1 << n); mask++)
            {
                int cost = 0, dmg = 0;
                for (var i = 0; i < n; i++) if ((mask & (1 << i)) != 0) { cost += items[i].Cost; dmg += items[i].Dmg; }
                if (cost <= mana && dmg > best.Damage)
                {
                    best = new Burn { Damage = dmg };
                    for (var i = 0; i < n; i++) if ((mask & (1 << i)) != 0) best.Cards.Add(items[i].Id);
                }
            }
            return best;
        }

        // ================================================================== your deck: draw odds

        private AdviceSection DeckSection(out AdviceLine nextDraw)
        {
            nextDraw = null;
            var me = _game.FriendlyPlayerId;
            var deckCount = _game.DeckCount(me);
            var title = _game.FriendlyDeckName ?? _friendlyArchetype?.Name ?? "Your deck";
            var s = new AdviceSection(title) { Bar = true, Lower = true, RightText = $"{deckCount} left" };
            var remaining = _game.FriendlyRemainingDeck();
            if (_game.FriendlyDeckList == null) { s.Add("Looking for your deck list (Decks.log, then memory)...", Tone.Dim); return s; }
            if (deckCount == 0) { s.Add("Empty - fatigue next draw!", Tone.Bad); return s; }
            var showOdds = Settings?.DeckShowOdds ?? true;

            var known = remaining.Values.Sum();
            // Role odds for the next draw (shown under the list).
            double removal = 0, aoe = 0, heal = 0, draw = 0;
            foreach (var kv in remaining)
            {
                var p = _cards.ById(kv.Key)?.Profile;
                if (p == null) continue;
                var prob = (double)kv.Value / deckCount;
                if (p.IsSingleRemoval) removal += prob;
                if (p.IsAoe) aoe += prob;
                if (p.Heal > 0 || p.Armor > 0) heal += prob;
                if (p.Draws) draw += prob;
            }
            var roles = new List<string>();
            if (removal > 0) roles.Add($"removal {removal:P0}");
            if (aoe > 0) roles.Add($"AoE {aoe:P0}");
            if (heal > 0) roles.Add($"heal/armor {heal:P0}");
            if (draw > 0) roles.Add($"draw {draw:P0}");

            // Full deck list, by cost: copies left / copies in the list. Drawn-out cards are struck through.
            var total = new Dictionary<string, int>();
            foreach (var id in _game.FriendlyDeckList) total[id] = (total.TryGetValue(id, out var n) ? n : 0) + 1;
            var ordered = total.Keys
                .Select(id => new { Id = id, Info = _cards.ById(id) })
                .OrderBy(x => x.Info?.Cost ?? 99).ThenBy(x => x.Info?.Name ?? x.Id)
                .ToList();
            foreach (var x in ordered)
            {
                var left = remaining.TryGetValue(x.Id, out var r) ? r : 0;
                var name = x.Info?.Name ?? x.Id;
                var cost = x.Info?.Cost?.ToString() ?? "-";
                if (left == 0)
                {
                    var gone = AdviceLine.Card(x.Info, x.Id, $"0/{total[x.Id]}  ({cost}) {name}", Tone.Dim, "0", null, struck: true);
                    gone.Flash = JustDrawn(x.Id);
                    s.Lines.Add(gone);
                    continue;
                }
                var next = (double)left / deckCount;
                var twoDraws = 1 - Choose(deckCount - left, 2) / Math.Max(1, Choose(deckCount, 2));
                var drawnSome = left < total[x.Id];
                var badge = x.Info?.Rarity == "LEGENDARY" && total[x.Id] == 1 ? "\u2605" : left.ToString();
                var line = AdviceLine.Card(x.Info, x.Id, $"{left}/{total[x.Id]}  ({cost}) {name}   {next:P0} next, {twoDraws:P0} in 2",
                    drawnSome ? Tone.Normal : Tone.Heading, badge, showOdds ? $"{next:P0}" : null);
                line.Flash = JustDrawn(x.Id);
                s.Lines.Add(line);
            }

            // Cards shuffled into your deck by effects that you know about.
            foreach (var kv in remaining.Where(kv => !total.ContainsKey(kv.Key)))
            {
                var info = _cards.ById(kv.Key);
                s.Lines.Add(AdviceLine.Card(info, kv.Key, $"+{kv.Value}  ({info?.Cost}) {info?.Name ?? kv.Key} (added)   {(double)kv.Value / deckCount:P0} next",
                    Tone.Warn, kv.Value.ToString(), showOdds ? $"{(double)kv.Value / deckCount:P0}" : null));
            }
            if (known < deckCount) s.Add($"+ {deckCount - known} unknown card(s) added by effects", Tone.Dim);
            if (roles.Count > 0) nextDraw = new AdviceLine("Next draw: " + string.Join(", ", roles), Tone.Dim);
            return s;
        }

        /// <summary>Did a copy of this card leave your deck in the last 3 seconds?</summary>
        private bool JustDrawn(string cardId) =>
            (_game.LastFriendlyDraw.CardId == cardId || GameTracker.BaseCardId(_game.LastFriendlyDraw.CardId) == cardId)
            && DateTime.UtcNow - _game.LastFriendlyDraw.At < TimeSpan.FromSeconds(3);

        private static double Choose(int n, int k)
        {
            if (k < 0 || n < k) return 0;
            double r = 1;
            for (var i = 1; i <= k; i++) r = r * (n - k + i) / i;
            return r;
        }

        // ================================================================== mulligan

        public sealed class MulliganVerdict
        {
            public string Verdict;      // KEEP / TOSS / either
            public double Delta;        // win-rate change when kept, vs. the baseline
            public double KeepRate;
            public bool FromData;
        }

        /// <summary>Your deck's stats in this matchup (or all matchups when the sample is small).</summary>
        private (Dictionary<string, CardStat> Cards, double Baseline, Matchup Matchup) MatchupData(string oppClass)
        {
            Matchup mu = null;
            if (_friendlyDetail != null && _friendlyDetail.Matchups.TryGetValue(MetaStats.Key(oppClass), out var m) && m.Games >= 50) mu = m;
            var cards = mu?.Cards ?? _friendlyDetail?.Archetype?.Cards ?? _friendlyArchetype?.Cards;
            var baseline = mu?.WinRate ?? _friendlyDetail?.Archetype?.WinRate ?? _friendlyArchetype?.WinRate ?? 0.5;
            return (cards, baseline, mu);
        }

        /// <summary>Keep or toss one opening-hand card, from win-rate-when-kept data, or simple cost rules without data.</summary>
        public MulliganVerdict Verdict(string cardId, string oppClass, bool onCoin)
        {
            var (cards, baseline, _) = MatchupData(oppClass);
            CardStat st = null;
            if (cards != null && cardId != null) cards.TryGetValue(cardId, out st);
            if (st != null && st.InHandAfterMulligan >= 20)
            {
                var delta = st.MulliganWinRate - baseline;
                return new MulliganVerdict { Verdict = delta >= 0.01 ? "KEEP" : delta <= -0.01 ? "TOSS" : "either", Delta = delta, KeepRate = st.KeepRate, FromData = true };
            }
            var cost = _cards.ById(cardId)?.Cost ?? 99;
            var keep = cost <= 2 || (cost == 3 && onCoin);
            return new MulliganVerdict { Verdict = keep ? "KEEP" : "TOSS" };
        }

        private AdviceSection MulliganSection(string oppClass)
        {
            var s = new AdviceSection("Mulligan");
            var me = _game.FriendlyPlayerId;
            var onCoin = _game.PlayerEntity(me)?.Flag("FIRST_PLAYER") == false;
            var offered = _game.Hand(me).Where(e => !_cards.IsCoin(e.KnownCardId)).ToList();
            var (_, baseline, mu) = MatchupData(oppClass);

            if (_friendlyArchetype != null)
                s.Add($"{_friendlyArchetype.Name} vs {MetaStats.ClassName(oppClass)}: {baseline:P1} WR{(mu != null ? $" over {mu.Games:N0} games" : " (all matchups)")}", Tone.Heading);
            else
                s.Add("Deck not matched to a meta archetype - using simple rules.", Tone.Dim);

            foreach (var e in offered)
            {
                var info = _cards.ById(e.KnownCardId);
                var v = Verdict(e.KnownCardId, oppClass, onCoin);
                var tone = v.Verdict == "KEEP" ? Tone.Good : v.Verdict == "TOSS" ? Tone.Bad : Tone.Normal;
                var text = v.FromData
                    ? $"{v.Verdict,-6} {info?.Name ?? e.KnownCardId}  {(v.Delta >= 0 ? "+" : "")}{v.Delta * 100:F1}% WR when kept, kept {v.KeepRate:P0}"
                    : $"{v.Verdict,-6} {info?.Name ?? e.KnownCardId}  (no data; {(v.Verdict == "KEEP" ? "cheap" : "expensive")})";
                s.Lines.Add(AdviceLine.Card(info, e.KnownCardId, text, tone, v.Verdict == "KEEP" ? "\u2714" : v.Verdict == "TOSS" ? "\u2716" : "~",
                    v.FromData ? $"{(v.Delta >= 0 ? "+" : "")}{v.Delta * 100:F1}%" : "no data"));
            }
            s.Add(onCoin ? "You're on the coin: one extra card, keep a 3-drop more often." : "You're going first.", Tone.Dim);
            return s;
        }

        // ================================================================== Discover helper

        /// <summary>Win rate when this card was Discovered in your deck's archetype (matchup first, then overall).</summary>
        public DiscoverStat DiscoverStatFor(string cardId, string oppClass)
        {
            if (_friendlyDetail == null || cardId == null) return null;
            if (_friendlyDetail.Matchups.TryGetValue(MetaStats.Key(oppClass), out var mu) && mu.Discover.TryGetValue(cardId, out var ms) && ms.Discovered >= 30) return ms;
            return _friendlyDetail.Discover.TryGetValue(cardId, out var st) && st.Discovered >= 30 ? st : null;
        }

        private AdviceSection DiscoverSection(string oppClass)
        {
            var choice = _game.OpenFriendlyChoice;
            if (choice == null || choice.Entities.Count < 2) return null;
            var source = _cards.NameOf(choice.SourceCardId);
            var s = new AdviceSection($"Choose ({source})");
            var options = choice.Entities.Select((id, i) => new { Id = _game.EntityCardId(id) ?? choice.CardIds.ElementAtOrDefault(i) }).ToList();
            var stats = options.Select(o => DiscoverStatFor(o.Id, oppClass)).ToList();
            var best = stats.Where(x => x != null).OrderByDescending(x => x.WinRate).FirstOrDefault();
            for (var i = 0; i < options.Count; i++)
            {
                var info = _cards.ById(options[i].Id);
                var st = stats[i];
                var isBest = st != null && best != null && st.CardId == best.CardId && stats.Count(x => x != null) > 1;
                var detail = st != null ? $"{st.WinRate:P0} ({st.Discovered:N0})" : (info?.Profile?.Summary() ?? "");
                s.Lines.Add(AdviceLine.Card(info, options[i].Id, $"{info?.Name ?? options[i].Id}: {(st != null ? $"{st.WinRate:P1} WR when picked over {st.Discovered:N0} games" : "no data")}",
                    isBest ? Tone.Good : Tone.Normal, isBest ? "\u2714" : "", detail));
            }
            if (best == null) s.Add("No pick data for these options in your archetype.", Tone.Dim);
            else s.Add("Win rate in games where that card was picked, for your archetype.", Tone.Dim);
            return s;
        }

        // ================================================================== this turn: mana helper

        private AdviceSection ThisTurnSection()
        {
            if (!_game.IsFriendlyTurn || _game.Playable.Count == 0) return null;
            var me = _game.FriendlyPlayerId;
            var mana = _game.AvailableMana(me);
            var cards = _game.Hand(me).Where(e => _game.Playable.Contains(e.Id))
                .Select(e => new { e.Id, CardId = e.KnownCardId, Cost = e.Int("COST") })
                .Where(x => x.Cost <= mana).ToList();
            var heroPower = _game.Zone(me, "PLAY").Concat(Entities(me)).FirstOrDefault(e => e.CardType == "HERO_POWER" && _game.UsableNow.Contains(e.Id));
            var s = new AdviceSection($"This turn - {mana} mana");
            if (cards.Count == 0 && heroPower == null) return null;

            // Best ways to spend the mana: every subset of up to 12 playable cards.
            var n = Math.Min(cards.Count, 12);
            var combos = new List<(int Used, List<int> Idx)>();
            for (var mask = 1; mask < (1 << n); mask++)
            {
                int used = 0; var idx = new List<int>();
                for (var i = 0; i < n; i++) if ((mask & (1 << i)) != 0) { used += cards[i].Cost; idx.Add(i); }
                if (used <= mana) combos.Add((used, idx));
            }
            var hpCost = heroPower?.Int("COST") ?? 0;
            var top = combos.OrderByDescending(c => c.Used).ThenBy(c => c.Idx.Count).Take(3).ToList();
            if (top.Count == 0 && heroPower != null) s.Add($"Hero power ({hpCost}) is your only play.", Tone.Normal);
            foreach (var c in top)
            {
                var names = string.Join(" + ", c.Idx.Select(i => $"{_cards.NameOf(cards[i].CardId)} ({cards[i].Cost})"));
                var left = mana - c.Used;
                var withHp = heroPower != null && left >= hpCost ? $" + hero power ({hpCost})" : "";
                var used = c.Used + (withHp.Length > 0 ? hpCost : 0);
                s.Add($"{used}/{mana}: {names}{withHp}", used == mana ? Tone.Good : Tone.Normal);
            }
            s.Add("Plays the game says are legal now, ranked by mana used (not by strength).", Tone.Dim);
            return s;
        }

        private IEnumerable<Entity> Entities(int playerId) => _game.Entities.Values.Where(e => e.Controller == playerId);

        // ================================================================== opponent's played cards

        /// <summary>Their deck panel: the deck we think they're on, cards left, and what they've played so far.</summary>
        private AdviceSection OpponentPlayedSection(string oppClass, List<ArchetypeGuess> guesses)
        {
            var top = guesses.FirstOrDefault();
            var name = top != null && top.Probability >= 0.5 ? top.Archetype.Name : MetaStats.ClassName(oppClass);
            var s = new AdviceSection(string.IsNullOrEmpty(name) ? "Opponent" : name)
                { Bar = true, Lower = true, RightText = $"{_game.DeckCount(_game.OpponentPlayerId)} left" };
            foreach (var g in _game.OpponentPlayLog.Where(p => !string.IsNullOrEmpty(p.CardId))
                         .GroupBy(p => p.CardId)
                         .Select(g => new { Id = g.Key, Count = g.Count(), Created = g.All(x => x.Created), Last = g.Max(x => x.Turn) })
                         .OrderBy(g => _cards.ById(g.Id)?.Cost ?? 99).ThenBy(g => _cards.NameOf(g.Id)))
            {
                var info = _cards.ById(g.Id);
                s.Lines.Add(AdviceLine.Card(info, g.Id, $"{g.Count}x {info?.Name ?? g.Id}{(g.Created ? " (created)" : "")}",
                    g.Created ? Tone.Dim : Tone.Normal, g.Count > 1 ? g.Count.ToString() : null, g.Created ? "created" : $"T{(g.Last + 1) / 2}"));
            }
            return s;
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

        // ================================================================== reading the opponent

        /// <summary>
        /// Inferences from what the opponent did with their mana: floating mana with a full hand, skipping a turn,
        /// and cheap cards their archetype plays that they could have played but didn't. All from public information.
        /// </summary>
        public List<string> Reads(List<LikelyCard> likely)
        {
            var reads = new List<string>();
            var turns = _game.OpponentTurns;
            if (turns.Count == 0) return reads;

            var last = turns[turns.Count - 1];
            if (last.Unspent >= 2 && last.HandAtEnd >= 2 && last.Mana >= 3)
                reads.Add($"Floated {last.Unspent} of {last.Mana} mana with {last.HandAtEnd} cards: likely holding reactive or expensive cards.");
            var skipped = turns.Where(t => t.CardsPlayed == 0 && t.Mana >= 1 && t.Mana <= 4 && t.HandAtEnd >= 3).Select(t => t.Mana).ToList();
            if (skipped.Count > 0)
                reads.Add($"Played nothing with {string.Join(" and ", skipped)} mana: probably no cheap plays early, so a slower hand.");

            // Cheap likely cards they had the mana to play but didn't (only when their hand wasn't empty).
            var spare = turns.Where(t => t.HandAtEnd > 0 && t.Unspent > 0).Select(t => t.Unspent).DefaultIfEmpty(0).Max();
            var notHeld = likely.Where(c => c.Probability >= 0.4)
                .Select(c => _cards.ById(c.CardId))
                .Where(c => c != null && c.Cost.HasValue && c.Cost.Value <= spare && c.Cost.Value <= 3 && c.Type == "MINION")
                .Take(3).Select(c => c.Name).ToList();
            if (notHeld.Count > 0)
                reads.Add($"Probably not in hand (they had the mana to play them): {string.Join(", ", notHeld)}.");

            // Cards kept since the mulligan and never played.
            var oldKeeps = _game.Hand(_game.OpponentPlayerId).Count(e => e.KeptInMulligan && (_game.Turn - e.ZoneTurn) / 2 >= 4);
            if (oldKeeps > 0)
                reads.Add($"{oldKeeps} card{(oldKeeps == 1 ? "" : "s")} kept since the mulligan and still unplayed: often a combo piece or a situational answer.");
            return reads;
        }

        // ================================================================== outs

        /// <summary>Cards left in your deck that stop their board from killing you next turn (by count).</summary>
        private Dictionary<string, int> YourOuts(int attacker, int excess)
        {
            var result = new Dictionary<string, int>();
            var theirMinions = _game.Minions(attacker);
            foreach (var kv in _game.FriendlyRemainingDeck())
            {
                var info = _cards.ById(kv.Key);
                var p = info?.Profile;
                if (p == null) continue;
                var saves = false;
                if (p.BoardClear) saves = true;
                else if (p.AoeDamage > 0)
                    saves = theirMinions.Where(m => m.Health <= p.AoeDamage && !m.Flag("DIVINE_SHIELD")).Sum(m => m.Attack * (m.Flag("WINDFURY") ? 2 : 1)) >= excess;
                else if (p.Heal >= excess || p.Armor >= excess) saves = true;
                else if (p.Taunt && info.Type == "MINION" && (info.Health ?? 0) >= 4) saves = true;
                else if ((p.Destroy || p.Transform || p.Damage > 0) && theirMinions.Any(m => (p.Destroy || p.Transform || p.Damage >= m.Health) && m.Attack * (m.Flag("WINDFURY") ? 2 : 1) >= excess)) saves = true;
                if (saves) result[kv.Key] = kv.Value;
            }
            return result;
        }

        // ================================================================== matchup cheat sheet

        private AdviceSection MatchupSection(string oppClass, List<ArchetypeGuess> guesses)
        {
            if (_friendlyArchetype == null) return null;
            var (cards, baseline, mu) = MatchupData(oppClass);
            var s = new AdviceSection($"Matchup: {_friendlyArchetype.Name} vs {MetaStats.ClassName(oppClass)}");
            s.Add($"{baseline:P1} win rate{(mu != null ? $" over {mu.Games:N0} games" : " (all matchups)")}", baseline >= 0.53 ? Tone.Good : baseline < 0.47 ? Tone.Bad : Tone.Normal);

            if (cards != null && _game.FriendlyDeckList != null)
            {
                var mine = _game.FriendlyDeckList.Distinct()
                    .Select(id => cards.TryGetValue(id, out var st) && st.Drawn >= 50 ? new { Id = id, St = st } : null)
                    .Where(x => x != null).ToList();
                var best = mine.OrderByDescending(x => x.St.DrawnWinRate).Take(3).Where(x => x.St.DrawnWinRate > baseline + 0.02).ToList();
                var worst = mine.OrderBy(x => x.St.DrawnWinRate).Take(2).Where(x => x.St.DrawnWinRate < baseline - 0.02).ToList();
                if (best.Count > 0) s.Add("Your best cards here: " + string.Join(", ", best.Select(x => $"{_cards.NameOf(x.Id)} ({x.St.DrawnWinRate:P0})")), Tone.Good);
                if (worst.Count > 0) s.Add("Weak here: " + string.Join(", ", worst.Select(x => $"{_cards.NameOf(x.Id)} ({x.St.DrawnWinRate:P0})")), Tone.Bad);
            }

            var top = guesses.FirstOrDefault();
            if (top != null && top.Probability >= 0.3)
            {
                s.Add($"They're most likely {top.Archetype.Name} ({top.Probability:P0}). Key cards:", Tone.Heading);
                var keys = top.Archetype.CoreCards.Distinct().Select(id => _cards.ById(id)).Where(c => c != null)
                    .OrderByDescending(c => c.Profile.IsAoe ? 1 : 0).ThenBy(c => c.Cost ?? 99).Take(6).ToList();
                foreach (var c in keys)
                    s.Add($"  ({c.Cost}) {c.Name}{(c.Profile.IsAoe ? $" - {c.Profile.Summary()}" : "")}", c.Profile.IsAoe ? Tone.Warn : Tone.Normal);
            }
            return s;
        }

        // ================================================================== counters for scaling cards

        private AdviceSection CountersSection()
        {
            if (_game.FriendlyDeckList == null) return null;
            var text = string.Join(" | ", _game.FriendlyDeckList.Distinct().Select(id => _cards.ById(id)?.Profile?.CleanText?.ToLowerInvariant() ?? ""));
            var s = new AdviceSection("Counters");
            if (text.Contains("spell") && (text.Contains("cast this game") || text.Contains("you've cast") || text.Contains("spells you")))
                s.Add($"Spells you've cast: {_game.FriendlySpellsCast}");
            if (text.Contains("died this game") || text.Contains("friendly minions that died") || text.Contains("minions that died"))
                s.Add($"Your minions died: {_game.FriendlyMinionsDied}");
            if (text.Contains("died this turn"))
                s.Add($"Your minions died this turn: {_game.FriendlyMinionsDiedThisTurn}");
            if (text.Contains("corpse"))
                s.Add($"Corpses: {_game.PlayerEntity(_game.FriendlyPlayerId)?.Int("CORPSES") ?? 0}");
            if (text.Contains("hero power") && (text.Contains("used") || text.Contains("times")))
                s.Add($"Hero power uses: {_game.FriendlyHeroPowerUses}");
            if (text.Contains("cards played this turn") || text.Contains("played this turn") || text.Contains("combo"))
                s.Add($"Cards played this turn: {_game.FriendlyCardsPlayedThisTurn}");
            if (text.Contains("drawn") || text.Contains("you've drawn"))
                s.Add($"Cards drawn: {_game.FriendlyCardsDrawn}");
            return s.Lines.Count > 0 ? s : null;
        }

        // ================================================================== helpers for the recorder

        public sealed class LethalInfo
        {
            public bool Lethal;
            public int Damage;
            public int TheirHealth;
            public int TauntHealth;
            public List<string> Burn = new List<string>();
            public List<string> Plan = new List<string>();
        }

        /// <summary>Damage the hero power deals to the enemy hero if usable (e.g. Hunter 2, Mage 1), and its cost.</summary>
        private (int Damage, int Cost) HeroPowerDamage(int playerId, bool now)
        {
            var hp = _game.Entities.Values.FirstOrDefault(e => e.Controller == playerId && e.CardType == "HERO_POWER" && e.Zone == "PLAY");
            if (hp == null) return (0, 0);
            if (now && (hp.Flag("EXHAUSTED") || (playerId == _game.FriendlyPlayerId && _game.UsableNow.Count > 0 && !_game.UsableNow.Contains(hp.Id)))) return (0, 0);
            var p = _cards.ById(hp.KnownCardId)?.Profile;
            if (p == null) return (0, 0);
            return (p.DamageHitsFace ? p.Damage : 0, hp.Int("COST"));
        }

        /// <summary>Your attackers, damage spells you can afford, and their Taunts, for the lethal solver.</summary>
        private LethalSolver.Plan SolveLethal(int mana, int needed)
        {
            var me = _game.FriendlyPlayerId;
            var opp = _game.OpponentPlayerId;
            var attackers = new List<LethalSolver.Attacker>();
            foreach (var m in _game.Minions(me))
            {
                if (!CanAttackMinion(m)) continue;
                attackers.Add(new LethalSolver.Attacker { Name = _game.CardName(m), Attack = m.Attack, CanHitFace = CanAttackFace(m), Windfury = m.Flag("WINDFURY") });
            }
            var hero = _game.Hero(me);
            var weapon = _game.Weapon(me);
            var heroAtk = hero == null ? 0 : (weapon != null ? weapon.Attack : hero.Attack);
            if (hero != null && heroAtk > 0 && !hero.Flag("EXHAUSTED") && !hero.Flag("FROZEN"))
                attackers.Add(new LethalSolver.Attacker { Name = weapon != null ? _game.CardName(weapon) : "your hero", Attack = heroAtk, Windfury = weapon?.Flag("WINDFURY") ?? false });

            var taunts = _game.Minions(opp).Where(m => m.Flag("TAUNT") && !m.Flag("STEALTH"))
                .Select(m => new LethalSolver.Taunt { Name = _game.CardName(m), Health = Math.Max(1, m.Health), DivineShield = m.Flag("DIVINE_SHIELD") }).ToList();

            var spells = new List<LethalSolver.Spell>();
            foreach (var e in _game.Hand(me))
            {
                var info = _cards.ById(e.KnownCardId);
                var p = info?.Profile;
                if (p == null) continue;
                var cost = e.Int("COST");
                if (p.Damage > 0) spells.Add(new LethalSolver.Spell { Name = info.Name, Cost = cost, Damage = p.Damage, CanHitFace = p.DamageHitsFace });
                else if (p.AoeHitsFace && p.AoeDamage > 0) spells.Add(new LethalSolver.Spell { Name = info.Name, Cost = cost, Damage = p.AoeDamage, CanHitFace = true });
                else if (p.Charge && info.Type == "MINION" && (info.Attack ?? 0) > 0)
                    spells.Add(new LethalSolver.Spell { Name = info.Name + " (Charge)", Cost = cost, Damage = info.Attack.Value, CanHitFace = true });
            }
            var hp = HeroPowerDamage(me, true);
            if (hp.Damage > 0) spells.Add(new LethalSolver.Spell { Name = "hero power", Cost = hp.Cost, Damage = hp.Damage, CanHitFace = true });
            return LethalSolver.Solve(attackers, taunts, spells, mana, needed);
        }

        /// <summary>Your damage available right now (board + affordable burn) against their health. Lock must be held.</summary>
        public LethalInfo LethalNow()
        {
            var me = _game.FriendlyPlayerId;
            var opp = _game.OpponentPlayerId;
            var hero = _game.Hero(opp);
            var hp = (hero?.Health ?? 30) + (hero?.Armor ?? 0);
            var board = BoardDamage(me, nextTurn: false);
            var burn = BestBurn(_game.Hand(me), _game.AvailableMana(me));
            var taunt = _game.Minions(opp).Where(m => m.Flag("TAUNT") && !m.Flag("STEALTH")).Sum(m => Math.Max(0, m.Health));
            var heroPower = HeroPowerDamage(me, true);
            var extra = heroPower.Damage > 0 && heroPower.Cost <= _game.AvailableMana(me) - burn.Cards.Sum(id => _cards.ById(id)?.Cost ?? 0) ? heroPower.Damage : 0;
            var info = new LethalInfo
            {
                Damage = board + burn.Damage + extra,
                TheirHealth = hp,
                TauntHealth = taunt,
                Burn = burn.Cards,
                Lethal = hp > 0 && taunt == 0 && board + burn.Damage + extra >= hp,
            };
            if (taunt > 0 && hp > 0 && board + burn.Damage + extra >= hp)
            {
                var plan = SolveLethal(_game.AvailableMana(me), hp);
                info.Lethal = plan.Lethal;
                info.Damage = plan.FaceDamage;
                info.Plan = plan.Steps;
            }
            return info;
        }

        /// <summary>Chance (by deck prediction) that the opponent holds each board clear they could cast next turn.</summary>
        public Dictionary<string, double> LikelyAoe()
        {
            var opp = _game.OpponentPlayerId;
            var likely = LikelyRemaining(PredictArchetypes(_game.HeroClass(opp)));
            return likely.Where(c => _cards.ById(c.CardId)?.Profile?.IsAoe == true).ToDictionary(c => c.CardId, c => c.Probability);
        }

        public (string Name, double Probability) TopOpponentArchetype()
        {
            var g = PredictArchetypes(_game.HeroClass(_game.OpponentPlayerId)).FirstOrDefault();
            return g == null ? ((string)null, 0.0) : (g.Archetype.Name, g.Probability);
        }
    }
}
