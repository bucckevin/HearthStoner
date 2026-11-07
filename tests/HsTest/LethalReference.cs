using System.Collections.Generic;
using System.Linq;
using Xunit;
using static HearthStoner.Advice.LethalSolver;

namespace HearthStoner.Tests
{
    /// <summary>
    /// The original, straightforward (slow) LethalSolver, kept as a reference: the fast solver must agree with it
    /// on face damage and lethal for random boards.
    /// </summary>
    public class LethalReferenceTests
    {
        private static int Reference(List<Attacker> attackers, List<Taunt> taunts, List<Spell> spells, int mana)
        {
            var atk = new List<Attacker>();
            foreach (var a in attackers.Where(a => a.Attack > 0)) { atk.Add(a); if (a.Windfury) atk.Add(new Attacker { Attack = a.Attack, CanHitFace = a.CanHitFace }); }
            atk = atk.OrderByDescending(a => a.Attack).Take(8).ToList();
            var ts = taunts.Take(3).ToList();
            var sp = spells.Where(s => s.Damage > 0 && s.Cost <= mana).OrderByDescending(s => s.Damage).Take(4).ToList();
            var bestFace = -1;
            int[] Digits(int n, int b, int len) { var d = new int[len]; for (var i = 0; i < len; i++) { d[i] = n % b; n /= b; } return d; }
            int Pow(int b, int e) { var r = 1; for (var i = 0; i < e; i++) r *= b; return r; }
            bool Kills(Taunt t, List<int> hits) { if (hits.Count == 0) return false; var h = hits.OrderBy(x => x).ToList(); if (t.DivineShield) h.RemoveAt(0); return h.Sum() >= t.Health; }
            for (var sm = 0; sm < (1 << sp.Count); sm++)
            {
                var chosen = Enumerable.Range(0, sp.Count).Where(i => (sm & (1 << i)) != 0).Select(i => sp[i]).ToList();
                if (chosen.Sum(s => s.Cost) > mana) continue;
                for (var st = 0; st < Pow(ts.Count + 1, chosen.Count); st++)
                {
                    var sT = Digits(st, ts.Count + 1, chosen.Count);
                    if (Enumerable.Range(0, chosen.Count).Any(i => sT[i] == ts.Count && !chosen[i].CanHitFace)) continue;
                    for (var c = 0; c < Pow(ts.Count + 1, atk.Count); c++)
                    {
                        var aT = Digits(c, ts.Count + 1, atk.Count);
                        var face = 0; var ok = true;
                        for (var i = 0; i < atk.Count; i++) if (aT[i] == ts.Count) { if (!atk[i].CanHitFace) { ok = false; break; } face += atk[i].Attack; }
                        for (var t = 0; t < ts.Count && ok; t++)
                            ok = Kills(ts[t], chosen.Where((s, i) => sT[i] == t).Select(s => s.Damage).Concat(atk.Where((a, i) => aT[i] == t).Select(a => a.Attack)).ToList());
                        if (!ok) continue;
                        face += chosen.Where((s, i) => sT[i] == ts.Count).Sum(s => s.Damage);
                        if (face > bestFace) bestFace = face;
                    }
                }
            }
            return bestFace;
        }

        [Fact]
        public void Fast_solver_matches_reference_on_random_boards()
        {
            var rng = new System.Random(1455);
            for (var n = 0; n < 3000; n++)
            {
                var attackers = Enumerable.Range(0, rng.Next(0, 5)).Select(i => new Attacker { Name = "a" + i, Attack = rng.Next(0, 7), CanHitFace = rng.Next(5) > 0, Windfury = rng.Next(8) == 0 }).ToList();
                var taunts = Enumerable.Range(0, rng.Next(0, 3)).Select(i => new Taunt { Name = "t" + i, Health = rng.Next(1, 8), DivineShield = rng.Next(4) == 0 }).ToList();
                var spells = Enumerable.Range(0, rng.Next(0, 3)).Select(i => new Spell { Name = "s" + i, Cost = rng.Next(0, 5), Damage = rng.Next(1, 6), CanHitFace = rng.Next(3) > 0 }).ToList();
                var mana = rng.Next(0, 8);
                var refFace = Reference(attackers, taunts, spells, mana);
                // needed = more than possible, so the fast solver can't stop early and must report its best face damage.
                var fast = Solve(attackers, taunts, spells, mana, 1000);
                Assert.True(refFace == (fast.Steps.Count == 0 && refFace <= 0 ? refFace : fast.FaceDamage),
                    $"case {n}: reference {refFace}, fast {fast.FaceDamage}");
                if (refFace > 0) Assert.True(Solve(attackers, taunts, spells, mana, refFace).Lethal, $"case {n}: lethal for {refFace}");
            }
        }
    }
}
