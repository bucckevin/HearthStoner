using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    public sealed class PlayerDeck
    {
        public string Name;
        public string Code;
        public string Class;
        public List<string> Cards;
        /// <summary>Pasted in by you (a list from a streamer or a meta site), not one of your saved decks.</summary>
        public bool Imported;
    }

    /// <summary>
    /// Out-of-game planning from the meta stats: which of your decks to queue (expected win rate against the field
    /// at your rank), how to tune a deck (card swaps the data favours, and the best-performing list of its archetype),
    /// and what to craft (cheapest top decks to complete with your collection).
    /// </summary>
    public sealed class DeckPlanner
    {
        private readonly MetaStats _meta;
        private readonly CardDb _cards;
        private readonly OwnedCollection _collection;
        private readonly Action<string> _log;

        public DeckPlanner(MetaStats meta, CardDb cards, OwnedCollection collection, Action<string> log)
        {
            _meta = meta;
            _cards = cards;
            _collection = collection;
            _log = log;
        }

        public static List<PlayerDeck> FromCodes(IEnumerable<QueuedDeck> decks, CardDb cards) =>
            decks.Where(d => d.DeckCode != null)
                 .Select(d => new PlayerDeck { Name = d.Name, Code = d.DeckCode, Class = DeckCode.HeroClass(d.DeckCode, cards), Cards = DeckCode.Decode(d.DeckCode, cards) })
                 .Where(d => d.Cards != null && d.Cards.Count >= 20)
                 .ToList();

        /// <summary>How often each class shows up in the meta (share of games).</summary>
        public Dictionary<string, double> ClassField()
        {
            var total = (double)_meta.Archetypes.Sum(a => a.Games);
            return _meta.Archetypes.GroupBy(a => a.Class).ToDictionary(g => g.Key, g => total > 0 ? g.Sum(a => a.Games) / total : 0);
        }

        // ------------------------------------------------------------------ which deck to play

        public sealed class ClassMatchup
        {
            public string Class;
            /// <summary>How much of the field this class is.</summary>
            public double Share;
            public double WinRate;
            /// <summary>False when there weren't enough games for this matchup and the archetype's overall rate is used.</summary>
            public bool FromData;
        }

        public sealed class DeckPick
        {
            public PlayerDeck Deck;
            /// <summary>Null when the deck doesn't match a meta archetype.</summary>
            public MetaArchetype Archetype;
            public double Expected;
            /// <summary>Against every class in the field, most common first.</summary>
            public List<ClassMatchup> Matchups = new List<ClassMatchup>();
            public ClassMatchup Best, Worst;
            /// <summary>A legendary from the deck to show as its picture (different for each deck when possible).</summary>
            public string SignatureCardId;
        }

        /// <summary>Your decks with their expected win rate against today's field, best first; unmatched decks last.</summary>
        public List<DeckPick> PickDecks(List<PlayerDeck> decks)
        {
            var field = ClassField();
            var picks = new List<DeckPick>();
            foreach (var d in decks)
            {
                var pick = new DeckPick { Deck = d, Archetype = _meta.MatchDeck(d.Class, d.Cards) };
                picks.Add(pick);
                if (pick.Archetype == null) continue;
                var det = _meta.Detail(pick.Archetype.Id, _log);
                double exp = 0, weight = 0;
                foreach (var kv in field.OrderByDescending(kv => kv.Value))
                {
                    var m = new ClassMatchup { Class = kv.Key, Share = kv.Value, WinRate = pick.Archetype.WinRate };
                    if (det != null && det.Matchups.TryGetValue(kv.Key, out var mu) && mu.Games >= 50) { m.WinRate = mu.WinRate; m.FromData = true; }
                    exp += kv.Value * m.WinRate; weight += kv.Value;
                    pick.Matchups.Add(m);
                }
                pick.Expected = weight > 0 ? exp / weight : pick.Archetype.WinRate;
                // Matchups that matter: classes that are at least 5% of the field.
                var common = pick.Matchups.Where(m => m.Share >= 0.05 && m.FromData).ToList();
                pick.Best = common.OrderByDescending(m => m.WinRate).FirstOrDefault();
                pick.Worst = common.OrderBy(m => m.WinRate).FirstOrDefault();
            }
            var ordered = picks.Where(p => p.Archetype != null).OrderByDescending(p => p.Expected)
                .Concat(picks.Where(p => p.Archetype == null)).ToList();
            // Your own decks pick their pictures first; pasted-in decks take what's left.
            AssignSignatures(ordered.OrderBy(p => p.Deck.Imported).Select(p => (p.Deck, (Action<string>)(id => p.SignatureCardId = id))));
            return ordered;
        }

        /// <summary>
        /// Picks a card to represent each deck: its most expensive legendary that no earlier deck already uses
        /// (then any legendary, then its most expensive card).
        /// </summary>
        public void AssignSignatures(IEnumerable<(PlayerDeck Deck, Action<string> Set)> decks)
        {
            var used = new HashSet<string>();
            foreach (var (deck, set) in decks)
            {
                var ranked = deck.Cards.Distinct().Select(_cards.ById).Where(c => c != null)
                    .OrderByDescending(c => c.Rarity == "LEGENDARY").ThenByDescending(c => c.Cost ?? 0).ThenBy(c => c.Name).ToList();
                var pick = ranked.FirstOrDefault(c => c.Rarity == "LEGENDARY" && !used.Contains(c.Name)) ?? ranked.FirstOrDefault();
                if (pick == null) continue;
                used.Add(pick.Name);
                set(pick.Id);
            }
        }

        public AdviceSection DeckPicker(List<PlayerDeck> decks)
        {
            var s = new AdviceSection($"Which deck to play ({_meta.Rank}, {_meta.Period})");
            var picks = PickDecks(decks);
            var top = picks.Where(p => p.Archetype != null).ToList();
            foreach (var p in picks.Where(p => p.Archetype == null)) s.Add($"{p.Deck.Name}: not matched to a meta archetype", Tone.Dim);
            string Mu(ClassMatchup m) => m == null ? "" : $"{MetaStats.ClassName(m.Class)} {m.WinRate:P0}";
            foreach (var r in top)
                s.Add($"{r.Expected:P1}  {r.Deck.Name} ({r.Archetype.Name})   best vs {Mu(r.Best)}, worst vs {Mu(r.Worst)}",
                    r == top.First() ? Tone.Good : r.Expected < 0.5 ? Tone.Bad : Tone.Normal);
            if (top.Count > 0) s.Add($"Expected win rate against today's mix of classes at {_meta.Rank}. Queue {top[0].Deck.Name}.", Tone.Dim);
            var fieldLine = string.Join(", ", ClassField().OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{MetaStats.ClassName(kv.Key)} {kv.Value:P0}"));
            s.Add($"Field: {fieldLine}", Tone.Dim);
            return s;
        }

        // ------------------------------------------------------------------ tuning one deck

        public sealed class Swap
        {
            /// <summary>Your card to cut (null if this is only an addition).</summary>
            public string OutId;
            public double OutWinRate;
            public string InId;
            public double InWinRate;
            /// <summary>Share of the archetype's lists that run the card (0..1).</summary>
            public double InPlayRate;
            /// <summary>Copies you own of the card to add, or -1 if your collection isn't known.</summary>
            public int InOwned = -1;
            public int InCraftCost;
        }

        public sealed class BestList
        {
            public double WinRate;
            public int Games;
            public string Code;
            public List<string> Add = new List<string>();
            public List<string> Remove = new List<string>();
            /// <summary>Dust to build it from your collection, or -1 if your collection isn't known.</summary>
            public int Dust = -1;
        }

        public sealed class TuningPlan
        {
            public MetaArchetype Archetype;
            public double Baseline;
            public List<Swap> Swaps = new List<Swap>();
            public BestList Best;
        }

        /// <summary>Card swaps the data favours for this deck, and the best-performing list of its archetype. Null if unmatched.</summary>
        public TuningPlan Tune(PlayerDeck deck)
        {
            var arch = _meta.MatchDeck(deck.Class, deck.Cards);
            if (arch == null) return null;
            var plan = new TuningPlan { Archetype = arch, Baseline = arch.WinRate };
            var baseline = arch.WinRate;
            var mine = new HashSet<string>(deck.Cards.Select(id => _cards.NameOf(id)));
            // Played (drawn) win rate per card, for cards with enough games.
            var stats = arch.Cards.Values.Where(c => c.Drawn >= 200).ToDictionary(c => c.CardId, c => c);

            var weakest = deck.Cards.Distinct()
                .Select(id => stats.TryGetValue(id, out var st) ? new { Id = id, St = st } : null)
                .Where(x => x != null && x.St.DrawnWinRate < baseline - 0.01)
                .OrderBy(x => x.St.DrawnWinRate).Take(4).ToList();
            var candidates = stats.Values
                .Where(c => !mine.Contains(_cards.NameOf(c.CardId)) && arch.Copies(c.CardId) >= 0.2 && c.DrawnWinRate > baseline + 0.01)
                .OrderByDescending(c => c.DrawnWinRate).Take(4).ToList();
            for (var i = 0; i < Math.Max(weakest.Count, candidates.Count); i++)
            {
                var sw = new Swap();
                if (i < weakest.Count) { sw.OutId = weakest[i].Id; sw.OutWinRate = weakest[i].St.DrawnWinRate; }
                if (i < candidates.Count)
                {
                    var c = candidates[i];
                    sw.InId = c.CardId; sw.InWinRate = c.DrawnWinRate; sw.InPlayRate = arch.Copies(c.CardId) / 2;
                    sw.InCraftCost = OwnedCollection.CraftCost(_cards.ById(c.CardId)?.Rarity);
                    if (_collection != null) sw.InOwned = _collection.CountByName(_cards, c.CardId);
                }
                plan.Swaps.Add(sw);
            }

            // The archetype's best list with enough games, and how far yours is from it.
            var best = _meta.Decks(_log).Where(d => d.ArchetypeKey == arch.Key && d.Games >= 100).OrderByDescending(d => d.WinRate).FirstOrDefault();
            var bestCards = best == null ? null : DeckCode.Decode(best.DeckCode, _cards);
            if (bestCards != null)
            {
                var (add, remove) = Diff(deck.Cards, bestCards);
                plan.Best = new BestList { WinRate = best.WinRate, Games = best.Games, Code = best.DeckCode, Add = add, Remove = remove };
                if (_collection != null)
                    plan.Best.Dust = bestCards.GroupBy(id => _cards.NameOf(id))
                        .Sum(g => Math.Max(0, g.Count() - _collection.CountByName(_cards, g.First())) * OwnedCollection.CraftCost(_cards.ById(g.First())?.Rarity));
            }
            return plan;
        }

        public AdviceSection Tuning(PlayerDeck deck)
        {
            var plan = Tune(deck);
            var s = new AdviceSection($"Tuning: {deck.Name}{(plan != null ? $" ({plan.Archetype.Name}, {plan.Baseline:P1})" : "")}");
            if (plan == null) { s.Add("Not matched to a meta archetype, so there's no data to tune against.", Tone.Dim); return s; }
            if (plan.Swaps.Count == 0) s.Add("Your list lines up with what wins in this archetype.", Tone.Good);
            foreach (var sw in plan.Swaps)
            {
                var outText = sw.OutId != null ? $"-{_cards.NameOf(sw.OutId)} ({sw.OutWinRate:P0} when drawn)" : "";
                var inText = sw.InId != null ? $"+{_cards.NameOf(sw.InId)} ({sw.InWinRate:P0}, in {sw.InPlayRate:P0} of lists){Ownership(sw.InId)}" : "";
                s.Add($"{outText}{(outText.Length > 0 && inText.Length > 0 ? "  ->  " : "")}{inText}", Tone.Normal);
            }
            s.Add($"Win rate in games where the card was drawn, vs the archetype's {plan.Baseline:P1}.", Tone.Dim);
            var b = plan.Best;
            if (b != null)
            {
                s.Add($"Best list: {b.WinRate:P1} over {b.Games:N0} games ({b.Add.Count} card{(b.Add.Count == 1 ? "" : "s")} different{(b.Dust > 0 ? $", {b.Dust:N0} dust to craft" : "")})", Tone.Heading);
                if (b.Add.Count > 0) s.Add("  + " + Group(b.Add), Tone.Good);
                if (b.Remove.Count > 0) s.Add("  - " + Group(b.Remove), Tone.Bad);
                s.Add($"  Code: {b.Code}", Tone.Dim);
            }
            return s;
        }

        private string Group(List<string> ids) =>
            string.Join(", ", ids.GroupBy(x => x).Select(g => $"{(g.Count() > 1 ? $"{g.Count()}x " : "")}{_cards.NameOf(g.Key)}"));

        private string Ownership(string cardId)
        {
            if (_collection == null) return "";
            var owned = _collection.CountByName(_cards, cardId);
            if (owned > 0) return " - owned";
            var cost = OwnedCollection.CraftCost(_cards.ById(cardId)?.Rarity);
            return cost > 0 ? $" - craft {cost}" : "";
        }

        /// <summary>Cards to add (in target, not in yours) and remove, compared by name, with copies.</summary>
        private (List<string> Add, List<string> Remove) Diff(List<string> mine, List<string> target)
        {
            var myCounts = mine.GroupBy(id => _cards.NameOf(id)).ToDictionary(g => g.Key, g => g.ToList());
            var tCounts = target.GroupBy(id => _cards.NameOf(id)).ToDictionary(g => g.Key, g => g.ToList());
            var add = new List<string>();
            var remove = new List<string>();
            foreach (var kv in tCounts)
            {
                var have = myCounts.TryGetValue(kv.Key, out var m) ? m.Count : 0;
                for (var i = have; i < kv.Value.Count; i++) add.Add(kv.Value[0]);
            }
            foreach (var kv in myCounts)
            {
                var want = tCounts.TryGetValue(kv.Key, out var t) ? t.Count : 0;
                for (var i = want; i < kv.Value.Count; i++) remove.Add(kv.Value[0]);
            }
            return (add, remove);
        }

        // ------------------------------------------------------------------ crafting

        public sealed class CraftOption
        {
            public MetaArchetype Archetype;
            public MetaDeck Deck;
            public int Dust;
            public List<string> Missing = new List<string>();
        }

        public sealed class SharedCraft
        {
            public string CardId;
            public int Cost;
            public List<string> Archetypes = new List<string>();
        }

        public sealed class CraftPlan
        {
            /// <summary>False until your collection has been read once.</summary>
            public bool HasCollection;
            public DateTime CollectionUpdated;
            /// <summary>Strong, popular decks, cheapest to complete first.</summary>
            public List<CraftOption> Options = new List<CraftOption>();
            /// <summary>Single crafts that several of those decks need.</summary>
            public List<SharedCraft> Shared = new List<SharedCraft>();
        }

        public CraftPlan Craft()
        {
            var plan = new CraftPlan { HasCollection = _collection != null };
            if (_collection == null) return plan;
            plan.CollectionUpdated = _collection.Updated;
            // For each strong, popular archetype: its most played list and the dust you'd need to complete it.
            var options = new List<CraftOption>();
            foreach (var a in _meta.Archetypes.Where(a => !a.IsCatchAll && a.Games >= 1000 && a.WinRate >= 0.52))
            {
                var deck = _meta.Decks(_log).Where(d => d.ArchetypeKey == a.Key && d.Games >= 100).OrderByDescending(d => d.Games * d.WinRate).FirstOrDefault();
                var list = deck == null ? null : DeckCode.Decode(deck.DeckCode, _cards);
                if (list == null) continue;
                var o = new CraftOption { Archetype = a, Deck = deck };
                foreach (var g in list.GroupBy(id => _cards.NameOf(id)))
                {
                    var need = g.Count() - _collection.CountByName(_cards, g.First());
                    for (var i = 0; i < need; i++) o.Missing.Add(g.First());
                }
                o.Dust = o.Missing.Sum(id => OwnedCollection.CraftCost(_cards.ById(id)?.Rarity));
                options.Add(o);
            }
            plan.Options = options.OrderBy(o => o.Dust).ThenByDescending(o => o.Archetype.WinRate).Take(8).ToList();
            plan.Shared = options.SelectMany(o => o.Missing.Distinct().Select(id => new { Name = _cards.NameOf(id), Id = id, o.Archetype }))
                .GroupBy(x => x.Name)
                .Select(g => new SharedCraft { CardId = g.First().Id, Cost = OwnedCollection.CraftCost(_cards.ById(g.First().Id)?.Rarity), Archetypes = g.Select(x => x.Archetype.Name).Distinct().ToList() })
                .Where(g => g.Archetypes.Count >= 2)
                .OrderByDescending(g => g.Archetypes.Count).Take(5).ToList();
            return plan;
        }

        public AdviceSection Crafting()
        {
            var s = new AdviceSection("What to craft");
            var plan = Craft();
            if (!plan.HasCollection) { s.Add("Run 'HearthStoner collection' once (with Hearthstone open) so HearthStoner knows what you own.", Tone.Dim); return s; }
            s.Add($"Collection as of {plan.CollectionUpdated:g}.", Tone.Dim);
            if (plan.Options.Count == 0) { s.Add("No meta decklists available right now.", Tone.Dim); return s; }
            foreach (var o in plan.Options)
            {
                var tone = o.Dust == 0 ? Tone.Good : o.Dust <= 3200 ? Tone.Normal : Tone.Dim;
                s.Add($"{o.Archetype.Name} ({o.Deck.WinRate:P1}): {(o.Dust == 0 ? "you own every card" : $"{o.Dust:N0} dust, {o.Missing.Count} card{(o.Missing.Count == 1 ? "" : "s")}")}", tone);
                if (o.Missing.Count > 0 && o.Missing.Count <= 6)
                    s.Add("   " + string.Join(", ", o.Missing.GroupBy(x => x).Select(g => $"{(g.Count() > 1 ? "2x " : "")}{_cards.NameOf(g.Key)}")), Tone.Dim);
            }
            if (plan.Shared.Count > 0)
            {
                s.Add("Crafts that help several top decks:", Tone.Heading);
                foreach (var g in plan.Shared)
                    s.Add($"   {_cards.NameOf(g.CardId)} ({g.Cost}): {string.Join(", ", g.Archetypes.Take(4))}", Tone.Normal);
            }
            return s;
        }
    }
}
