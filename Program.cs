using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HackF5.UnitySpy.HearthstoneLib;
using HackF5.UnitySpy.HearthstoneLib.Detail.Deck;
using Newtonsoft.Json;

namespace HearthStoner
{
    /// <summary>The command line: parses options and runs a command. The commands live in Cli/.</summary>
    internal static partial class Program
    {
        private const string Usage = @"HearthStoner - read your Hearthstone cards and games (no Deck Tracker needed)

Usage:
  HearthStoner install [--no-start]      Install for your Windows user (Start menu, desktop, installed apps)
  HearthStoner uninstall [--yes]         Remove the program; your games and settings are kept
  HearthStoner setup                     Turn on Hearthstone's Power logging (restart Hearthstone after)
  HearthStoner collection [--csv F] [--json F]
                                         Every card you own, read from game memory
  HearthStoner decks [--json F]          Your saved decks, with importable deck codes
  HearthStoner status                    Account, current screen, and match info if in a game
  HearthStoner app [--minimized]         The full app: tray icon, overlay, and a window with deck picker,
                                         deck tuning, crafting, stats, game history and meta (no console)
  HearthStoner overlay                   In-game overlay: opponent's likely deck and cards, secrets,
                                         mulligan advice, threats/answers, lethal checks, draw odds
  HearthStoner watch [--tips]            Live play-by-play in the console (--tips: advice every turn)
  HearthStoner meta [--class C]          Current Standard meta: archetypes by popularity and win rate
  HearthStoner stats                     Your win rates by deck and matchup vs the meta, mulligan habits,
                                         tilt check, games to your next rank, recurring mistakes
  HearthStoner review [N]                Review of your last N games (default 1)
  HearthStoner import-logs               Keep the logs of past games for replays (and record games played without HearthStoner)
  HearthStoner launch [--dry-run]        Start Hearthstone through Battle.net (--dry-run: just show how)
  HearthStoner screenshot [folder]       Draw every page of the app window to PNGs (default: .\screenshots)
  HearthStoner gameart [outDir]          Pull the replay board's art and render the game boards from your Hearthstone
                                         install (default: %LocalAppData%\HearthStoner\gameart)
  HearthStoner preview [out.png] [--log Power.log] [--turn N]
                                         Draw the overlay as it looked at turn N of a saved game (default:
                                         your latest game, turn 12) to a PNG
  HearthStoner selftest                  Check HearthStoner against your own game logs and known card answers
  HearthStoner puzzles                   Lethal puzzles: turns where you had lethal, to solve again
  HearthStoner quiz                      Mulligan quiz from your own opening hands, checked against the data
  HearthStoner quests                    Your quests and progress (from memory)
  HearthStoner replay <Power.log> [--deck CODE] [--me NAME]
                                         Run a saved Power.log through the advisor (testing)
  HearthStoner find <text>               Search Hearthstone's class names (for fixing things after a patch)
  HearthStoner explore <Class[.field...]>  Show a class's fields, or follow a path like
                                         CollectionManager.s_instance.m_decks
  HearthStoner --version                 Which version this is, when it was built, and its git commit

Options:
  --hs PATH      Hearthstone install folder (default: from registry)
  --verbose      Show UnitySpy's internal log
  --rank R       Meta stats rank bracket: all (default), bronze-gold, platinum, diamond, legend,
                 legend-diamond, top-2000-legend
  --period P     Meta stats period: past-7 (default), past-3, past-20, last-patch, current-season
";


        private static bool _verbose;


        private static int Main(string[] argv)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var args = argv.ToList();
            var hsPath = TakeOption(args, "--hs");
            var csvOut = TakeOption(args, "--csv");
            var jsonOut = TakeOption(args, "--json");
            _verbose = TakeFlag(args, "--verbose");
            var tips = TakeFlag(args, "--tips");
            var rank = TakeOption(args, "--rank");
            var period = TakeOption(args, "--period");
            var heroClass = TakeOption(args, "--class");
            var deckCode = TakeOption(args, "--deck");
            var me = TakeOption(args, "--me");
            var minimized = TakeFlag(args, "--minimized");
            var dryRun = TakeFlag(args, "--dry-run");
            var logPath = TakeOption(args, "--log");
            var turnOpt = TakeOption(args, "--turn");
            var noStart = TakeFlag(args, "--no-start");
            var yes = TakeFlag(args, "--yes");
            Diag.SetSink(msg => Log("[ignored] " + msg));   // command line: caught errors show with --verbose

            // Double-clicked from outside the install folder (e.g. Downloads): offer to install.
            if (args.Count == 0 && !Installer.IsInstalledCopy && Installer.OwnConsole)
            {
                ShowWindow(GetConsoleWindow(), 0);   // SW_HIDE
                return Installer.Offer();
            }
            if (args.Count > 0 && args[0] is "-v" or "--version" or "version")
            {
                Console.WriteLine("HearthStoner " + AppVersion.Current.Detail);
                return 0;
            }
            if (args.Count == 0 || args[0] is "-h" or "--help" or "help")
            {
                Console.WriteLine(Usage);
                return 0;
            }

            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "install": return Installer.Install(noStart);
                    case "uninstall": return Installer.Uninstall(yes);
                    case "setup": return Setup(hsPath);
                    case "collection": return Collection(csvOut, jsonOut);
                    case "decks": return Decks(jsonOut);
                    case "status": return Status();
                    case "watch": return Watch(hsPath, overlay: false, tips: tips, rank, period);
                    case "overlay": return Watch(hsPath, overlay: true, tips: tips, rank, period);
                    case "app": return Watch(hsPath, overlay: true, tips: false, rank, period, app: true, minimized: minimized);
                    case "meta": return MetaCommand(heroClass, rank, period);
                    case "stats": return StatsCommand(rank, period);
                    case "puzzles": return PuzzlesCommand();
                    case "import-logs":
                    {
                        var st = Settings.Load();
                        var cdb = CardDb.Load();
                        var m = LoadMeta(rank ?? Ranks.MetaBracket(st), period ?? st.MetaPeriod);
                        var r = GameLog.Import(HearthstoneLocator.FindInstallDir(hsPath), cdb, m, Log);
                        Console.WriteLine($"Games: {r.Attached} logs attached to existing records, {r.Created} new records from older logs, {r.Skipped} already done or unfinished, {r.Backfilled} games given per-turn deck guesses.");
                        return 0;
                    }
                    case "launch":
                    {
                        var st = Settings.Load();
                        var target = GameLauncher.Resolve(st.LaunchPath);
                        Console.WriteLine(target.Path == null ? "Battle.net not found; would open battlenet://WTCG." : $"{target.Path} {target.Arguments}   (found in {target.Source})");
                        if (GameLauncher.IsRunning) { Console.WriteLine("Hearthstone is already running."); return 0; }
                        if (dryRun) return 0;
                        var err = GameLauncher.Launch(st.LaunchPath);
                        Console.WriteLine(err ?? "Starting Hearthstone.");
                        return err == null ? 0 : 1;
                    }
                    case "screenshot":
                    {
                        var st = Settings.Load();
                        var cdb = CardDb.Load();
                        var m = LoadMeta(rank ?? Ranks.MetaBracket(st), period ?? st.MetaPeriod);
                        var install = HearthstoneLocator.FindInstallDir(hsPath);
                        var imp = GameLog.Import(install, cdb, m, Log);
                        Console.WriteLine($"Game logs: {imp.Attached} attached, {imp.Created} games added, {imp.Skipped} already done.");
                        var svc = new AppServices
                        {
                            InstallDir = install, Cards = cdb, Meta = () => m, Settings = st, Log = Log,
                            PlayerName = st.LastBattleTag,
                        };
                        return MainWindow.Screenshots(svc, args.Count > 1 ? args[1] : "screenshots");
                    }
                    case "preview":
                        return Preview.Run(args.Count > 1 ? args[1] : "preview.png", logPath,
                            int.TryParse(turnOpt, out var pt) ? pt : 12, rank, period, Console.WriteLine);
                    case "selftest":
                    {
                        var st = Settings.Load();
                        var cdb = CardDb.Load();
                        var m = LoadMeta(rank ?? Ranks.MetaBracket(st), period ?? st.MetaPeriod);
                        return SelfTest.Run(cdb, HearthstoneLocator.FindInstallDir(hsPath), m, Console.WriteLine) == 0 ? 0 : 1;
                    }
                    case "quiz": return QuizCommand();
                    case "quests": return QuestsCommand();
                    case "review": return ReviewCommand(args.Count > 1 && int.TryParse(args[1], out var nGames) ? nGames : 1);
                    case "replay":
                        if (args.Count < 2) { Console.Error.WriteLine("Usage: HearthStoner replay <Power.log> [--deck CODE] [--me NAME]"); return 2; }
                        return Replay(args[1], deckCode, me, rank, period);
                    case "gameart": return GameArtCommand(hsPath, args.Count > 1 ? args[1] : null);
                    case "find":
                        if (args.Count < 2) { Console.Error.WriteLine("Usage: HearthStoner find <text>"); return 2; }
                        return Find(args[1]);
                    case "explore":
                        if (args.Count < 2) { Console.Error.WriteLine("Usage: HearthStoner explore <Class[.field...]>"); return 2; }
                        return Explore(args[1]);
                    default:
                        Console.Error.WriteLine($"Unknown command '{args[0]}'.\n");
                        Console.WriteLine(Usage);
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Error: " + ex.Message);
                if (_verbose) Console.Error.WriteLine(ex);
                Console.ResetColor();
                return 1;
            }
        }


        private static string TakeOption(List<string> args, string name)
        {
            var i = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (i < 0 || i + 1 >= args.Count) return null;
            var v = args[i + 1];
            args.RemoveRange(i, 2);
            return v;
        }


        private static bool TakeFlag(List<string> args, string name)
        {
            var i = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            args.RemoveAt(i);
            return true;
        }
    }
}
