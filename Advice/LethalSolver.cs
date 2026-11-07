using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    /// <summary>
    /// Can your attackers get through the enemy Taunts and still deal enough face damage? Tries every way of
    /// sending each attacker at a Taunt or at face (at most 8 attacks and 3 Taunts, so 4^8 = 65k cases), plus
    /// single-target damage spells aimed at Taunts or face. Allocation-free inner loop. Divine Shield needs one extra hit. Pure function: unit-tested.
    /// </summary>
    public static class LethalSolver
    {
        public sealed class Attacker
        {
            public string Name;
            public int Attack;
            public bool CanHitFace = true;   // Rush minions the turn they're played can only hit minions
            public bool Windfury;
        }

        public sealed class Taunt
        {
            public string Name;
            public int Health;
            public bool DivineShield;
        }

        public sealed class Spell
        {
            public string Name;
            public int Cost;
            public int Damage;
            public bool CanHitFace;
        }

        public sealed class Plan
        {
            public bool Lethal;
            public int FaceDamage;
            public List<string> Steps = new List<string>();
        }

        /// <param name="needed">Enemy health + armor.</param>
        public static Plan Solve(List<Attacker> attackers, List<Taunt> taunts, List<Spell> spells, int mana, int needed)
        {
            // Each windfury attacker attacks twice: model as two attackers.
            var atk = new List<Attacker>();
            foreach (var a in attackers.Where(a => a.Attack > 0))
            {
                atk.Add(a);
                if (a.Windfury) atk.Add(new Attacker { Name = a.Name + " (2nd attack)", Attack = a.Attack, CanHitFace = a.CanHitFace });
            }
            atk = atk.OrderByDescending(a => a.Attack).Take(8).ToList();
            var ts = taunts.Take(3).ToList();

            // Spell subsets within mana.
            var sp = spells.Where(s => s.Damage > 0 && s.Cost <= mana).OrderByDescending(s => s.Damage).Take(4).ToList();
            var budget = 250000;   // keeps a worst case under a few milliseconds
            Plan best = new Plan();
            var bestFace = -1;

            for (var sm = 0; sm < (1 << sp.Count); sm++)
            {
                var cost = 0;
                for (var i = 0; i < sp.Count; i++) if ((sm & (1 << i)) != 0) cost += sp[i].Cost;
                if (cost > mana) continue;
                var chosen = Enumerable.Range(0, sp.Count).Where(i => (sm & (1 << i)) != 0).Select(i => sp[i]).ToList();

                // Each chosen spell goes at face or at one Taunt: (ts.Count + 1)^chosen.Count options.
                var spellTargets = Pow(ts.Count + 1, chosen.Count);
                for (var st = 0; st < spellTargets; st++)
                {
                    var spellTarget = Digits(st, ts.Count + 1, chosen.Count);
                    if (Enumerable.Range(0, chosen.Count).Any(i => spellTarget[i] == ts.Count && !chosen[i].CanHitFace)) continue;

                    // Spell damage on each Taunt is fixed for this spell assignment.
                    var spSum = new int[ts.Count]; var spCnt = new int[ts.Count]; var spMin = new int[ts.Count];
                    var spellFace = 0;
                    for (var t = 0; t < ts.Count; t++) spMin[t] = int.MaxValue;
                    for (var i = 0; i < chosen.Count; i++)
                    {
                        var t = spellTarget[i];
                        if (t == ts.Count) { spellFace += chosen[i].Damage; continue; }
                        spSum[t] += chosen[i].Damage; spCnt[t]++; if (chosen[i].Damage < spMin[t]) spMin[t] = chosen[i].Damage;
                    }

                    // Odometer over attacker targets (0..ts.Count-1 = a Taunt, ts.Count = face); no allocations per case.
                    var target = new int[atk.Count];
                    var sum = new int[ts.Count]; var cnt = new int[ts.Count]; var min = new int[ts.Count];
                    var combos = Pow(ts.Count + 1, atk.Count);
                    for (var c = 0; c < combos; c++)
                    {
                        if (c > 0)
                            for (var i = 0; i < atk.Count; i++) { if (++target[i] <= ts.Count) break; target[i] = 0; }
                        if (--budget <= 0) return best;

                        var face = 0;
                        var ok = true;
                        for (var t = 0; t < ts.Count; t++) { sum[t] = spSum[t]; cnt[t] = spCnt[t]; min[t] = spMin[t]; }
                        for (var i = 0; i < atk.Count; i++)
                        {
                            var t = target[i];
                            var a = atk[i].Attack;
                            if (t == ts.Count) { if (!atk[i].CanHitFace) { ok = false; break; } face += a; }
                            else { sum[t] += a; cnt[t]++; if (a < min[t]) min[t] = a; }
                        }
                        if (!ok) continue;
                        // Every Taunt must die before anything hits face. Divine Shield absorbs the smallest hit.
                        for (var t = 0; t < ts.Count && ok; t++)
                            ok = cnt[t] > 0 && (ts[t].DivineShield ? sum[t] - min[t] : sum[t]) >= ts[t].Health;
                        if (!ok) continue;
                        face += spellFace;
                        if (face > bestFace)
                        {
                            bestFace = face;
                            best = new Plan { FaceDamage = face, Lethal = face >= needed };
                            for (var t = 0; t < ts.Count; t++)
                            {
                                var who = Enumerable.Range(0, atk.Count).Where(i => target[i] == t).Select(i => atk[i].Name)
                                    .Concat(Enumerable.Range(0, chosen.Count).Where(i => spellTarget[i] == t).Select(i => chosen[i].Name)).ToList();
                                if (who.Count > 0) best.Steps.Add($"{string.Join(" + ", who)} -> {ts[t].Name}");
                            }
                            var faceWho = Enumerable.Range(0, atk.Count).Where(i => target[i] == ts.Count).Select(i => atk[i].Name)
                                .Concat(Enumerable.Range(0, chosen.Count).Where(i => spellTarget[i] == ts.Count).Select(i => chosen[i].Name)).ToList();
                            if (faceWho.Count > 0) best.Steps.Add($"face: {string.Join(", ", faceWho)} ({face})");
                        }
                        if (best.Lethal) break;
                    }
                    if (best.Lethal) break;
                }
                if (best.Lethal) break;
            }
            return best;
        }

        private static int Pow(int b, int e) { var r = 1; for (var i = 0; i < e; i++) { r *= b; if (r > 1_000_000) return r; } return r; }

        private static int[] Digits(int n, int b, int len)
        {
            var d = new int[len];
            for (var i = 0; i < len; i++) { d[i] = n % b; n /= b; }
            return d;
        }
    }
}
