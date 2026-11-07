using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HearthStoner.Tests
{
    /// <summary>
    /// Where the tests get their data. tests/data (if it exists) holds copies of %LocalAppData%\HearthStoner
    /// (appdata/HearthStoner: cards.json, meta/) and of Hearthstone's Logs folder (install/Logs/Hearthstone_*).
    /// Without it, on Windows, the real data folder and Hearthstone install are used.
    /// </summary>
    public static class TestData
    {
        /// <summary>
        /// Trait value for tests that replay your own Hearthstone logs and saved games. Their inputs grow as you play;
        /// every other test uses fixed inputs (plus the card database). Run just one group with
        /// <c>dotnet test --filter Category=MyGames</c> or <c>--filter Category!=MyGames</c>.
        /// </summary>
        public const string MyGames = "MyGames";

        public static readonly string Root = FindRoot();
        public static readonly string InstallDir;
        public static readonly CardDb Cards;

        private static readonly Lazy<MetaStats> _meta = new Lazy<MetaStats>(() => MetaStats.Load("standard", "legend", "past-7"));
        public static MetaStats Meta => _meta.Value;

        static TestData()
        {
            var data = Root == null ? null : Path.Combine(Root, "tests", "data");
            if (data != null && Directory.Exists(data))
            {
                // CardDb/MetaStats read %LocalAppData%\HearthStoner; on Linux and macOS that's $XDG_DATA_HOME.
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(data, "appdata"));
                InstallDir = Path.Combine(data, "install");
            }
            else InstallDir = HearthstoneLocator.FindInstallDir(null);
            Cards = CardDb.Load();
        }

        private static string FindRoot()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "HearthStoner.slnx"))) return d.FullName;
            return null;
        }
    }
}
