#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HackF5.UnitySpy;
using HackF5.UnitySpy.HearthstoneLib;
using HackF5.UnitySpy.HearthstoneLib.Detail;
using HackF5.UnitySpy.HearthstoneLib.Detail.AccountInfo;
using HackF5.UnitySpy.HearthstoneLib.Detail.Collection;
using HackF5.UnitySpy.HearthstoneLib.Detail.Deck;
using HackF5.UnitySpy.HearthstoneLib.Detail.Match;
using HackF5.UnitySpy.HearthstoneLib.Detail.SceneMode;

namespace HearthStoner
{
    /// <summary>
    /// Read-only access to the running Hearthstone client's memory, via the vendored UnitySpy library.
    ///
    /// How it works (all inside vendor/UnitySpy): Hearthstone is a Unity game running on the Mono runtime.
    /// UnitySpy opens the process for reading, finds mono's root domain through the exported
    /// mono_get_root_domain function, walks to the Assembly-CSharp image, and then reads classes, static
    /// fields and object fields using Mono's own metadata. Nothing is injected and nothing is written.
    ///
    /// The paths below (e.g. CollectionManager.s_instance.m_decks) are Hearthstone's own class/field names.
    /// When a patch renames one, use `HearthStoner explore` to find the new name.
    /// </summary>
    public sealed class HsMemory : IDisposable
    {
        private readonly IAssemblyImage _raw;
        private readonly HearthstoneImage _image;

        public int ProcessId { get; }

        private HsMemory(int pid, IAssemblyImage raw)
        {
            ProcessId = pid;
            _raw = raw;
            _image = new HearthstoneImage(raw);
        }

        public static bool IsHearthstoneRunning => Process.GetProcessesByName("Hearthstone").Length > 0;

        public static HsMemory Attach(bool verbose = false)
        {
            Logger.LogHandler = verbose ? (Action<string>)(m => Console.Error.WriteLine("[unityspy] " + m)) : (_ => { });

            var process = Process.GetProcessesByName("Hearthstone").FirstOrDefault()
                          ?? throw new InvalidOperationException("Hearthstone isn't running. Start it and wait for the main menu, then try again.");

            CollectionCardReader.InvalidateCardIdCache();
            ActiveDeckReader.InvalidateDeckCardDbfCache();

            IAssemblyImage raw;
            try
            {
                raw = AssemblyImageFactory.Create(process.Id, Logger.Log);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Couldn't attach to Hearthstone's Mono runtime (" + ex.Message + "). If Hearthstone was just patched, " +
                    "the vendored UnitySpy may need updating from https://github.com/Zero-to-Heroes/unity-spy-.net4.5 . " +
                    "Run with --verbose for details.", ex);
            }
            return new HsMemory(process.Id, raw);
        }

        public void Dispose() => _image.Dispose();

        // ------------------------------------------------------------------ high-level reads

        public bool IsCollectionReady() => CollectionCardReader.IsCollectionInit(_image);

        /// <summary>How many cards the collection holds: a cheap read to tell when it changed.</summary>
        public int CollectionSize() => CollectionCardReader.ReadCollectionSize(_image);

        public IReadOnlyList<ICollectionCard> GetCollection() => CollectionCardReader.ReadCollection(_image);

        public IMatchInfo GetMatchInfo() => MatchInfoReader.ReadMatchInfo(_image);

        public AccountInfo GetAccountInfo() => AccountInfoReader.ReadAccountInfo(_image);

        public SceneModeEnum? GetSceneMode() => SceneModeReader.ReadSceneMode(_image);

        public IDeck GetActiveDeck() => ActiveDeckReader.ReadActiveDeck(_image, null);

        public HackF5.UnitySpy.HearthstoneLib.Detail.Quests.QuestsLog GetQuests() =>
            HackF5.UnitySpy.HearthstoneLib.Detail.Quests.QuestsReader.ReadQuests(_image);

        /// <summary>Your reward tracks (level and XP); the main one has TrackType 1.</summary>
        public HackF5.UnitySpy.HearthstoneLib.Detail.RewardTrack.RewardTrackInfos GetRewardTrack() =>
            HackF5.UnitySpy.HearthstoneLib.Detail.RewardTrack.RewardTrackInfoReader.ReadRewardTrack(_image);

        /// <summary>When the ranked season ends (UTC): NetCacheRewardProgress.SeasonEndDate, a Windows file time.</summary>
        public DateTime? SeasonEnd()
        {
            long ft = _image.GetNetCacheService("NetCacheRewardProgress")?["<SeasonEndDate>k__BackingField"] ?? 0L;
            return ft > 0 ? DateTime.FromFileTimeUtc(ft) : (DateTime?)null;
        }

        /// <summary>Fields of one record of a game database table (e.g. "Quest"), found by its m_ID.</summary>
        public List<string> DbfRecord(string table, int id)
        {
            if (_image["GameDbf"]?[table]?["m_records"] == null) return new List<string> { $"No GameDbf.{table} table found." };
            var r = FindDbf(table, id);
            return r == null ? new List<string> { $"GameDbf.{table} has no record {id}." } : DescribeObject($"GameDbf.{table} #{id}", (object)r).ToList();
        }

        /// <summary>
        /// A quest's definition from GameDbf.Quest (goal text, quota, reward-track XP, icon) and its pool from
        /// GameDbf.QuestPool (pool type 1 daily, 2 weekly; the weekday it hands out new quests). Null if it isn't there.
        /// </summary>
        public QuestEntry? QuestDef(int id)
        {
            var r = FindDbf("Quest", id);
            if (r == null) return null;
            int quota = r["m_quota"];
            var q = new QuestEntry
            {
                Id = id, Name = Loc(r["m_name"]), Quota = quota, Xp = (int)r["m_rewardTrackXp"],
                Icon = r["m_icon"] as string, RewardTrack = (int)r["m_rewardTrackType"],
            };
            q.Text = QuestEntry.Fill(Loc(r["m_description"]), quota);
            int poolId = r["m_questPoolId"];
            var p = poolId == 0 ? null : FindDbf("QuestPool", poolId);
            int type = p == null ? 0 : (int)p["m_questPoolType"];
            q.Pool = type == 1 ? "Daily" : type == 2 ? "Weekly" : "Event";
            q.ResetDay = p == null ? -1 : (int)p["m_grantDayOfWeek"];
            return q;
        }

        /// <summary>A localized DBF string (DbfLocValue) in the game's language.</summary>
        private static string? Loc(dynamic v)
        {
            try { return v is string s ? s : (string?)v?["m_currentLocaleValue"]; }
            catch { return null; }
        }

        private dynamic? FindDbf(string table, int id)
        {
            var recs = _image["GameDbf"]?[table]?["m_records"];
            if (recs == null) return null;
            int size = recs["_size"];
            var items = recs["_items"];
            for (var i = 0; i < size; i++)
            {
                var r = items[i];
                if (r == null) continue;
                int rid;
                try { rid = (int)r["m_ID"]; } catch { continue; }
                if (rid == id) return r;
            }
            return null;
        }

        /// <summary>
        /// Every deck in "My Collection". CollectionManager.m_decks is Hearthstone's own hash map type,
        /// stored as parallel keySlots (deck ids) / valueSlots (CollectionDeck objects) arrays.
        /// </summary>
        public IReadOnlyList<Deck> GetAllDecks()
        {
            var result = new List<Deck>();
            var decks = _image["CollectionManager"]?["s_instance"]?["m_decks"];
            if (decks == null) return result;

            var keys = decks["keySlots"];
            var values = decks["valueSlots"];
            if (keys is null || values is null) return result;

            int n = Math.Min((int)keys.Length, (int)values.Length);
            for (var i = 0; i < n; i++)
            {
                long id = Convert.ToInt64(keys[i]);
                var deck = values[i];
                if (id == 0 || deck == null) continue;   // empty slot
                try
                {
                    Deck d = ActiveDeckReader.GetDynamicDeck(deck);
                    if (d != null) result.Add(d);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Couldn't read deck {id}: {ex.Message}");
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ exploration (for fixing paths after patches)

        /// <summary>Type names in Assembly-CSharp containing the text.</summary>
        public IEnumerable<string> FindTypes(string contains) =>
            _raw.TypeDefinitions
                .Select(SafeName)
                .OfType<string>()
                .Where(n => n.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct()
                .OrderBy(n => n);

        /// <summary>
        /// Explores a dotted path such as "CollectionManager", "CollectionManager.s_instance" or
        /// "CollectionManager.s_instance.m_decks". Returns one line per field with its current value.
        /// </summary>
        public IEnumerable<string> Explore(string path)
        {
            var parts = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) yield break;

            // The class name may itself contain dots (namespaces), so try the longest matching prefix.
            ITypeDefinition? type = null;
            var consumed = 0;
            for (var take = parts.Length; take >= 1 && type == null; take--)
            {
                var name = string.Join(".", parts.Take(take));
                type = SafeGetType(name);
                if (type != null) consumed = take;
            }
            if (type == null)
            {
                yield return $"No class named '{parts[0]}'. Try: HearthStoner find {parts[0]}";
                yield break;
            }

            if (consumed == parts.Length)
            {
                yield return $"class {type.FullName}";
                foreach (var f in type.Fields)
                {
                    var isStatic = SafeIsStatic(f);
                    var val = isStatic ? Describe(SafeStatic(type, f.Name)) : "";
                    yield return $"  {(isStatic ? "static " : "       ")}{TypeLabel(f),-14} {f.Name}{(isStatic ? " = " + val : "")}";
                }
                yield break;
            }

            // Walk: first hop is a static field on the class, the rest are instance fields.
            object? current = null;
            string? readError = null;
            try { current = type[parts[consumed]]; }
            catch (Exception ex) { readError = $"Couldn't read {type.FullName}.{parts[consumed]}: {ex.Message}"; }
            if (readError != null) { yield return readError; yield break; }

            for (var i = consumed + 1; i < parts.Length && current != null; i++)
            {
                current = Step(current, parts[i], out var err);
                if (err != null) { yield return err; yield break; }
            }

            foreach (var line in DescribeObject(path, current)) yield return line;
        }

        private static object? Step(object current, string part, out string? error)
        {
            error = null;
            if (int.TryParse(part, out var index))
            {
                if (current is IList list)
                    return index < list.Count ? list[index] : null;
                error = $"'{part}' is an index, but the value isn't an array.";
                return null;
            }
            if (current is IManagedObjectInstance obj)
            {
                try { return obj.GetValue<object>(part); }
                catch (Exception ex) { error = $"Couldn't read field '{part}' on {obj.TypeDefinition?.FullName}: {ex.Message}"; return null; }
            }
            error = $"Can't read '{part}' from a {current?.GetType().Name ?? "null"} value.";
            return null;
        }

        private static IEnumerable<string> DescribeObject(string path, object? value)
        {
            if (value is IManagedObjectInstance obj)
            {
                yield return $"{path} : {obj.TypeDefinition?.FullName}";
                var fields = obj.TypeDefinition?.Fields ?? (IReadOnlyList<IFieldDefinition>)new IFieldDefinition[0];
                foreach (var f in fields.Where(f => !SafeIsStatic(f)))
                {
                    string v;
                    try { v = Describe(obj.GetValue<object>(f.Name)); }
                    catch (Exception ex) { v = "<" + ex.GetType().Name + ">"; }
                    yield return $"  {TypeLabel(f),-14} {f.Name} = {v}";
                }
            }
            else if (value is IList list && !(value is string))
            {
                yield return $"{path} : array[{list.Count}]";
                for (var i = 0; i < Math.Min(list.Count, 50); i++) yield return $"  [{i}] {Describe(list[i])}";
                if (list.Count > 50) yield return $"  ... {list.Count - 50} more";
            }
            else
            {
                yield return $"{path} = {Describe(value)}";
            }
        }

        private static string Describe(object? v)
        {
            switch (v)
            {
                case null: return "null";
                case string s: return "\"" + (s.Length > 80 ? s.Substring(0, 80) + "..." : s) + "\"";
                case IManagedObjectInstance o: return "{" + (o.TypeDefinition?.FullName ?? "object") + "}";
                case IList l: return $"array[{l.Count}]";
                default: return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
        }

        private static string TypeLabel(IFieldDefinition f)
        {
            try
            {
                if (f.TypeInfo.TryGetTypeDefinition(out var td) && td != null) return td.Name;
                return f.TypeInfo.TypeCode.ToString();
            }
            catch { return "?"; }
        }

        private static bool SafeIsStatic(IFieldDefinition f) { try { return f.TypeInfo.IsStatic; } catch { return false; } }
        private static object? SafeStatic(ITypeDefinition t, string field) { try { return t[field]; } catch (Exception ex) { return "<" + ex.GetType().Name + ">"; } }
        private ITypeDefinition? SafeGetType(string name) { try { return _raw.GetTypeDefinition(name); } catch { return null; } }
        private static string? SafeName(ITypeDefinition t) { try { return t.FullName; } catch { return null; } }
    }
}
