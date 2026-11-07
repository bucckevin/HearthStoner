using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    public sealed partial class Advisor
    {

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
