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
    internal static class Program
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

            // Double-clicked from outside the install folder (e.g. Downloads): offer to install.
            if (args.Count == 0 && !Installer.IsInstalledCopy && Installer.OwnConsole)
            {
                ShowWindow(GetConsoleWindow(), 0);   // SW_HIDE
                return Installer.Offer();
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
                        var m = LoadMeta(rank ?? MetaRankSetting(st), period ?? st.MetaPeriod);
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
                        var m = LoadMeta(rank ?? MetaRankSetting(st), period ?? st.MetaPeriod);
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
                        var m = LoadMeta(rank ?? MetaRankSetting(st), period ?? st.MetaPeriod);
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

        // ------------------------------------------------------------------ commands

        private static int GameArtCommand(string hsPath, string outDir)
        {
            var hs = HearthstoneLocator.FindInstallDir(hsPath);
            outDir ??= AppPaths.Data("gameart");
            Console.WriteLine($"Game {GameArtBuilder.GameVersion(hs)} in {hs} -> {outDir}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string last = null;
            GameArtBuilder.Log = s => { Console.WriteLine("\r" + s.PadRight(40)); last = null; };
            GameArtBuilder.Run(hs, outDir, (stage, done, total) =>
            {
                var line = $"{stage} {done}/{total}";
                if (line != last) { Console.Write("\r" + line.PadRight(40)); last = line; }
                if (done == total) Console.WriteLine();
            }, CancellationToken.None);
            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:0}s.");
            return 0;
        }

        private static int Setup(string hsPath)
        {
            var changed = HearthstoneLocator.EnsureLogConfig();
            Console.WriteLine(changed
                ? $"Updated {HearthstoneLocator.LogConfigPath}. Restart Hearthstone for logging to take effect."
                : $"Logging already enabled in {HearthstoneLocator.LogConfigPath}.");

            var install = HearthstoneLocator.FindInstallDir(hsPath);
            Console.WriteLine(install != null
                ? $"Hearthstone install: {install}"
                : "Couldn't find the Hearthstone install folder. Pass --hs \"C:\\path\\to\\Hearthstone\" to 'watch'.");
            if (install != null)
                Console.WriteLine(SafeEnsureClientConfig(install)
                    ? $"Removed the 10 MB log limit in {HearthstoneLocator.ClientConfigPath(install)}."
                    : $"Log size limit already off ({HearthstoneLocator.ClientConfigPath(install)}).");
            Console.WriteLine($"Hearthstone running: {(HsMemory.IsHearthstoneRunning ? "yes" : "no")}");
            WarnIfRestartNeeded(install);
            return 0;
        }

        /// <summary>
        /// Hearthstone only reads log.config and client.config at startup. If either is newer than the running game,
        /// the logs won't be complete until Hearthstone is restarted.
        /// </summary>
        private static bool WarnIfRestartNeeded(string install)
        {
            try
            {
                var proc = System.Diagnostics.Process.GetProcessesByName("Hearthstone").FirstOrDefault();
                if (proc == null) return false;
                var started = proc.StartTime;
                var newer = new[] { HearthstoneLocator.LogConfigPath, HearthstoneLocator.ClientConfigPath(install) }
                    .Where(f => f != null && File.Exists(f) && File.GetLastWriteTime(f) > started)
                    .ToList();
                if (newer.Count == 0) return false;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Hearthstone was started at {started:HH:mm}, before {string.Join(" and ", newer.Select(Path.GetFileName))} changed.");
                Console.WriteLine("Restart Hearthstone, or tracking will be missing or stop early this session.");
                Console.ResetColor();
                return true;
            }
            catch { return false; }   // e.g. access denied reading another process's start time
        }

        private static int Collection(string csvOut, string jsonOut)
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var cards = CardDb.Load();
                if (!WaitFor(() => mem.IsCollectionReady(), "your collection to load (go to the main menu)")) return 1;

                var raw = Retry(() => mem.GetCollection(), r => r != null && r.Count > 0, "collection");
                if (raw == null) return 1;
                OwnedCollection.FromMemory(raw).Save();   // for crafting advice when the game isn't running

                var rows = new List<CollectionRow>();
                foreach (var c in raw)
                {
                    var info = cards.ById(c.CardId);
                    void Add(string finish, int count)
                    {
                        if (count <= 0) return;
                        rows.Add(new CollectionRow
                        {
                            CardId = c.CardId,
                            Name = info?.Name ?? c.CardId,
                            Class = info?.CardClass ?? "",
                            Set = info?.Set ?? "",
                            Rarity = info?.Rarity ?? "",
                            Cost = info?.Cost,
                            Finish = finish,
                            Count = count,
                        });
                    }
                    Add("normal", c.Count);
                    Add("golden", c.PremiumCount);
                    Add("diamond", c.DiamondCount);
                    Add("signature", c.SignatureCount);
                }

                rows = rows.OrderBy(r => ClassOrder(r.Class)).ThenBy(r => r.Cost ?? 99).ThenBy(r => r.Name).ThenBy(r => r.Finish).ToList();

                foreach (var cls in rows.GroupBy(r => r.Class))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n== {Pretty(cls.Key)} ({cls.Sum(r => r.Count)} cards) ==");
                    Console.ResetColor();
                    foreach (var r in cls)
                        Console.WriteLine($"  {r.Count}x  [{(r.Cost?.ToString() ?? "-"),2}] {r.Name}{(r.Finish == "normal" ? "" : $" ({r.Finish})")}  - {Pretty(r.Rarity)}, {r.Set}");
                }

                Console.WriteLine($"\nTotal: {rows.Sum(r => r.Count)} cards, {rows.Select(r => r.CardId).Distinct().Count()} unique.");

                if (csvOut != null)
                {
                    var sb = new StringBuilder("card_id,name,class,set,rarity,cost,finish,count\n");
                    foreach (var r in rows)
                        sb.AppendLine(string.Join(",", Csv(r.CardId), Csv(r.Name), r.Class, r.Set, r.Rarity, r.Cost?.ToString() ?? "", r.Finish, r.Count));
                    File.WriteAllText(csvOut, sb.ToString());
                    Console.WriteLine($"Wrote {csvOut}");
                }
                if (jsonOut != null)
                {
                    File.WriteAllText(jsonOut, JsonConvert.SerializeObject(rows, Formatting.Indented));
                    Console.WriteLine($"Wrote {jsonOut}");
                }
            }
            return 0;
        }

        private static int Decks(string jsonOut)
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var cards = CardDb.Load();
                if (!WaitFor(() => mem.IsCollectionReady(), "your collection to load (go to the main menu)")) return 1;

                var decks = Retry(() => mem.GetAllDecks(), d => d != null && d.Count > 0, "decks");
                if (decks == null) return 1;

                var export = new List<object>();
                foreach (var deck in decks.OrderBy(d => d.Name))
                {
                    var hero = cards.ById(deck.HeroCardId);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n## {deck.Name}  ({Pretty(hero?.CardClass)}, {FormatName(deck.FormatType)}, {deck.DeckList.Count} cards)");
                    Console.ResetColor();

                    foreach (var g in deck.DeckList.GroupBy(id => id).Select(g => new { Info = cards.ById(g.Key), Id = g.Key, Count = g.Count() })
                                 .OrderBy(c => c.Info?.Cost ?? 99).ThenBy(c => c.Info?.Name))
                        Console.WriteLine($"  {g.Count}x [{(g.Info?.Cost?.ToString() ?? "-"),2}] {g.Info?.Name ?? g.Id}");

                    var code = DeckCode.Encode(deck, cards);
                    if (code != null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"  Deck code: {code}");
                        Console.ResetColor();
                    }
                    export.Add(new { deck.DeckId, deck.Name, Class = hero?.CardClass, Format = FormatName(deck.FormatType), deck.HeroCardId, Cards = deck.DeckList, DeckCode = code });
                }

                if (jsonOut != null)
                {
                    File.WriteAllText(jsonOut, JsonConvert.SerializeObject(export, Formatting.Indented));
                    Console.WriteLine($"\nWrote {jsonOut}");
                }
            }
            return 0;
        }

        private static int Status()
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var acct = Safe(() => mem.GetAccountInfo());
                Console.WriteLine($"Account:  {acct?.BattleTag ?? "(not available yet)"}");
                Console.WriteLine($"Screen:   {Safe(() => mem.GetSceneMode())?.ToString() ?? "?"}");
                Console.WriteLine($"Collection loaded: {(Safe(() => mem.IsCollectionReady()) ? "yes" : "no")}");

                var match = Safe(() => mem.GetMatchInfo());
                if (match?.OpposingPlayer != null && !string.IsNullOrEmpty(match.OpposingPlayer.Name))
                {
                    Console.WriteLine($"In game:  {match.GameType} ({match.FormatType})");
                    Console.WriteLine($"  You:      {DescribePlayer(match.LocalPlayer, match.FormatType)}");
                    Console.WriteLine($"  Opponent: {DescribePlayer(match.OpposingPlayer, match.FormatType)}");
                }
                else Console.WriteLine("In game:  no");
            }
            return 0;
        }

        private static int Find(string text)
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var hits = mem.FindTypes(text).ToList();
                foreach (var h in hits.Take(200)) Console.WriteLine("  " + h);
                Console.WriteLine($"{hits.Count} match(es){(hits.Count > 200 ? " (showing 200)" : "")}.");
            }
            return 0;
        }

        private static int Explore(string path)
        {
            using (var mem = HsMemory.Attach(_verbose))
                foreach (var line in mem.Explore(path)) Console.WriteLine(line);
            return 0;
        }

        private static int Watch(string hsPath, bool overlay, bool tips, string rank, string period, bool app = false, bool minimized = false)
        {
            if (app) ConsoleToLogFile();
            var install = HearthstoneLocator.FindInstallDir(hsPath)
                          ?? throw new DirectoryNotFoundException("Couldn't find Hearthstone. Pass --hs \"C:\\path\\to\\Hearthstone\".");
            if (HearthstoneLocator.EnsureLogConfig())
                Console.WriteLine("Enabled Power logging in log.config.");
            if (SafeEnsureClientConfig(install))
                Console.WriteLine("Removed Hearthstone's 10 MB log limit in client.config.");
            WarnIfRestartNeeded(install);

            var settings = Settings.Load();
            var cards = CardDb.Load();
            var meta = LoadMeta(rank ?? MetaRankSetting(settings), period ?? settings.MetaPeriod);
            var live = new LiveMemory(_verbose);
            var buildChecked = false;
            if (settings.CheckForUpdates)
                Task.Run(() => { var msg = UpdateCheck.CheckUnitySpy(); if (msg != null) Warn(msg); });

            GameTracker tracker = null;
            Advisor advisor = null;
            DeckLoader deckLoader = null;
            PowerLogTailer tailer = null;
            GameRecorder recorder = null;
            tracker = new GameTracker(cards, ev =>
            {
                // Runs on the log thread while the tracker lock is held, so reading tracker state here is safe.
                recorder?.OnEvent(ev);
                var suffix = "";
                if (ev.Kind == GameEventKind.Draw && ev.Friendly)
                    suffix = $"  ({tracker.DeckCount(tracker.FriendlyPlayerId)} left in deck)";

                Console.ForegroundColor = ev.Kind switch
                {
                    GameEventKind.GameStart or GameEventKind.GameEnd => ConsoleColor.Yellow,
                    GameEventKind.Turn => ConsoleColor.DarkGray,
                    GameEventKind.Info => ConsoleColor.Cyan,
                    GameEventKind.Secret => ConsoleColor.Magenta,
                    _ => ev.Friendly ? ConsoleColor.Green : ConsoleColor.Red,
                };
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {ev.Text}{suffix}");
                Console.ResetColor();

                if (ev.Kind == GameEventKind.GameStart)
                {
                    Task.Run(() =>
                    {
                        var match = ShowGameStartInfo(live);
                        if (match == null) return;
                        var r = RankOf(match.LocalPlayer, match.FormatType);
                        var o = RankOf(match.OpposingPlayer, match.FormatType);
                        lock (tracker.Sync)
                            recorder?.SetMatchInfo(match.GameType.ToString(), match.FormatType.ToString(), r == null ? null : RankName(r),
                                r?.StarLevel ?? 0, r?.LegendRank ?? 0, match.OpposingPlayer?.Name, r?.EarnedStars ?? 0, r?.StarsPerWin ?? 0, r?.SeasonId ?? 0,
                                o?.StarLevel ?? 0, o?.LegendRank ?? 0);
                    });
                    deckLoader?.Reset();
                }
                if (ev.Kind == GameEventKind.Turn && !buildChecked && tracker.BuildNumber != null)
                {
                    buildChecked = true;
                    var msg = UpdateCheck.CheckHearthstoneBuild(tracker.BuildNumber);
                    if (msg != null) Warn(msg);
                }
                if (ev.Kind == GameEventKind.Turn && tips && !tracker.Quiet)
                    Task.Run(() => { Thread.Sleep(300); PrintSnapshot(advisor.Compute()); });
                if (ev.Kind == GameEventKind.GameEnd)
                {
                    PrintSummary(tracker, cards);
                    var left = tracker.FriendlyRemainingDeck();
                    if (left.Count > 0)
                        Console.WriteLine($"  Never drawn:     {string.Join(", ", left.SelectMany(kv => Enumerable.Repeat(cards.NameOf(kv.Key), kv.Value)))}\n");
                    var rec = recorder?.Current;
                    if (rec != null)
                    {
                        advisor.LastGame = rec;
                        Task.Run(() =>
                        {
                            Thread.Sleep(1500);   // let the match info and result settle
                            try
                            {
                                // Keep this game's part of Power.log with its record, for replays.
                                try
                                {
                                    var logPath = tailer.CurrentPath;
                                    var slice = logPath == null ? null : GameLog.LastGame(logPath);
                                    if (slice != null && slice.Complete) GameLog.Attach(rec, slice);
                                }
                                catch (Exception ex) { Log("[record] Couldn't keep the game log: " + ex.Message); }
                                string path;
                                lock (tracker.Sync) path = rec.Save();
                                PrintSnapshot(new AdviceSnapshot { Sections = { Advisor.ReviewSection(rec) } });
                                Console.WriteLine($"  Saved to {path}\n");

                                // "auto" meta bracket: follow your rank as it changes.
                                if (rank == null && string.Equals(settings.MetaRank, "auto", StringComparison.OrdinalIgnoreCase))
                                {
                                    var bracket = MetaRankSetting(settings);
                                    if (bracket != advisor.Meta?.Rank) advisor.Meta = LoadMeta(bracket, period ?? settings.MetaPeriod);
                                }
                            }
                            catch (Exception ex) { Log("[record] Couldn't save the game: " + ex.Message); }
                        });
                    }
                }
            });
            advisor = new Advisor(tracker, cards, meta, settings);
            recorder = new GameRecorder(tracker, advisor, cards);
            tracker.Changed += recorder.OnChanged;
            advisor.LastGame = GameRecord.LoadAll().LastOrDefault();

            // Your deck list: from Decks.log (what you queued with), falling back to memory. Retried in the
            // background until it works, which also covers starting HearthStoner in the middle of a game.
            tailer = new PowerLogTailer(install, tracker);
            deckLoader = new DeckLoader(tracker, advisor, cards, live, () => tailer.CurrentPath);
            var loaderThread = new Thread(() => deckLoader.Loop()) { IsBackground = true, Name = "deck loader" };
            loaderThread.Start();

            // Your BattleTag from memory is the most reliable way to tell which player you are.
            var tag = live.Use(m => m.GetAccountInfo()?.BattleTag);
            if (!string.IsNullOrEmpty(tag))
            {
                tracker.FriendlyNameHint = tag;
                Console.WriteLine($"Logged in as {tag}");
                if (settings.LastBattleTag != tag) { settings.LastBattleTag = tag; settings.Save(); }
            }
            else if (!HsMemory.IsHearthstoneRunning) Console.WriteLine("Hearthstone isn't running yet - start it whenever you like.");

            if (app)
            {
                var services = new AppServices
                {
                    InstallDir = install,
                    Cards = cards,
                    Meta = () => advisor.Meta,
                    Settings = settings,
                    Log = Log,
                    PlayerName = tag,
                    ReloadMeta = () => advisor.Meta = LoadMeta(MetaRankSetting(settings), settings.MetaPeriod),
                    RefreshCollection = () =>
                    {
                        var col = live.Use(m => m.IsCollectionReady() ? m.GetCollection() : null);
                        if (col == null || col.Count == 0) return null;
                        var owned = OwnedCollection.FromMemory(col);
                        owned.Save();
                        return owned;
                    },
                };
                // Keep every game in Hearthstone's log folders replayable: attach logs to records, record older games.
                services.ImportLogs = () => GameLog.Import(install, cards, advisor.Meta, Log);
                AppHost.Start(() => advisor.Compute(), advisor, settings, Log, services, showWindow: !minimized);
                GameArt.StartJob(install, () => tracker.InGame);
                Task.Run(() => WatchCollection(live, tracker, services));
            }
            else if (overlay)
            {
                OverlayWindow.Start(() => advisor.Compute(), advisor, settings, Log);
                Console.WriteLine("Overlay on. It shows over Hearthstone while the game window is active (windowed or borderless fullscreen).");
                Console.WriteLine($"Hotkeys: {settings.HotkeyToggleOverlay} overlay, {settings.HotkeyToggleDeck} deck list, {settings.HotkeyToggleOpponent} opponent, {settings.HotkeyToggleAdvice} advice, {settings.HotkeyHoverMode} hover mode.");
                Console.WriteLine($"Settings: {Settings.Path}");
            }

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
            Console.WriteLine("Watching for games. Press Ctrl+C to stop.\n");
            tailer.Run(cts.Token, msg =>
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(msg);
                Console.ResetColor();
            });
            live.Dispose();
            return 0;
        }

        /// <summary>Feeds a saved Power.log through the tracker and prints the advice at every turn: for testing.</summary>
        private static int Replay(string path, string deckCode, string me, string rank, string period)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("No such log: " + path);
            var cards = CardDb.Load();
            var meta = LoadMeta(rank, period);
            List<string> deck = null;
            if (!string.IsNullOrEmpty(deckCode))
            {
                deck = DeckCode.Decode(deckCode, cards);
                if (deck == null) throw new ArgumentException("Couldn't decode that deck code.");
            }
            else
            {
                var queued = DecksLog.LastQueuedDeck(path);
                if (queued != null) deck = DeckCode.Decode(queued.DeckCode, cards);
                if (deck != null) Console.WriteLine($"Deck from Decks.log: {queued.Name} ({deck.Count} cards)");
            }

            GameTracker tracker = null;
            Advisor advisor = null;
            var games = 0;
            var detailPrepared = false;
            GameRecorder recorder = null;
            tracker = new GameTracker(cards, ev =>
            {
                Console.ForegroundColor = ev.Kind == GameEventKind.Secret ? ConsoleColor.Magenta : ev.Kind == GameEventKind.Turn ? ConsoleColor.DarkGray : ConsoleColor.Gray;
                Console.WriteLine("  " + ev.Text);
                Console.ResetColor();
                recorder?.OnEvent(ev);
                if (ev.Kind == GameEventKind.GameStart)
                {
                    games++;
                    if (deck != null) tracker.FriendlyDeckList = deck.ToList();
                }
                // The previous turn is complete when the next one starts: show the advice as it stood then.
                if (ev.Kind == GameEventKind.Turn || ev.Kind == GameEventKind.GameEnd)
                {
                    if (!detailPrepared && deck != null && tracker.FriendlyPlayerId != 0) { advisor.PrepareFriendlyDeck(Log); detailPrepared = true; }
                    PrintSnapshot(advisor.Compute());
                }
            });
            tracker.FriendlyNameHint = me;
            advisor = new Advisor(tracker, cards, meta);
            recorder = new GameRecorder(tracker, advisor, cards);
            tracker.Changed += recorder.OnChanged;
            var reviews = new List<GameRecord>();

            foreach (var line in File.ReadLines(path))
            {
                lock (tracker.Sync)
                {
                    var before = recorder.Current;
                    tracker.Feed(line);
                    if (before != null && before.End != default(DateTime) && !reviews.Contains(before)) reviews.Add(before);
                }
            }
            foreach (var r in reviews) PrintSnapshot(new AdviceSnapshot { Sections = { Advisor.ReviewSection(r) } });
            PrintSnapshot(advisor.Compute());
            Console.WriteLine($"{games} game(s) in the log.");
            return 0;
        }

        private static int StatsCommand(string rank, string period)
        {
            var settings = Settings.Load();
            var cards = CardDb.Load();
            var meta = LoadMeta(rank ?? MetaRankSetting(settings), period ?? settings.MetaPeriod);
            var games = GameRecord.LoadAll();
            Console.WriteLine($"{games.Count} saved game(s) in {GameRecord.Dir}");
            var sections = StatsReport.Build(games, meta, cards, (key, id) => meta.Detail(id, Log));
            PrintSnapshot(new AdviceSnapshot { Sections = sections });
            return 0;
        }

        private static int PuzzlesCommand()
        {
            var puzzles = GameRecord.LoadAll().SelectMany(g => g.Snapshots.Where(t => t.Puzzle != null).Select(t => (Game: g, Turn: t))).ToList();
            if (puzzles.Count == 0) { Console.WriteLine("No lethal puzzles yet: they're saved whenever HearthStoner sees you have lethal."); return 0; }
            Console.WriteLine($"{puzzles.Count} puzzle(s). Press Enter to reveal each solution.\n");
            var n = 0;
            foreach (var (g, t) in puzzles)
            {
                var p = t.Puzzle;
                n++;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Puzzle {n}: {g.Start:MMM d} {MetaStats.ClassName(g.MyClass)} vs {g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass)}, turn {(t.Turn + 1) / 2}" +
                                  (g.Review.Any(r => r.Kind == "MissedLethal" && r.Turn == t.Turn) ? "  (you missed this one)" : ""));
                Console.ResetColor();
                Console.WriteLine($"  Opponent: {p.TheirHealth} HP{(p.TheirArmor > 0 ? $" + {p.TheirArmor} armor" : "")}");
                Console.WriteLine($"  Their board: {(p.TheirMinions.Count > 0 ? string.Join(", ", p.TheirMinions) : "empty")}");
                Console.WriteLine($"  Your board: {(p.YourMinions.Count > 0 ? string.Join(", ", p.YourMinions) : "empty")}{(p.Weapon != null ? $"; weapon {p.Weapon}" : "")}");
                Console.WriteLine($"  Your hand ({p.Mana} mana): {string.Join(", ", p.Hand)}");
                Console.Write("  Find lethal, then press Enter... ");
                Console.ReadLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  {p.Solution}\n");
                Console.ResetColor();
            }
            return 0;
        }

        private static int QuizCommand()
        {
            var cards = CardDb.Load();
            var hands = GameRecord.LoadAll().Where(g => g.Mulligan.Count(m => m.Advice != null) >= 2).ToList();
            if (hands.Count == 0) { Console.WriteLine("No opening hands with data yet. Play a few games with HearthStoner running."); return 0; }
            var rng = new Random();
            int right = 0, total = 0;
            Console.WriteLine("Mulligan quiz: type k (keep) or t (toss) for each card; q quits.\n");
            foreach (var g in hands.OrderBy(_ => rng.Next()).Take(10))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{g.MyArchetype ?? MetaStats.ClassName(g.MyClass)} vs {MetaStats.ClassName(g.OpponentClass)}, {(g.WentFirst ? "going first" : "on the coin")}");
                Console.ResetColor();
                Console.WriteLine("  Hand: " + string.Join(", ", g.Mulligan.Select(m => cards.NameOf(m.CardId))));
                foreach (var m in g.Mulligan.Where(m => m.Advice != null && m.Advice != "either"))
                {
                    Console.Write($"  {cards.NameOf(m.CardId)}? ");
                    var a = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    if (a == "q") { Console.WriteLine($"\nScore: {right}/{total}"); return 0; }
                    var keep = a.StartsWith("k");
                    var correct = keep == (m.Advice == "KEEP");
                    total++; if (correct) right++;
                    Console.ForegroundColor = correct ? ConsoleColor.Green : ConsoleColor.Red;
                    Console.WriteLine($"    {(correct ? "Right" : "Data says " + m.Advice.ToLower())}: {(m.Delta >= 0 ? "+" : "")}{m.Delta * 100:F1}% win rate when kept. You {(m.Kept ? "kept" : "tossed")} it in that game.");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
            Console.WriteLine($"Score: {right}/{total}{(total > 0 ? $" ({(double)right / total:P0})" : "")}");
            return 0;
        }

        private static int QuestsCommand()
        {
            using (var mem = HsMemory.Attach(_verbose))
            {
                var log = ReadQuests(mem, new Dictionary<int, QuestEntry>());
                if (log == null) { Console.WriteLine("No quests found (open the Quest log in game once, then try again)."); return 0; }
                log.Save();
                foreach (var q in log.Quests.OrderBy(q => q.Pool))
                    Console.WriteLine($"{q.Pool,-7} {q.Text}  {q.Progress}/{q.Quota}  {q.Xp} XP  (quest {q.Id}, status {q.Status}, icon {q.Icon})");
                if (log.Track is TrackState t) Console.WriteLine($"Reward track: level {t.Level}, {t.Xp}/{t.XpNeeded} XP");
                if (_verbose) foreach (var q in log.Quests) foreach (var line in mem.DbfRecord("Quest", q.Id)) Console.WriteLine("    " + line);
            }
            return 0;
        }

        private static int ReviewCommand(int count)
        {
            var games = GameRecord.LoadAll();
            if (games.Count == 0) { Console.WriteLine("No saved games yet."); return 0; }
            var cards = CardDb.Load();
            foreach (var g in games.Skip(Math.Max(0, games.Count - count)))
            {
                PrintSnapshot(new AdviceSnapshot { Sections = { Advisor.ReviewSection(g) } });
                if (g.Mulligan.Count > 0)
                    Console.WriteLine("    Mulligan: " + string.Join(", ", g.Mulligan.Select(m => $"{cards.NameOf(m.CardId)} {(m.Kept ? "kept" : "tossed")}{(m.Advice != null ? $" (data: {m.Advice.ToLower()})" : "")}")));
                foreach (var t in g.Snapshots.Where(t => t.Yours))
                    Console.WriteLine($"    T{(t.Turn + 1) / 2}: you {t.MyHp}+{t.MyArmor} hp, board {t.MyBoardAttack}/{t.MyBoardHealth} vs {t.TheirBoardAttack}/{t.TheirBoardHealth}, them {t.TheirHp}+{t.TheirArmor}; mana {t.ManaAvailable - t.ManaUnspent}/{t.ManaAvailable}" +
                                      (t.Played.Count > 0 ? $"; played {string.Join(", ", t.Played.Select(cards.NameOf))}" : ""));
                Console.WriteLine();
            }
            return 0;
        }

        /// <summary>Prints the current meta (archetypes by popularity) to check the stats download.</summary>
        private static int MetaCommand(string heroClass, string rank, string period)
        {
            var meta = LoadMeta(rank, period);
            if (meta.Archetypes.Count == 0) { Console.Error.WriteLine("No meta data available."); return 1; }
            Console.WriteLine($"Standard meta ({meta.Rank}, {meta.Period}), updated {meta.Updated:u}, {meta.TotalGames:N0} games\n");
            var list = heroClass != null ? meta.ForClass(heroClass).ToList() : meta.Archetypes.Take(40).ToList();
            foreach (var a in list)
                Console.WriteLine($"  {a.Share,6:P1}  {a.WinRate,6:P1}  {a.Games,8:N0}  {MetaStats.ClassName(a.Class),-13} {a.Name}");
            return 0;
        }

        private static MetaStats LoadMeta(string rank, string period)
        {
            var meta = MetaStats.Load("standard", rank ?? "all", period ?? "past-7", Log);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(meta.Archetypes.Count > 0
                ? $"Meta stats: {meta.Archetypes.Count} archetypes from {meta.TotalGames:N0} games ({meta.Rank}, {meta.Period}) - Firestone"
                : "Meta stats unavailable: deck prediction and mulligan data are off.");
            Console.ResetColor();
            return meta;
        }

        private static void Warn(string msg)
        {
            lock (ConsoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(msg);
                Console.ResetColor();
            }
        }

        private static void Log(string msg)
        {
            if (!_verbose && !msg.StartsWith("[meta]")) return;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Error.WriteLine(msg);
            Console.ResetColor();
        }

        private static readonly object ConsoleLock = new object();

        private static void PrintSnapshot(AdviceSnapshot snap)
        {
            lock (ConsoleLock)
            {
                Console.WriteLine();
                foreach (var sec in snap.Sections)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  [{sec.Title}]");
                    foreach (var l in sec.Lines)
                    {
                        Console.ForegroundColor = l.Tone switch
                        {
                            Tone.Good => ConsoleColor.Green,
                            Tone.Bad => ConsoleColor.Red,
                            Tone.Warn => ConsoleColor.DarkYellow,
                            Tone.Dim => ConsoleColor.DarkGray,
                            Tone.Heading => ConsoleColor.Cyan,
                            _ => ConsoleColor.Gray,
                        };
                        Console.WriteLine("    " + l.Text);
                    }
                }
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>App mode: hide the console window and send console output to %LocalAppData%\HearthStoner\hearthstoner.log.</summary>
        private static void ConsoleToLogFile()
        {
            try
            {
                var dir = AppPaths.DataDir;
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "hearthstoner.log");
                if (File.Exists(path) && new FileInfo(path).Length > 5_000_000) File.Delete(path);
                var w = TextWriter.Synchronized(new StreamWriter(path, append: true) { AutoFlush = true });
                w.WriteLine($"==== HearthStoner started {DateTime.Now:G} ====");
                Console.SetOut(w);
                Console.SetError(w);
                ShowWindow(GetConsoleWindow(), 0);   // SW_HIDE
            }
            catch { }
        }

        private static bool SafeEnsureClientConfig(string install)
        {
            try { return HearthstoneLocator.EnsureClientConfig(install); }
            catch (Exception ex)
            {
                Console.WriteLine($"Couldn't update {HearthstoneLocator.ClientConfigPath(install)} ({ex.Message}). Hearthstone will stop logging after ~10 MB per session.");
                return false;
            }
        }

        // ------------------------------------------------------------------ watch helpers

        /// <summary>Prints mode and opponent once they show up in memory; returns the match info (or null).</summary>
        private static IMatchInfo ShowGameStartInfo(LiveMemory live)
        {
            IMatchInfo match = null;
            for (var i = 0; i < 30 && match == null; i++)
            {
                var m = live.Use(x => x.GetMatchInfo());
                if (m?.OpposingPlayer != null && !string.IsNullOrEmpty(m.OpposingPlayer.Name)) match = m;
                else Thread.Sleep(500);
            }
            if (match == null) return null;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  Mode: {match.GameType} ({match.FormatType})");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  You: {DescribePlayer(match.LocalPlayer, match.FormatType)}");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  Opponent: {DescribePlayer(match.OpposingPlayer, match.FormatType)}");
            Console.ResetColor();
            return match;
        }

        /// <summary>
        /// Finds your deck list for the current game and hands it to the tracker. Tries Decks.log first (the deck
        /// you queued with, logged by Hearthstone), then game memory. Keeps retrying every 2 seconds while a game
        /// is on and no list is known.
        /// </summary>
        private sealed class DeckLoader
        {
            private readonly GameTracker _tracker;
            private readonly Advisor _advisor;
            private readonly CardDb _cards;
            private readonly LiveMemory _live;
            private readonly Func<string> _powerLog;
            private volatile bool _reset = true;
            private int _attempts;

            public DeckLoader(GameTracker tracker, Advisor advisor, CardDb cards, LiveMemory live, Func<string> powerLog)
            {
                _tracker = tracker; _advisor = advisor; _cards = cards; _live = live; _powerLog = powerLog;
            }

            public void Reset() => _reset = true;

            public void Loop()
            {
                while (true)
                {
                    try { Step(); }
                    catch (Exception ex) { Log("[deck] " + ex.Message); }
                    Thread.Sleep(2000);
                }
            }

            private void Step()
            {
                bool inGame, haveDeck;
                lock (_tracker.Sync)
                {
                    inGame = _tracker.InGame && _tracker.FriendlyPlayerId != 0;
                    haveDeck = _tracker.FriendlyDeckList != null;
                }
                if (_reset) { _reset = false; _attempts = 0; }
                if (!inGame || haveDeck || _attempts >= 30) return;
                _attempts++;

                string source = null, name = null;
                List<string> list = null;

                var queued = DecksLog.LastQueuedDeck(_powerLog());
                if (queued != null)
                {
                    list = DeckCode.Decode(queued.DeckCode, _cards);
                    if (list != null && list.Count > 0) { source = "Decks.log"; name = queued.Name; }
                }
                if (list == null || list.Count == 0)
                {
                    var deck = _live.Use(x => x.GetActiveDeck());
                    if (deck?.DeckList != null && deck.DeckList.Count > 0)
                    {
                        list = deck.DeckList.ToList();
                        // Sideboard picks that join the deck (Commander Beatrix: ten copies of her pick).
                        if (deck is HackF5.UnitySpy.HearthstoneLib.Detail.Deck.Deck full && full.Sideboards != null)
                            list.AddRange(DeckCode.JoiningCopies(full.Sideboards.SelectMany(sb => (sb.Cards ?? new List<string>())
                                .Select(c => new DeckCode.Sideboard { CardId = c, OwnerId = sb.KeyCardId, Count = 1 })), _cards));
                        name = deck.Name; source = "memory";
                    }
                }
                if (list == null || list.Count == 0)
                {
                    if (_attempts == 1 || _attempts == 30) Log($"[deck] Couldn't find your deck list yet (attempt {_attempts}).");
                    return;
                }

                lock (_tracker.Sync)
                {
                    if (!_tracker.InGame || _tracker.FriendlyDeckList != null) return;
                    _tracker.FriendlyDeckList = list;
                    _tracker.FriendlyDeckName = name;
                }
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  Your deck: {(string.IsNullOrEmpty(name) ? "(unnamed)" : name)} ({list.Count} cards, from {source})");
                Console.ResetColor();
                _advisor.PrepareFriendlyDeck(Log);
                if (_advisor.FriendlyArchetype != null)
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss}  Your deck plays as: {_advisor.FriendlyArchetype.Name} ({_advisor.FriendlyArchetype.WinRate:P1} WR in the meta)");
            }
        }

        private static void PrintSummary(GameTracker t, CardDb cards)
        {
            string Fmt(IEnumerable<string> ids) => string.Join(", ",
                ids.Where(i => !string.IsNullOrEmpty(i)).GroupBy(i => i)
                   .Select(g => g.Count() > 1 ? $"{cards.NameOf(g.Key)} x{g.Count()}" : cards.NameOf(g.Key)));

            Console.WriteLine();
            Console.WriteLine($"  You played:      {Fmt(t.FriendlyPlayed)}");
            Console.WriteLine($"  Opponent played: {Fmt(t.OpponentPlayed)}");
        }

        /// <summary>Keeps one attachment to Hearthstone and transparently re-attaches if the game restarts.</summary>
        private sealed class LiveMemory : IDisposable
        {
            private readonly bool _verbose;
            private readonly object _lock = new object();
            private HsMemory _mem;

            public LiveMemory(bool verbose) => _verbose = verbose;

            public T Use<T>(Func<HsMemory, T> read)
            {
                lock (_lock)
                {
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        try
                        {
                            if (_mem == null || !IsAlive(_mem.ProcessId))
                            {
                                _mem?.Dispose();
                                _mem = null;
                                if (!HsMemory.IsHearthstoneRunning) return default;
                                _mem = HsMemory.Attach(_verbose);
                            }
                            return read(_mem);
                        }
                        catch (Exception ex)
                        {
                            if (_verbose) Console.Error.WriteLine("[memory] " + ex.Message);
                            _mem?.Dispose();
                            _mem = null;
                        }
                    }
                    return default;
                }
            }

            private static bool IsAlive(int pid)
            {
                try { return !System.Diagnostics.Process.GetProcessById(pid).HasExited; }
                catch { return false; }
            }

            public void Dispose() { lock (_lock) { _mem?.Dispose(); _mem = null; } }
        }

        /// <summary>
        /// Keeps collection.json current by itself, for as long as the app runs. At the menu it checks the card count
        /// (a cheap read) each minute and reads the whole collection only when something may have changed: the count
        /// moved, you left pack opening or the collection screen (crafting, disenchanting), or a game just ended
        /// (rewards). Never during a game.
        /// </summary>
        private static void WatchCollection(LiveMemory live, GameTracker tracker, AppServices services)
        {
            var lastCount = -1;
            var wasInGame = false;
            SceneModeEnum? lastScene = null;
            var questDefs = new Dictionary<int, QuestEntry>();   // the Quest table doesn't change while the app runs
            string questKey = null;
            while (true)
            {
                try
                {
                    var inGame = tracker.InGame;
                    var gameEnded = wasInGame && !inGame;
                    wasInGame = inGame;
                    if (!inGame)
                    {
                        var now = live.Use(m => m.IsCollectionReady() ? (Scene: m.GetSceneMode(), Count: m.CollectionSize()) : default);
                        if (now.Count > 0)
                        {
                            var leftShop = lastScene is SceneModeEnum.PACKOPENING or SceneModeEnum.COLLECTIONMANAGER && now.Scene != lastScene;
                            if ((now.Count != lastCount || leftShop || gameEnded) && services.RefreshCollection() is OwnedCollection c)
                            {
                                Console.WriteLine($"[collection] saved ({c.Owned.Count} cards)");
                                if (lastCount >= 0) services.CollectionSaved?.Invoke();
                                lastCount = now.Count;
                            }
                            lastScene = now.Scene;
                        }
                        else lastScene = null;
                        SaveQuests(live, questDefs, ref questKey, services);
                    }
                }
                catch (Exception ex) { Console.WriteLine("[collection] " + ex.Message); }
                // Look more often while you're on the screens where the collection changes.
                Thread.Sleep(lastScene is SceneModeEnum.PACKOPENING or SceneModeEnum.COLLECTIONMANAGER ? 10000 : 60000);
            }
        }

        /// <summary>
        /// Saves your quests (with their definitions) and the reward track to quests.json when any of them changed, so the
        /// Play page shows them while Hearthstone is closed. Called at the menu only.
        /// </summary>
        private static void SaveQuests(LiveMemory live, Dictionary<int, QuestEntry> defs, ref string lastKey, AppServices services)
        {
            var log = live.Use(m =>
            {
                // Caught here: an exception inside Use would drop the attachment.
                try { return ReadQuests(m, defs); }
                catch (Exception ex) { if (_verbose) Console.WriteLine("[quests] " + ex.Message); return null; }
            });
            if (log == null) return;
            var key = string.Join(",", log.Quests.Select(q => $"{q.Id}:{q.Progress}:{q.Status}")) + "|" + log.Track?.TotalXp + "|" + log.SeasonEnd;
            if (key == lastKey) return;
            log.Save();
            lastKey = key;
            Console.WriteLine($"[quests] saved ({log.Quests.Count} quests)");
            services.CollectionSaved?.Invoke();
        }

        private static QuestLog ReadQuests(HsMemory m, Dictionary<int, QuestEntry> defs)
        {
            var quests = m.GetQuests()?.Quests;
            if (quests == null || quests.Count == 0) return null;
            var log = new QuestLog { Updated = DateTime.Now };
            foreach (var q in quests)
            {
                if (!defs.TryGetValue(q.Id, out var def)) defs[q.Id] = def = m.QuestDef(q.Id);
                if (def != null) log.Quests.Add(def.With(q.Progress, q.Status));
            }
            var t = m.GetRewardTrack()?.TrackEntries?.OrderBy(x => x.TrackType == 1 ? 0 : 1).FirstOrDefault();
            if (t != null) log.Track = new TrackState { Level = t.Level, Xp = t.Xp, XpNeeded = t.XpNeeded, TotalXp = t.TotalXp, BonusPercent = t.XpBonusPercent };
            try { log.SeasonEnd = m.SeasonEnd(); } catch (Exception ex) { if (_verbose) Console.WriteLine("[season] " + ex.Message); }
            return log;
        }

        // ------------------------------------------------------------------ general helpers

        private static IRank RankOf(IPlayer p, GameFormat format)
        {
            if (p == null) return null;
            return format switch
            {
                GameFormat.FT_WILD => p.Wild,
                GameFormat.FT_CLASSIC => p.Classic,
                GameFormat.FT_TWIST => p.Twist,
                _ => p.Standard,
            };
        }

        /// <summary>The meta stats bracket for "auto": from the rank in your most recent saved game.</summary>
        private static string MetaRankSetting(Settings settings)
        {
            if (!string.Equals(settings.MetaRank, "auto", StringComparison.OrdinalIgnoreCase)) return settings.MetaRank;
            var last = GameRecord.LoadAll().LastOrDefault(g => g.StarLevel > 0 || g.LegendRank > 0);
            if (last == null) return "all";
            if (last.LegendRank > 0 || last.StarLevel > 50) return "legend";
            if (last.StarLevel > 40) return "diamond";
            if (last.StarLevel > 30) return "platinum";
            return "bronze-gold";
        }

        private static string DescribePlayer(IPlayer p, GameFormat format)
        {
            if (p == null) return "?";
            var rank = RankOf(p, format);
            var tag = p.BattleTag != null && !string.IsNullOrEmpty(p.BattleTag.Name) ? $"{p.BattleTag.Name}#{p.BattleTag.Number}" : p.Name;
            return rank == null ? tag : $"{tag} - {RankName(rank)}";
        }

        private static string RankName(IRank r)
        {
            if (r.LegendRank > 0) return $"Legend {r.LegendRank}";
            if (r.StarLevel <= 0) return "unranked";
            // Star levels 1-50: Bronze 10 .. Diamond 1, ten ranks per league.
            string[] leagues = { "Bronze", "Silver", "Gold", "Platinum", "Diamond" };
            var idx = Math.Min((r.StarLevel - 1) / 10, leagues.Length - 1);
            var rank = 10 - (r.StarLevel - 1) % 10;
            return $"{leagues[idx]} {rank}";
        }

        private static bool WaitFor(Func<bool> ready, string what)
        {
            for (var i = 0; i < 30; i++)
            {
                if (Safe(ready)) return true;
                if (i == 0) Console.WriteLine($"Waiting for {what}...");
                Thread.Sleep(1000);
            }
            Console.Error.WriteLine($"Timed out waiting for {what}.");
            return false;
        }

        private static T Retry<T>(Func<T> f, Func<T, bool> ok, string what) where T : class
        {
            Exception last = null;
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    var r = f();
                    if (ok(r)) return r;
                }
                catch (Exception ex) { last = ex; }
                Thread.Sleep(1000);
            }
            Console.Error.WriteLine($"Couldn't read {what} from memory{(last != null ? $" ({last.Message})" : "")}. " +
                                    "If Hearthstone was just patched, field names may have changed - try 'HearthStoner explore CollectionManager.s_instance'.");
            return null;
        }

        private static T Safe<T>(Func<T> f)
        {
            try { return f(); }
            catch (Exception ex) { if (_verbose) Console.Error.WriteLine("[memory] " + ex.Message); return default; }
        }

        private static string FormatName(int formatType) => formatType switch
        {
            1 => "Wild",
            2 => "Standard",
            3 => "Classic",
            4 => "Twist",
            _ => "Format " + formatType,
        };

        private static readonly string[] Classes =
            { "DEATHKNIGHT", "DEMONHUNTER", "DRUID", "HUNTER", "MAGE", "PALADIN", "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR", "NEUTRAL" };
        private static int ClassOrder(string c) { var i = Array.IndexOf(Classes, c); return i < 0 ? 99 : i; }

        private static string Pretty(string s) =>
            string.IsNullOrEmpty(s) ? "Neutral" : char.ToUpper(s[0]) + s.Substring(1).ToLower().Replace('_', ' ');

        private static string Csv(string s) => s != null && (s.Contains(",") || s.Contains("\"")) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

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

        private sealed class CollectionRow
        {
            public string CardId { get; set; }
            public string Name { get; set; }
            public string Class { get; set; }
            public string Set { get; set; }
            public string Rarity { get; set; }
            public int? Cost { get; set; }
            public string Finish { get; set; }
            public int Count { get; set; }
        }
    }
}
