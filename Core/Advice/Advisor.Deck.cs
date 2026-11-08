using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Advice
{
    public sealed partial class Advisor
    {

        // ================================================================== your deck: draw odds

        private AdviceSection DeckSection(out AdviceLine nextDraw)
        {
            nextDraw = null;
            var me = _game.FriendlyPlayerId;
            var deckCount = _game.DeckCount(me);
            // Arena decks have no name, and a drafted deck isn't a ranked archetype.
            var arena = GameModes.Of(_game.GameType) == GameModes.Kind.Arena;
            var title = _game.FriendlyDeckName ?? (arena ? GameModes.Name(_game.GameType, _game.FormatType) + " deck" : CurrentArchetype?.Name) ?? "Your deck";
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
            if (CurrentDetail != null && CurrentDetail.Matchups.TryGetValue(MetaStats.Key(oppClass), out var m) && m.Games >= 50) mu = m;
            var cards = mu?.Cards ?? CurrentDetail?.Archetype?.Cards ?? CurrentArchetype?.Cards;
            var baseline = mu?.WinRate ?? CurrentDetail?.Archetype?.WinRate ?? CurrentArchetype?.WinRate ?? 0.5;
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

            if (CurrentArchetype != null)
                s.Add($"{CurrentArchetype.Name} vs {MetaStats.ClassName(oppClass)}: {baseline:P1} WR{(mu != null ? $" over {mu.Games:N0} games" : " (all matchups)")}", Tone.Heading);
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
            if (CurrentDetail == null || cardId == null) return null;
            if (CurrentDetail.Matchups.TryGetValue(MetaStats.Key(oppClass), out var mu) && mu.Discover.TryGetValue(cardId, out var ms) && ms.Discovered >= 30) return ms;
            return CurrentDetail.Discover.TryGetValue(cardId, out var st) && st.Discovered >= 30 ? st : null;
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


        // ================================================================== matchup cheat sheet

        private AdviceSection MatchupSection(string oppClass, List<ArchetypeGuess> guesses)
        {
            if (CurrentArchetype == null) return null;
            var (cards, baseline, mu) = MatchupData(oppClass);
            var s = new AdviceSection($"Matchup: {CurrentArchetype.Name} vs {MetaStats.ClassName(oppClass)}");
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
    }
}
