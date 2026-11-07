using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HearthStoner.Pages
{
    /// <summary>
    /// The board art pulled from your own Hearthstone install by Art/GameArtBuilder, in
    /// %LocalAppData%\HearthStoner\gameart\board. Each piece is optional: the replay board draws its own when one is missing.
    /// </summary>
    public static class GameArt
    {
        private static readonly Dictionary<string, BitmapSource> Cache = new Dictionary<string, BitmapSource>();

        public static string Dir => AppPaths.Data("gameart", "board");
        /// <summary>Whole boards rendered from the game's 3D models by Art/GameArtBuilder: &lt;root&gt;.jpg, 4:3.</summary>
        public static string BoardsDir => AppPaths.Data("gameart", "boards");

        public static BitmapSource Get(string name) => Load(System.IO.Path.Combine(Dir, name + ".png"), 0);

        /// <summary>Makes the art in the background if it's missing or outdated (<paramref name="force"/>: always); see ArtJob.</summary>
        public static void StartJob(string installDir, Func<bool> inGame = null, bool force = false)
        {
            lock (Cache)
            {
                if (!_jobConfigured)
                {
                    // The builder's progress notes go to the diagnostics log (hearthstoner.log in the app).
                    GameArtBuilder.Log ??= note => Console.Error.WriteLine("[gameart] " + note);
                    ArtJob.Finished += () => { lock (Cache) Cache.Clear(); };   // new art replaces the loaded images
                    _jobConfigured = true;
                }
                ArtJob.Configure(new InstallArtBuilder(), inGame);
            }
            ArtJob.Start(installDir, force);
        }

        private static bool _jobConfigured;

        /// <summary>The real builder: reads the art out of the Hearthstone install (<see cref="GameArtBuilder"/>).</summary>
        private sealed class InstallArtBuilder : IGameArtBuilder
        {
            public string GameVersion(string installDir) => GameArtBuilder.GameVersion(installDir);

            public void Build(string installDir, string outDir, Action<string, int, int> progress, System.Threading.CancellationToken cancel) =>
                GameArtBuilder.Run(installDir, outDir, progress, cancel);
        }

        /// <summary>A rendered board (null if it hasn't been rendered); a small copy when <paramref name="width"/> is set.</summary>
        public static BitmapSource Board(string root, int width = 0) =>
            string.IsNullOrEmpty(root) ? null : Load(System.IO.Path.Combine(BoardsDir, root + ".jpg"), width);

        /// <summary>The boards that have been rendered, by root name.</summary>
        public static List<string> Boards() =>
            System.IO.Directory.Exists(BoardsDir)
                ? System.IO.Directory.GetFiles(BoardsDir, "*.jpg").Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).OrderBy(BoardName).ToList()
                : new List<string>();

        /// <summary>A readable name for a board's root (the set it came with), else the root itself.</summary>
        public static string BoardName(string root) => Names.TryGetValue(root ?? "", out var n) ? n : root;
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STW"] = "Stormwind", ["STWOG"] = "Stormwind (Old Gods)", ["STW2"] = "Stormwind 2", ["ORG"] = "Orgrimmar", ["MOP"] = "Pandaria",
            ["STR"] = "Stranglethorn", ["ICC"] = "Icecrown", ["BAR"] = "Barrens", ["BRM"] = "Blackrock Mountain", ["GVG"] = "Goblins vs Gnomes",
            ["NAX"] = "Naxxramas", ["TGT"] = "Grand Tournament", ["LOE1"] = "League of Explorers", ["LOE2"] = "League of Explorers 2",
            ["KAR"] = "Karazhan", ["MSG"] = "Gadgetzan", ["UNG"] = "Un'Goro", ["LOOT"] = "Kobolds & Catacombs", ["GIL"] = "Gilneas",
            ["BOT"] = "Boomsday", ["TRL"] = "Rastakhan's Rumble", ["DAL"] = "Dalaran", ["ULD"] = "Uldum", ["ULD2"] = "Uldum 2", ["ULD3"] = "Uldum 3",
            ["DRG"] = "Dragons", ["BT"] = "Outland", ["SCH"] = "Scholomance", ["DMF"] = "Darkmoon Faire", ["BOH"] = "Book of Heroes",
            ["TTN"] = "Titans", ["MOTLK"] = "Lich King", ["VSC"] = "Sunken City", ["BotB"] = "Festival of Legends", ["WW"] = "Badlands",
            ["TOY"] = "Whizbang's Workshop", ["CATA"] = "Cataclysm", ["EDR"] = "Emerald Dream", ["TIME"] = "Timeways", ["TLC"] = "Lost City",
            ["ALT"] = "Alterac Valley", ["RAG"] = "Firelands", ["REV"] = "Revendreth",
        };

        private static BitmapSource Load(string file, int width)
        {
            var key = file + "|" + width;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var b)) return b;
                BitmapSource img = null;
                try
                {
                    if (System.IO.File.Exists(file))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(file);
                        if (width > 0) bi.DecodePixelWidth = width;
                        bi.EndInit();
                        bi.Freeze();
                        img = bi;
                    }
                }
                catch (Exception swallowed) { Diag.Ignored(swallowed, "ReplayBoard"); }
                Cache[key] = img;
                return img;
            }
        }
    }
}
