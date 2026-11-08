using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HearthStoner.Stats
{
    /// <summary>What one practice question remembers: how you did and when it comes back.</summary>
    public sealed class PracticeCard
    {
        /// <summary>Right answers in a row (0 after a miss).</summary>
        public int Streak;
        public int Right, Wrong;
        /// <summary>The day it's due again in the daily review.</summary>
        public DateTime Due;
        /// <summary>Right <see cref="PracticeStore.RetireAfter"/> times in a row: no longer reviewed.</summary>
        public bool Retired;
    }

    /// <summary>One day's answers, for the Practice page's progress strip.</summary>
    public sealed class PracticeDay
    {
        public DateTime Day;
        public int Right, Wrong;
        [JsonIgnore] public int Answered => Right + Wrong;
    }

    /// <summary>How you've done in one drill: answers right and wrong, and questions retired (learned).</summary>
    public readonly struct DrillAccuracy
    {
        public DrillAccuracy(int right, int wrong, int retired) { Right = right; Wrong = wrong; Retired = retired; }
        public int Right { get; }
        public int Wrong { get; }
        public int Retired { get; }
        public int Answered => Right + Wrong;
        /// <summary>Share of answers that were right; 0 when there are none.</summary>
        public double Rate => Answered == 0 ? 0 : (double)Right / Answered;
    }

    /// <summary>
    /// Spaced repetition for the Practice drills, saved to %LocalAppData%\HearthStoner\practice.json. A question you
    /// miss comes back the next day; right answers push it back 3, then 7 days, and the third right answer in a row
    /// retires it. Questions are keyed by id ("mull:...", "disc:...", see <see cref="PracticeIds"/>); only questions
    /// you've answered are stored.
    /// </summary>
    public sealed class PracticeStore
    {
        /// <summary>Right answers in a row that retire a question.</summary>
        public const int RetireAfter = 3;

        public Dictionary<string, PracticeCard> Cards = new Dictionary<string, PracticeCard>();
        /// <summary>Days you answered at least one question (for the streak).</summary>
        public List<DateTime> Days = new List<DateTime>();
        /// <summary>Answers per day (only days with answers; files from before this was kept start empty).</summary>
        public List<PracticeDay> History = new List<PracticeDay>();

        /// <summary>HEARTHSTONER_PRACTICE points somewhere else (tests and screenshots).</summary>
        public static string Path => Environment.GetEnvironmentVariable("HEARTHSTONER_PRACTICE") is string p && p != ""
            ? p : AppPaths.Data("practice.json");

        /// <summary>Records an answer and schedules the question's next review.</summary>
        /// <param name="id">The question's id.</param>
        /// <param name="right">Whether you got it right.</param>
        /// <param name="today">Today's date (local).</param>
        public void Record(string id, bool right, DateTime today)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A question needs an id.", nameof(id));
            today = today.Date;
            if (!Cards.TryGetValue(id, out var c)) Cards[id] = c = new PracticeCard();
            if (right) { c.Right++; c.Streak++; } else { c.Wrong++; c.Streak = 0; }
            c.Retired = c.Streak >= RetireAfter;
            c.Due = today.AddDays(right ? (c.Streak == 1 ? 3 : 7) : 1);
            if (!Days.Contains(today)) Days.Add(today);
            var day = History.FirstOrDefault(h => h.Day == today);
            if (day == null) History.Add(day = new PracticeDay { Day = today });
            if (right) day.Right++; else day.Wrong++;
        }

        /// <summary>Your answers and retired questions in one drill (see <see cref="PracticeIds"/>).</summary>
        public DrillAccuracy Accuracy(string drill)
        {
            var cards = Cards.Where(kv => PracticeIds.DrillOf(kv.Key) == drill).Select(kv => kv.Value).ToList();
            return new DrillAccuracy(cards.Sum(c => c.Right), cards.Sum(c => c.Wrong), cards.Count(c => c.Retired));
        }

        /// <summary>The last <paramref name="days"/> days up to today, oldest first; days without answers are empty.</summary>
        public List<PracticeDay> LastDays(DateTime today, int days)
        {
            if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days), "Ask for at least one day.");
            return Enumerable.Range(0, days).Select(i => today.Date.AddDays(i - days + 1))
                .Select(d => History.FirstOrDefault(h => h.Day == d) ?? new PracticeDay { Day = d }).ToList();
        }

        /// <summary>Questions due today or earlier, oldest first; retired ones never are.</summary>
        public List<string> Due(DateTime today) =>
            Cards.Where(kv => !kv.Value.Retired && kv.Value.Due <= today.Date).OrderBy(kv => kv.Value.Due).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).ToList();

        /// <summary>
        /// Questions coming due on each of the <paramref name="days"/> days after today, tomorrow first (questions due
        /// today or earlier are today's review, not counted here); retired ones never come back.
        /// </summary>
        public List<(DateTime Day, int Due)> Upcoming(DateTime today, int days)
        {
            if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days), "Ask for at least one day.");
            return Enumerable.Range(1, days).Select(i => today.Date.AddDays(i))
                .Select(d => (d, Cards.Values.Count(c => !c.Retired && c.Due.Date == d))).ToList();
        }

        /// <summary>Days in a row you practised, ending today (or yesterday, so the streak holds until you play today).</summary>
        public int StreakDays(DateTime today)
        {
            var set = new HashSet<DateTime>(Days.Select(d => d.Date));
            var day = set.Contains(today.Date) ? today.Date : today.Date.AddDays(-1);
            var n = 0;
            while (set.Contains(day)) { n++; day = day.AddDays(-1); }
            return n;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "Practice"); }
        }

        /// <summary>The saved store, or an empty one.</summary>
        public static PracticeStore Load()
        {
            try
            {
                if (File.Exists(Path)) return JsonConvert.DeserializeObject<PracticeStore>(File.ReadAllText(Path)) ?? new PracticeStore();
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "Practice"); }
            return new PracticeStore();
        }
    }

    /// <summary>The drills and how their question ids are built: "&lt;drill&gt;:&lt;game&gt;[:&lt;turn&gt;]".</summary>
    public static class PracticeIds
    {
        public const string Play = "play", Hand = "hand", Secret = "secret", Mulligan = "mull", Discover = "disc", Lethal = "lethal";

        /// <summary>Every drill, in the order the Practice page shows them.</summary>
        public static readonly string[] Drills = { Play, Hand, Secret, Mulligan, Discover, Lethal };

        public static string Make(string drill, GameRecord g, int turn = -1) => turn < 0 ? $"{drill}:{g.FileStem}" : $"{drill}:{g.FileStem}:{turn}";

        /// <summary>The drill a question id belongs to.</summary>
        public static string DrillOf(string id) => id?.Split(':')[0];

        /// <summary>The game part of an id (the record's file name).</summary>
        public static string GameOf(string id) => id?.Split(':').ElementAtOrDefault(1);

        /// <summary>The turn (or number) part of an id, or -1.</summary>
        public static int TurnOf(string id) => int.TryParse(id?.Split(':').ElementAtOrDefault(2), out var t) ? t : -1;

        /// <summary>A shuffle seed that is the same for the same id in every run (string.GetHashCode isn't).</summary>
        public static int Seed(string id)
        {
            unchecked
            {
                var h = (int)2166136261;
                foreach (var ch in id ?? "") h = (h ^ ch) * 16777619;
                return h;
            }
        }
    }
}
