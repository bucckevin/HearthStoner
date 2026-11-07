using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HearthStoner
{
    /// <summary>
    /// A daily snapshot of the meta numbers HearthStoner shows (each archetype's share and win rate, and your decks'
    /// expected win rates), kept in %LocalAppData%\HearthStoner\meta-history.json, so the Play page can point out what
    /// changed: your deck getting better or worse, an archetype becoming more or less common.
    /// </summary>
    public sealed class MetaHistory
    {
        public sealed class Snapshot
        {
            public DateTime Day;
            public string Rank, Period;
            public Dictionary<string, Arch> Archetypes = new Dictionary<string, Arch>();
            /// <summary>Your deck name -> expected win rate against the field.</summary>
            public Dictionary<string, double> Decks = new Dictionary<string, double>();
        }

        public sealed class Arch
        {
            public string Name;
            public double Share, WinRate;
            public int Place;
        }

        public sealed class Alert
        {
            public string Text;
            /// <summary>Positive is good for you (your deck up, or a deck you beat becoming common is not judged).</summary>
            public double Change;
            public bool AboutYourDeck;
            public double Size;
        }

        public List<Snapshot> Snapshots = new List<Snapshot>();
        private const int KeepDays = 60;

        public static string FilePath => AppPaths.Data("meta-history.json");

        public static MetaHistory Load(string path = null)
        {
            try
            {
                path = path ?? FilePath;
                if (File.Exists(path)) return JsonConvert.DeserializeObject<MetaHistory>(File.ReadAllText(path)) ?? new MetaHistory();
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "MetaHistory"); }
            return new MetaHistory();
        }

        public void Save(string path = null)
        {
            try
            {
                path = path ?? FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "MetaHistory"); }
        }

        public static Snapshot Take(MetaStats meta, IEnumerable<DeckPlanner.DeckPick> picks, DateTime day)
        {
            var s = new Snapshot { Day = day.Date, Rank = meta.Rank, Period = meta.Period };
            var place = 0;
            foreach (var a in meta.Archetypes.OrderByDescending(a => a.Share))
            {
                place++;
                if (string.IsNullOrEmpty(a.Key) || s.Archetypes.ContainsKey(a.Key)) continue;
                s.Archetypes[a.Key] = new Arch { Name = a.Name, Share = a.Share, WinRate = a.WinRate, Place = place };
            }
            foreach (var p in picks ?? Enumerable.Empty<DeckPlanner.DeckPick>())
                if (p.Archetype != null && !p.Deck.Imported && !string.IsNullOrEmpty(p.Deck.Name)) s.Decks[p.Deck.Name] = p.Expected;
            return s;
        }

        /// <summary>Adds (or replaces) today's snapshot and drops old ones.</summary>
        public void Add(Snapshot s)
        {
            Snapshots.RemoveAll(x => x.Day == s.Day && x.Rank == s.Rank && x.Period == s.Period);
            Snapshots.Add(s);
            Snapshots.RemoveAll(x => x.Day < s.Day.AddDays(-KeepDays));
            Snapshots = Snapshots.OrderBy(x => x.Day).ToList();
        }

        /// <summary>
        /// The snapshot to compare today with: the newest at least <paramref name="minDays"/> days older, else the
        /// oldest from before today. Null on the first day.
        /// </summary>
        public Snapshot Baseline(Snapshot now, int minDays = 3)
        {
            var same = Snapshots.Where(x => x.Rank == now.Rank && x.Period == now.Period && x.Day < now.Day).ToList();
            return same.Where(x => x.Day <= now.Day.AddDays(-minDays)).OrderByDescending(x => x.Day).FirstOrDefault()
                   ?? same.OrderBy(x => x.Day).FirstOrDefault();
        }

        /// <summary>What changed between two snapshots that's worth a line on the Play page, biggest first.</summary>
        public static List<Alert> Compare(Snapshot before, Snapshot now, int max = 4)
        {
            var alerts = new List<Alert>();
            if (before == null || now == null) return alerts;
            var since = before.Day.ToString("MMM d");
            foreach (var kv in now.Decks)
            {
                if (!before.Decks.TryGetValue(kv.Key, out var was)) continue;
                var d = kv.Value - was;
                if (Math.Abs(d) < 0.015) continue;
                alerts.Add(new Alert
                {
                    AboutYourDeck = true, Change = d, Size = Math.Abs(d) * 4,
                    Text = $"{kv.Key}: expected win rate {kv.Value:P1}, {(d > 0 ? "up" : "down")} from {was:P1} on {since}.",
                });
            }
            foreach (var kv in now.Archetypes.Where(a => a.Value.Place <= 12))
            {
                before.Archetypes.TryGetValue(kv.Key, out var was);
                var a = kv.Value;
                var oldShare = was?.Share ?? 0;
                var d = a.Share - oldShare;
                var relative = oldShare > 0 ? Math.Abs(d) / oldShare : 1;
                if (Math.Abs(d) >= 0.02 || (Math.Abs(d) >= 0.01 && relative >= 0.4))
                    alerts.Add(new Alert
                    {
                        Change = d, Size = Math.Abs(d),
                        Text = was == null
                            ? $"{a.Name} is new in the data: {a.Share:P1} of games."
                            : $"{a.Name}: {a.Share:P1} of games, {(d > 0 ? "up" : "down")} from {oldShare:P1}" + (a.Place <= 5 && was.Place > 5 ? $" (now the #{a.Place} deck)." : "."),
                    });
            }
            // Common decks that dropped out of the top 12.
            foreach (var kv in before.Archetypes.Where(a => a.Value.Place <= 8 && (!now.Archetypes.TryGetValue(a.Key, out var n) || n.Place > 12)))
                alerts.Add(new Alert { Change = -kv.Value.Share, Size = kv.Value.Share, Text = $"{kv.Value.Name} has mostly left the field (was {kv.Value.Share:P1} of games)." });
            return alerts.OrderByDescending(a => a.AboutYourDeck).ThenByDescending(a => a.Size).Take(max).ToList();
        }
    }
}
