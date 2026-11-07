using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HackF5.UnitySpy.HearthstoneLib;

namespace HearthStoner
{
    internal static partial class Program
    {
        /// <summary>
        /// 'watch', 'overlay' and 'app': runs a <see cref="GameSession"/> until Ctrl+C (or forever in app mode), printing
        /// the play-by-play and showing the overlay or the app window on top of it.
        /// </summary>
        /// <param name="hsPath">The --hs option, or null to find the install.</param>
        /// <param name="overlay">Show the in-game overlay.</param>
        /// <param name="tips">Print the advice every turn (watch mode).</param>
        /// <param name="rank">The --rank option, or null.</param>
        /// <param name="period">The --period option, or null.</param>
        /// <param name="app">Run the full app: tray icon, overlay and window; console output goes to hearthstoner.log.</param>
        /// <param name="minimized">App mode: start in the tray without opening the window.</param>
        /// <returns>The exit code.</returns>
        private static int Watch(string hsPath, bool overlay, bool tips, string rank, string period, bool app = false, bool minimized = false)
        {
            if (app) ConsoleToLogFile();
            var install = PrepareInstall(hsPath);
            var settings = Settings.Load();
            var cards = CardDb.Load();
            var meta = LoadMeta(rank ?? Ranks.MetaBracket(settings), period ?? settings.MetaPeriod);
            if (settings.CheckForUpdates)
                Task.Run(() => { var msg = UpdateCheck.CheckUnitySpy(); if (msg != null) Warn(msg); });

            using (var session = new GameSession(install, settings, cards, meta, new LiveMemory(_verbose, Console.Error.WriteLine),
                       LoadMeta, rank, period, say: Info, log: Log, warn: Warn))
            {
                session.EventSeen += ev => PrintEvent(session, ev, tips);
                session.MatchFound += PrintMatch;
                session.GameSaved += (record, path) =>
                {
                    PrintSnapshot(new AdviceSnapshot { Sections = { Advisor.ReviewSection(record) } });
                    Console.WriteLine($"  Saved to {path}\n");
                };

                var tag = session.Start();
                if (tag != null) Console.WriteLine($"Logged in as {tag}");
                else if (!HsMemory.IsHearthstoneRunning) Console.WriteLine("Hearthstone isn't running yet - start it whenever you like.");

                if (app) StartApp(session, tag, minimized);
                else if (overlay) StartOverlay(session);
                Follow(session);
            }
            return 0;
        }

        /// <summary>Finds the install and makes sure Hearthstone writes complete logs.</summary>
        private static string PrepareInstall(string hsPath)
        {
            var install = HearthstoneLocator.FindInstallDir(hsPath)
                          ?? throw new DirectoryNotFoundException("Couldn't find Hearthstone. Pass --hs \"C:\\path\\to\\Hearthstone\".");
            if (HearthstoneLocator.EnsureLogConfig())
                Console.WriteLine("Enabled Power logging in log.config.");
            if (SafeEnsureClientConfig(install))
                Console.WriteLine("Removed Hearthstone's 10 MB log limit in client.config.");
            WarnIfRestartNeeded(install);
            return install;
        }

        /// <summary>One line of play-by-play; at the end of a game, the summary. Runs with the tracker lock held.</summary>
        private static void PrintEvent(GameSession session, GameEvent ev, bool tips)
        {
            var tracker = session.Tracker;
            var suffix = ev.Kind == GameEventKind.Draw && ev.Friendly
                ? $"  ({tracker.DeckCount(tracker.FriendlyPlayerId)} left in deck)"
                : "";
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

            if (ev.Kind == GameEventKind.Turn && tips && !tracker.Quiet)
                Task.Run(() => { Thread.Sleep(300); PrintSnapshot(session.Advisor.Compute()); });
            if (ev.Kind == GameEventKind.GameEnd)
            {
                PrintSummary(tracker, session.Cards);
                var left = tracker.FriendlyRemainingDeck();
                if (left.Count > 0)
                    Console.WriteLine($"  Never drawn:     {string.Join(", ", left.SelectMany(kv => Enumerable.Repeat(session.Cards.NameOf(kv.Key), kv.Value)))}\n");
            }
        }

        /// <summary>Mode and both players with their ranks, once the match info shows up in memory.</summary>
        private static void PrintMatch(IMatchInfo match)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  Mode: {match.GameType} ({match.FormatType})");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  You: {Ranks.Describe(match.LocalPlayer, match.FormatType)}");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  Opponent: {Ranks.Describe(match.OpposingPlayer, match.FormatType)}");
            Console.ResetColor();
        }

        /// <summary>App mode: the tray icon, overlay and window, the game-art job, and the collection and quest watcher.</summary>
        private static void StartApp(GameSession session, string tag, bool minimized)
        {
            var advisor = session.Advisor;
            var settings = session.Settings;
            var services = new AppServices
            {
                InstallDir = session.InstallDir,
                Cards = session.Cards,
                Meta = () => advisor.Meta,
                Settings = settings,
                Log = Log,
                PlayerName = tag,
                ReloadMeta = () => advisor.Meta = LoadMeta(Ranks.MetaBracket(settings), settings.MetaPeriod),
                RefreshCollection = () => RefreshCollection(session.Live),
            };
            // Keep every game in Hearthstone's log folders replayable: attach logs to records, record older games.
            services.ImportLogs = () => GameLog.Import(session.InstallDir, session.Cards, advisor.Meta, Log);
            AppHost.Start(() => advisor.Compute(), advisor, settings, Log, services, showWindow: !minimized);
            GameArt.StartJob(session.InstallDir, () => session.Tracker.InGame);

            var watcher = new CollectionWatcher(session.Live, () => session.Tracker.InGame, services.RefreshCollection,
                () => services.CollectionSaved?.Invoke(), Console.WriteLine, _verbose ? Console.WriteLine : null);
            Task.Run(watcher.Loop);
        }

        /// <summary>Reads your whole collection from memory and saves it; null when it can't be read right now.</summary>
        private static OwnedCollection RefreshCollection(ILiveMemory live)
        {
            var col = live.Use(m => m.IsCollectionReady() ? m.GetCollection() : null);
            if (col == null || col.Count == 0) return null;
            var owned = OwnedCollection.FromMemory(col);
            owned.Save();
            return owned;
        }

        /// <summary>Overlay mode: the overlay over Hearthstone, with the hotkeys printed.</summary>
        private static void StartOverlay(GameSession session)
        {
            var settings = session.Settings;
            OverlayWindow.Start(() => session.Advisor.Compute(), session.Advisor, settings, Log);
            Console.WriteLine("Overlay on. It shows over Hearthstone while the game window is active (windowed or borderless fullscreen).");
            Console.WriteLine($"Hotkeys: {settings.HotkeyToggleOverlay} overlay, {settings.HotkeyToggleDeck} deck list, {settings.HotkeyToggleOpponent} opponent, {settings.HotkeyToggleAdvice} advice, {settings.HotkeyHoverMode} hover mode.");
            Console.WriteLine($"Settings: {Settings.Path}");
        }

        /// <summary>Follows Power.log until Ctrl+C.</summary>
        private static void Follow(GameSession session)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
            Console.WriteLine("Watching for games. Press Ctrl+C to stop.\n");
            session.Run(cts.Token, msg =>
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(msg);
                Console.ResetColor();
            });
        }
    }
}
