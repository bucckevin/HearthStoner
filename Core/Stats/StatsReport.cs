using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>
    /// Personal stats from your saved games: win rates by deck and matchup compared with the meta, mulligan habits,
    /// tilt patterns, an estimate of games to your next rank, and the review notes that come up most.
    /// </summary>
    public static class StatsReport
    {
        public static List<AdviceSection> Build(List<GameRecord> games, MetaStats meta, CardDb cards, Func<string, int, ArchetypeDetail> detail)
        {
            var result = new List<AdviceSection>();
            var ranked = games.Where(g => g.Decided).ToList();
            var summary = new AdviceSection("Overall");
            result.Add(summary);
            if (ranked.Count == 0)
            {
                summary.Add("No finished games recorded yet. Play with 'HearthStoner overlay' or 'watch' running.", Tone.Dim);
                return result;
            }
            var wins = ranked.Count(g => g.Won);
            summary.Add($"{ranked.Count} games, {wins}-{ranked.Count - wins} ({(double)wins / ranked.Count:P1})", Tone.Heading);
            summary.Add($"Going first: {Wr(ranked.Where(g => g.WentFirst))}   On the coin: {Wr(ranked.Where(g => !g.WentFirst))}");
            var last = ranked.Skip(Math.Max(0, ranked.Count - 20)).ToList();
            summary.Add($"Last {last.Count}: {Wr(last)}");

            // ---- by deck, against the meta's win rate for that archetype
            var decks = new AdviceSection("By deck");
            result.Add(decks);
            foreach (var d in ranked.GroupBy(g => g.DeckLabel).OrderByDescending(g => g.Count()))
            {
                var arch = d.Select(g => g.MyArchetype).FirstOrDefault(a => a != null);
                var metaWr = arch != null ? meta?.Archetypes.FirstOrDefault(a => a.Name == arch)?.WinRate : null;
                var mine = (double)d.Count(g => g.Won) / d.Count();
                var vs = metaWr.HasValue ? $"  meta {metaWr:P1} ({(mine - metaWr.Value >= 0 ? "+" : "")}{(mine - metaWr.Value) * 100:F1})" : "";
                decks.Add($"{d.Key}{(arch != null && arch != d.Key ? $" ({arch})" : "")}: {Wr(d)}{vs}",
                    metaWr.HasValue && d.Count() >= 10 ? (mine >= metaWr.Value ? Tone.Good : Tone.Bad) : Tone.Normal);
            }

            // ---- by opponent class (and archetype), with the meta's matchup numbers for your archetype
            var mu = new AdviceSection("By matchup");
            result.Add(mu);
            foreach (var c in ranked.GroupBy(g => g.OpponentClass ?? "?").OrderByDescending(g => g.Count()))
            {
                var myWr = (double)c.Count(g => g.Won) / c.Count();
                // Average the meta matchup win rate over the archetypes you played into this class.
                var metaVals = new List<double>();
                foreach (var g in c.Where(g => g.MyArchetype != null))
                {
                    var a = meta?.Archetypes.FirstOrDefault(x => x.Name == g.MyArchetype);
                    if (a == null) continue;
                    var det = detail(a.Key, a.Id);
                    if (det != null && det.Matchups.TryGetValue(MetaStats.Key(c.Key), out var m) && m.Games >= 50) metaVals.Add(m.WinRate);
                }
                var vs = metaVals.Count > 0 ? $"  meta {metaVals.Average():P1} ({(myWr - metaVals.Average() >= 0 ? "+" : "")}{(myWr - metaVals.Average()) * 100:F1})" : "";
                mu.Add($"vs {MetaStats.ClassName(c.Key)}: {Wr(c)}{vs}",
                    metaVals.Count > 0 && c.Count() >= 5 ? (myWr >= metaVals.Average() ? Tone.Good : Tone.Bad) : Tone.Normal);
                foreach (var a in c.Where(g => g.OpponentArchetype != null && g.OpponentArchetypeProbability >= 0.6).GroupBy(g => g.OpponentArchetype).OrderByDescending(g => g.Count()).Take(3))
                    mu.Add($"   {a.Key}: {Wr(a)}", Tone.Dim);
            }

            // ---- mulligan habits: cards you keep against the data, or throw away when they win when kept
            var mull = new AdviceSection("Mulligan habits");
            result.Add(mull);
            var picks = ranked.SelectMany(g => g.Mulligan.Select(p => new { g, p })).Where(x => x.p.Advice != null).ToList();
            var habits = picks.GroupBy(x => x.p.CardId)
                .Select(g => new
                {
                    Id = g.Key,
                    Seen = g.Count(),
                    KeptRate = (double)g.Count(x => x.p.Kept) / g.Count(),
                    AvgDelta = g.Average(x => x.p.Delta),
                    Against = g.Count(x => (x.p.Advice == "KEEP" && !x.p.Kept) || (x.p.Advice == "TOSS" && x.p.Kept)),
                })
                .Where(h => h.Seen >= 3 && h.Against >= 2 && Math.Abs(h.AvgDelta) >= 0.015)
                .OrderByDescending(h => h.Against * Math.Abs(h.AvgDelta))
                .Take(8).ToList();
            if (habits.Count == 0) mull.Add(picks.Count == 0 ? "No mulligans with data yet." : "Your mulligans line up with the data.", Tone.Good);
            foreach (var h in habits)
                mull.Lines.Add(new AdviceLine($"{cards.NameOf(h.Id)}: you keep it {h.KeptRate:P0}; it's worth {(h.AvgDelta >= 0 ? "+" : "")}{h.AvgDelta * 100:F1}% when kept -> {(h.AvgDelta >= 0 ? "keep it more" : "throw it back more")}", Tone.Warn)
                {
                    Cells = new[] { cards.NameOf(h.Id), $"{h.KeptRate:P0}", $"{(h.AvgDelta >= 0 ? "+" : "")}{h.AvgDelta * 100:F1}%", h.AvgDelta >= 0 ? "Keep more" : "Toss more" },
                });

            // ---- tilt: results after a loss vs. after a win, and by game number in a session
            var tilt = new AdviceSection("Tilt check");
            result.Add(tilt);
            var afterLoss = new List<GameRecord>();
            var afterWin = new List<GameRecord>();
            var after2Losses = new List<GameRecord>();
            for (var i = 1; i < ranked.Count; i++)
            {
                var gap = ranked[i].Start - ranked[i - 1].End;
                if (gap > TimeSpan.FromMinutes(30)) continue;     // a break resets the streak
                (ranked[i - 1].Won ? afterWin : afterLoss).Add(ranked[i]);
                if (i >= 2 && !ranked[i - 1].Won && !ranked[i - 2].Won && ranked[i - 1].Start - ranked[i - 2].End < TimeSpan.FromMinutes(30))
                    after2Losses.Add(ranked[i]);
            }
            var sessions = SplitSessions(ranked);
            var early = sessions.SelectMany(s => s.Take(5)).ToList();
            var late = sessions.SelectMany(s => s.Skip(5)).ToList();
            // One row each; a row with no games is left out instead of showing "-".
            foreach (var (label, list) in new[] { ("After a win", afterWin), ("After a loss", afterLoss), ("After 2 losses in a row", after2Losses), ("Games 1-5 of a session", early), ("Game 6+ of a session", late) })
            {
                if (list.Count == 0) continue;
                var w = list.Count(x => x.Won);
                tilt.Lines.Add(new AdviceLine($"{label}: {Wr(list)}") { Cells = new[] { label, $"{w}-{list.Count - w}", $"{(double)w / list.Count:P0}" } });
            }
            if (after2Losses.Count >= 8 && Rate(after2Losses) + 0.05 < Rate(ranked))
                tilt.Add($"You win {Rate(after2Losses):P0} after two straight losses vs {Rate(ranked):P0} overall: take a break after two losses.", Tone.Warn);
            if (late.Count >= 10 && Rate(late) + 0.05 < Rate(early))
                tilt.Add($"Long sessions cost you: {Rate(late):P0} after game 5 vs {Rate(early):P0} before. Consider shorter sessions.", Tone.Warn);
            var byHour = ranked.GroupBy(g => g.Start.Hour / 4).Where(g => g.Count() >= 8).OrderBy(g => Rate(g)).ToList();
            if (byHour.Count >= 2 && Rate(byHour.Last()) - Rate(byHour.First()) >= 0.08)
                tilt.Add($"Best hours: {byHour.Last().Key * 4}:00-{byHour.Last().Key * 4 + 4}:00 ({Rate(byHour.Last()):P0}); worst {byHour.First().Key * 4}:00-{byHour.First().Key * 4 + 4}:00 ({Rate(byHour.First()):P0})", Tone.Normal);

            // ---- ladder estimate
            var ladder = new AdviceSection("Ladder");
            result.Add(ladder);
            var latest = ranked.LastOrDefault(g => g.StarLevel > 0 || g.LegendRank > 0);
            if (latest == null) ladder.Add("No ranked games with rank info yet.", Tone.Dim);
            else if (latest.LegendRank > 0) ladder.Add($"Legend {latest.LegendRank:N0}. Recent win rate {Wr(last)}.");
            else
            {
                var wr = Rate(last.Count >= 10 ? last : ranked);
                ladder.Add($"Last seen at {latest.Rank} (star level {latest.StarLevel}). Recent win rate {wr:P1}.");
                foreach (var target in new[] { 11, 21, 31, 41, 51 }.Where(t => t > latest.StarLevel))
                {
                    var name = target == 51 ? "Legend" : new[] { "Bronze", "Silver", "Gold", "Platinum", "Diamond" }[(target - 1) / 10] + " 10";
                    var est = LadderEstimate.GamesToReach(latest.StarLevel, target, wr);
                    ladder.Add(est.HasValue ? $"  to {name}: about {est.Value} games" : $"  to {name}: not climbing at {wr:P0} (need over 50%)", est.HasValue ? Tone.Normal : Tone.Warn);
                }
            }

            // ---- what the reviews keep flagging
            var notes = new AdviceSection("Recurring review notes");
            result.Add(notes);
            var all = games.SelectMany(g => g.Review).ToList();
            if (all.Count == 0) notes.Add("Nothing flagged yet.", Tone.Good);
            foreach (var k in all.GroupBy(n => n.Kind).OrderByDescending(g => g.Count()))
                notes.Add($"{Describe(k.Key)}: {k.Count()} times in {games.Count(g => g.Review.Any(n => n.Kind == k.Key))} games",
                    k.Key == "MissedLethal" || k.Key == "UnspentMana" ? Tone.Warn : Tone.Normal);
            return result;
        }

        private static string Describe(string kind)
        {
            switch (kind)
            {
                case "MissedLethal": return "Possible missed lethal";
                case "UnspentMana": return "Unspent mana with a playable card";
                case "Overextended": return "Overextended into a board clear";
                case "Mulligan": return "Mulligan against the data";
                case "Discover": return "Weaker Discover pick";
                case "Secret": return "Ran into a secret";
                default: return kind;
            }
        }

        public static List<List<GameRecord>> SplitSessions(List<GameRecord> games)
        {
            var sessions = new List<List<GameRecord>>();
            foreach (var g in games)
            {
                if (sessions.Count == 0 || g.Start - sessions.Last().Last().End > TimeSpan.FromMinutes(30)) sessions.Add(new List<GameRecord>());
                sessions.Last().Add(g);
            }
            return sessions;
        }

        private static double Rate(IEnumerable<GameRecord> g)
        {
            var l = g.ToList();
            return l.Count == 0 ? 0 : (double)l.Count(x => x.Won) / l.Count;
        }

        private static string Wr(IEnumerable<GameRecord> g)
        {
            var l = g.ToList();
            if (l.Count == 0) return "-";
            var w = l.Count(x => x.Won);
            return $"{w}-{l.Count - w} ({(double)w / l.Count:P0})";
        }
    }
}
