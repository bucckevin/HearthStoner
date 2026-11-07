using System;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HearthStoner
{
    /// <summary>
    /// Two checks that matter after a Hearthstone patch: whether the memory-reading library (UnitySpy, maintained by
    /// the Firestone team) has a newer version than the copy built into HearthStoner, and whether the Hearthstone client
    /// build changed since HearthStoner last ran (memory reading is what usually breaks).
    /// </summary>
    public static class UpdateCheck
    {
        /// <summary>The UnitySpy commit vendored in vendor/ (see vendor/UNITYSPY_COMMIT.txt).</summary>
        public const string VendoredUnitySpyCommit = "11acf7869f1a9a0cbf5496e7ef12c8fe18243de6";
        private const string CommitsUrl = "https://api.github.com/repos/Zero-to-Heroes/unity-spy-.net4.5/commits?per_page=1";

        private sealed class State
        {
            public string LastBuild { get; set; }
            public DateTime LastUnitySpyCheck { get; set; }
            public string LatestUnitySpyCommit { get; set; }
        }

        private static string StatePath =>
            AppPaths.Data("state.json");

        private static State Load()
        {
            try { if (File.Exists(StatePath)) return JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath)) ?? new State(); }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "UpdateCheck"); }
            return new State();
        }

        private static void Save(State s)
        {
            try { File.WriteAllText(StatePath, JsonConvert.SerializeObject(s, Formatting.Indented)); } catch (Exception swallowed) { Diag.Ignored(swallowed, "UpdateCheck"); }
        }

        /// <summary>Once a day: is there a newer UnitySpy upstream? Returns a message, or null.</summary>
        public static string CheckUnitySpy()
        {
            var st = Load();
            if (DateTime.UtcNow - st.LastUnitySpyCheck > TimeSpan.FromDays(1))
            {
                try
                {
                    var json = Web.GetString(CommitsUrl, 20);
                    var first = JArray.Parse(json)[0];
                    st.LatestUnitySpyCommit = (string)first["sha"];
                    st.LastUnitySpyCheck = DateTime.UtcNow;
                    Save(st);
                }
                catch { return null; }
            }
            if (string.IsNullOrEmpty(st.LatestUnitySpyCommit) || st.LatestUnitySpyCommit == VendoredUnitySpyCommit) return null;
            return "A newer UnitySpy (memory reading) is available from the Firestone team: " +
                   "https://github.com/Zero-to-Heroes/unity-spy-.net4.5 . If collection/deck reading broke after a patch, " +
                   "copy its UnitySpy and UnitySpy.HearthstoneLib folders into vendor/ and rebuild.";
        }

        /// <summary>Call with the client build from Power.log. Returns a message the first time a new build is seen.</summary>
        public static string CheckHearthstoneBuild(string build)
        {
            if (string.IsNullOrEmpty(build)) return null;
            var st = Load();
            var previous = st.LastBuild;
            if (previous == build) return null;
            st.LastBuild = build;
            Save(st);
            return previous == null ? null :
                $"Hearthstone was updated (build {previous} -> {build}). If anything read from memory looks wrong, run 'HearthStoner status' " +
                "and check for a UnitySpy update; the meta stats also reset after balance patches.";
        }
    }
}
