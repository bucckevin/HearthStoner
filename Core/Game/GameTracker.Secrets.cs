using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HearthStoner.Game
{
    public sealed partial class GameTracker
    {
        private sealed class SecretCheck
        {
            public HashSet<SecretTrigger> Triggers = new HashSet<SecretTrigger>();
            public List<int> Secrets = new List<int>();
            public bool TheyHadBoardSpace;
            public bool RevealedDuring;
            public bool CheckYouHaveMinionAtResolve;
            public bool YouHadMinion;
        }

        private readonly List<SecretCheck> _pendingChecks = new List<SecretCheck>();


        private void AddOpponentSecret(Entity e)
        {
            var heroClass = HeroClass(OpponentPlayerId);
            var state = new SecretState { EntityId = e.Id, PlayedTurn = Turn };
            var creator = e.Int("CREATOR");
            if (creator != 0) state.CreatedBy = EntityCardId(creator);

            var cls = e.Tag("CLASS") ?? heroClass;   // a secret keeps its own class even if generated
            var classSecrets = Secrets.ForClass(ClassName(cls) ?? heroClass).Select(s => s.CardId).ToList();
            // You can't have two copies of the same secret active at once.
            var active = Entities.Values.Where(x => x.Controller == OpponentPlayerId && x.Zone == "SECRET" && !string.IsNullOrEmpty(x.CardId)).Select(x => x.CardId);
            foreach (var id in classSecrets.Except(active)) state.Candidates.Add(id);
            OpponentSecrets[e.Id] = state;

            var names = string.Join(", ", state.Candidates.Select(_cards.NameOf));
            Emit(GameEventKind.Secret, false, null,
                $"Opponent played a secret{(state.CreatedBy != null ? $" (created by {_cards.NameOf(state.CreatedBy)})" : "")}: could be {(names.Length > 0 ? names : "anything")}");
        }


        /// <summary>CLASS tag values are numeric in some logs; map the common ones.</summary>
        private static string ClassName(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return null;
            switch (cls)
            {
                case "3": return "HUNTER";
                case "4": return "MAGE";
                case "5": return "PALADIN";
                case "7": return "ROGUE";
                default: return int.TryParse(cls, out _) ? null : cls;
            }
        }


        private void RevealSecret(Entity e)
        {
            if (!OpponentSecrets.Remove(e.Id)) return;
            foreach (var c in _pendingChecks) c.RevealedDuring = true;
            var id = e.KnownCardId;
            if (!string.IsNullOrEmpty(id))
            {
                RevealedSecrets.Add(id);
                Emit(GameEventKind.Secret, false, id, $"Opponent's secret was {_cards.NameOf(id)}");
            }
        }


        private void QueueCheck(IEnumerable<SecretTrigger> triggers)
        {
            if (OpponentSecrets.Count == 0) return;
            var check = new SecretCheck
            {
                Secrets = OpponentSecrets.Keys.ToList(),
                TheyHadBoardSpace = Minions(OpponentPlayerId).Count < 7,
                YouHadMinion = Minions(FriendlyPlayerId).Count > 0,
            };
            foreach (var t in triggers) check.Triggers.Add(t);
            // "After you cast a spell, destroy one of your minions": judge it on the board after the spell resolved.
            check.CheckYouHaveMinionAtResolve = check.Triggers.Contains(SecretTrigger.SpellCast);
            _pendingChecks.Add(check);
        }


        private void QueueYourTurnEnds() => QueueCheck(new[] { SecretTrigger.YourTurnEnds });


        /// <summary>
        /// For each action that would have set off a secret, if no secret was revealed while it resolved, the hidden
        /// secrets can't be any secret with that trigger (provided its condition held).
        /// </summary>
        private void ResolvePendingChecks()
        {
            if (_pendingChecks.Count == 0) return;
            var checks = _pendingChecks.ToList();
            _pendingChecks.Clear();
            var youHaveMinionNow = Minions(FriendlyPlayerId).Count > 0;

            foreach (var c in checks)
            {
                if (c.RevealedDuring) continue;   // something triggered: don't guess which
                foreach (var sid in c.Secrets)
                {
                    if (!OpponentSecrets.TryGetValue(sid, out var state)) continue;
                    foreach (var cand in state.Candidates.ToList())
                    {
                        var rule = Secrets.ById(cand);
                        if (rule == null || !c.Triggers.Contains(rule.Trigger)) continue;
                        if (rule.Condition == SecretCondition.TheyNeedBoardSpace && !c.TheyHadBoardSpace) continue;
                        if (rule.Condition == SecretCondition.YouNeedAMinion)
                        {
                            var had = c.CheckYouHaveMinionAtResolve ? youHaveMinionNow : c.YouHadMinion;
                            if (!had) continue;
                        }
                        state.Candidates.Remove(cand);
                    }
                }
            }
            Changed?.Invoke();
        }
    }
}
