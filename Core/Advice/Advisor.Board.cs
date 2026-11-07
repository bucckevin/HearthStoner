using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Advice
{
    public sealed partial class Advisor
    {

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
            var s = new AdviceSection($"{who}: {hp} HP{(armor > 0 ? $" + {armor} armor = {effective}" : "")}")
            {
                Left = left, Owner = friendly ? "YOU" : "OPPONENT", HeaderName = who,
                HeaderRight = armor > 0 ? $"{hp} HP + {armor}" : $"{hp} HP",
            };

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
                var (tags, hints) = ThreatTags(m, info);
                s.Lines.Add(new AdviceLine($"{info?.Name ?? "?"} {m.Attack}/{m.Health}{(notes.Length > 0 ? " - " + notes : "")}", Threat(m) >= 8 ? Tone.Bad : Tone.Normal)
                    { CardName = info?.Name ?? "?", Aside = $"{m.Attack}/{m.Health}", Tags = tags, Hint = hints.Count > 0 ? string.Join(" · ", hints) : null });

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


        /// <summary>
        /// The same notes as <see cref="ThreatNotes"/>, split for the overlay: short keyword tags (the dangerous ones
        /// marked hot) and the advice that goes with them.
        /// </summary>
        private static (List<Chip> Tags, List<string> Hints) ThreatTags(Entity m, CardInfo info)
        {
            var tags = new List<Chip>();
            var hints = new List<string>();
            var p = info?.Profile;
            void Tag(string flag, string label, bool hot, string hint = null)
            {
                if (!m.Flag(flag)) return;
                tags.Add(new Chip(label, hot));
                if (hint != null) hints.Add(hint);
            }
            Tag("TAUNT", "Taunt", false);
            Tag("DIVINE_SHIELD", "Shield", false, "ping the shield first");
            Tag("POISONOUS", "Poison", true, "trade a token into it");
            Tag("STEALTH", "Stealth", true, "needs AoE");
            Tag("WINDFURY", "Windfury", true);
            Tag("LIFESTEAL", "Lifesteal", false);
            Tag("REBORN", "Reborn", false, "kill it twice");
            if (p != null && !m.Flag("SILENCED"))
            {
                if (p.Deathrattle) { tags.Add(new Chip("Deathrattle")); hints.Add("silence beats killing"); }
                if (p.Aura) { tags.Add(new Chip("Aura", true)); hints.Add("kill it first"); }
                if (p.Trigger) { tags.Add(new Chip("Each turn", true)); hints.Add("kill it first"); }
            }
            return (tags, hints.Distinct().ToList());
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
    }
}
