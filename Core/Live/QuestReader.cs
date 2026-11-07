using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    /// <summary>Reads your quests, the reward track and the season end from game memory into a <see cref="QuestLog"/>.</summary>
    public static class QuestReader
    {
        private const int MainRewardTrack = 1;

        /// <summary>Reads the quest log.</summary>
        /// <param name="mem">An attached reader; must not be null.</param>
        /// <param name="definitions">Quest definitions already read (quest id to entry); new ones are added. The game's Quest table doesn't change while it runs, so callers keep this between reads.</param>
        /// <param name="log">Where a failed season-end read is reported; may be null.</param>
        /// <returns>The log, or null when the game has no quests loaded yet.</returns>
        public static QuestLog Read(HsMemory mem, Dictionary<int, QuestEntry> definitions, Action<string> log = null)
        {
            if (mem == null) throw new ArgumentNullException(nameof(mem));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            var quests = mem.GetQuests()?.Quests;
            if (quests == null || quests.Count == 0) return null;
            var result = new QuestLog { Updated = DateTime.Now };
            foreach (var q in quests)
            {
                if (!definitions.TryGetValue(q.Id, out var def)) definitions[q.Id] = def = mem.QuestDef(q.Id);
                if (def != null) result.Quests.Add(def.With(q.Progress, q.Status));
            }
            result.Track = ReadTrack(mem);
            try { result.SeasonEnd = mem.SeasonEnd(); }
            catch (Exception ex) { log?.Invoke("[season] " + ex.Message); }
            return result;
        }

        private static TrackState ReadTrack(HsMemory mem)
        {
            var t = mem.GetRewardTrack()?.TrackEntries?.OrderBy(x => x.TrackType == MainRewardTrack ? 0 : 1).FirstOrDefault();
            return t == null ? null : new TrackState { Level = t.Level, Xp = t.Xp, XpNeeded = t.XpNeeded, TotalXp = t.TotalXp, BonusPercent = t.XpBonusPercent };
        }
    }
}
