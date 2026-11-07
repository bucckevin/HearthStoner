using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HearthStoner
{
    /// <summary>Everything the app window needs.</summary>
    public sealed class AppServices
    {
        public string InstallDir;
        public CardDb Cards;
        public Func<MetaStats> Meta;
        public Settings Settings;
        public Action<string> Log;
        /// <summary>Re-reads your collection from memory (Hearthstone must be running). Returns null on failure.</summary>
        public Func<OwnedCollection> RefreshCollection;
        /// <summary>Set by the window: called after the collection was saved, so the pages show it.</summary>
        public Action CollectionSaved;
        /// <summary>Loads the meta stats again after the rank bracket or period setting changed.</summary>
        public Action ReloadMeta;
        /// <summary>Attaches past games' logs to their records (and records older games); run once at startup.</summary>
        public Func<GameLog.ImportResult> ImportLogs;
        /// <summary>Your BattleTag, when Hearthstone is running.</summary>
        public string PlayerName;
    }

    /// <summary>
    /// The desktop window: the <see cref="AppShell"/> (navigation rail and pages) under a dark title bar, with a
    /// small notice that slides in for messages. Data loads in the background; card art redraws as it arrives.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private readonly AppServices _svc;
        private readonly AppShell _shell = new AppShell();
        private readonly Border _toast;
        private readonly TextBlock _toastText = Theme.Text("", 13.5, Theme.Vellum, wrap: true);
        private volatile bool _artArrived;
        private int _tileVersion;

        public MainWindow(AppServices svc)
        {
            _svc = svc;
            Title = "HearthStoner";
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/HearthStoner;component/App/hearthstoner.ico")); } catch (Exception swallowed) { Diag.Ignored(swallowed, "MainWindow"); }
            // Roomy by default (the replay board grows with the window), within the screen.
            var work = SystemParameters.WorkArea;
            Width = Math.Min(1640, work.Width * 0.92); Height = Math.Min(1100, work.Height * 0.92);
            MinWidth = 1080; MinHeight = 640;
            Background = Theme.Night;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            _toast = new Border
            {
                Child = _toastText, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 10, 16, 11), MaxWidth = 520,
                Background = Theme.Raised, BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 24, 24),
                Opacity = 0, IsHitTestVisible = false,
            };
            // No Windows title bar: the app runs to the top edge, the top strip drags the window, and AppShell draws
            // the minimize/maximize/close buttons. Windows still gives the window its border, shadow and snapping.
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 30, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
            });
            var root = new Grid();
            // Maximized, Windows pushes the window's edges just off screen: pad them back in.
            StateChanged += (s, e) =>
            {
                var b = SystemParameters.WindowResizeBorderThickness;
                root.Margin = WindowState == WindowState.Maximized ? new Thickness(b.Left + 4, b.Top + 4, b.Right + 4, b.Bottom + 4) : new Thickness(0);
            };
            root.Children.Add(_shell);
            root.Children.Add(_toast);
            Content = root;

            _shell.ReloadRequested = () => Reload();
            _shell.Status = Notice;
            SourceInitialized += (s, e) => Theme.DarkCaption(this);
            Loaded += (s, e) => Reload();
            _svc.CollectionSaved = () => _gamesChangedAt = DateTime.UtcNow;   // reload like after a game
            ArtCache.Arrived += () => _artArrived = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            // Refresh by itself when a game is saved (a game just ended, or older ones were imported): watch the games
            // folder, and reload once it has been quiet for a moment (the record and its log are written together).
            try
            {
                _watcher = new System.IO.FileSystemWatcher(GameRecord.Dir, "*.json") { NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.LastWrite };
                System.IO.FileSystemEventHandler changed = (s, e) => _gamesChangedAt = DateTime.UtcNow;
                _watcher.Created += changed;
                _watcher.Changed += changed;
                _watcher.Renamed += (s, e) => _gamesChangedAt = DateTime.UtcNow;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { _svc.Log?.Invoke("[watch games] " + ex.Message); }
            timer.Tick += (s, e) =>
            {
                if (_gamesChangedAt is DateTime at && DateTime.UtcNow - at > TimeSpan.FromSeconds(2) && _shell.Data != null && !_reloading)
                {
                    _gamesChangedAt = null;
                    Reload(newGames: true);
                }
                if (_artArrived || CardTiles.Version != _tileVersion)
                {
                    _artArrived = false;
                    _tileVersion = CardTiles.Version;
                    if (_shell.Data != null) _shell.Redraw();
                }
            };
            timer.Start();
        }

        private bool _imported;
        private System.IO.FileSystemWatcher _watcher;
        private volatile bool _reloading;
        private DateTime? _gamesChangedAt;

        /// <param name="newGames">Reloading because games were saved: keep the game you were looking at selected, and say what's new.</param>
        private void Reload(bool newGames = false)
        {
            _reloading = true;
            var before = _shell.Data;
            Task.Run(() =>
            {
                if (!_imported && _svc.ImportLogs != null)
                {
                    _imported = true;
                    try
                    {
                        var r = _svc.ImportLogs();
                        if (r.Created > 0) Dispatcher.Invoke(() => Notice($"Added {r.Created} earlier game{(r.Created == 1 ? "" : "s")} from Hearthstone's logs."));
                    }
                    catch (Exception ex) { _svc.Log?.Invoke("[import] " + ex.Message); }
                }
                AppData data;
                try { data = AppData.Load(_svc); }
                finally { _reloading = false; }
                Dispatcher.Invoke(() =>
                {
                    if (newGames) Console.WriteLine($"[games] refreshed after games were saved ({data.Games.Count} games)");   // to hearthstoner.log
                    if (newGames && before != null)
                    {
                        // The lists are newest first, so a new game moves everything down one: follow the selected games.
                        var history = before.Games.ElementAtOrDefault(_shell.SelectedGame)?.FileStem;
                        var replay = ReplayPage.Replayable(before).ElementAtOrDefault(_shell.ReplayGame)?.LogFile;
                        var h = data.Games.FindIndex(g => g.FileStem == history);
                        if (h >= 0) _shell.SelectedGame = h;
                        var r = ReplayPage.Replayable(data).FindIndex(g => g.LogFile == replay);
                        if (r >= 0) _shell.ReplayGame = r;
                        var fresh = data.Games.Where(g => !before.Games.Any(b => b.FileStem == g.FileStem)).ToList();
                        if (fresh.Count == 1)
                        {
                            var g = fresh[0];
                            Notice($"Game recorded: {(g.Won ? "win" : g.Result == "LOST" ? "loss" : "game")} vs {g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass)}.");
                        }
                        else if (fresh.Count > 1) Notice($"{fresh.Count} games recorded.");
                    }
                    _shell.SetData(data);
                });
            });
        }

        /// <summary>A message that fades in at the bottom right and fades out after a few seconds.</summary>
        public void Notice(string text)
        {
            _toastText.Text = text;
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5))));
            _toast.BeginAnimation(OpacityProperty, a);
        }

        // ------------------------------------------------------------------ screenshots

        /// <summary>
        /// 'HearthStoner screenshot [folder]': renders every page at 1280x800 to PNGs (app-play.png, ...), the same way
        /// the window draws them. Downloads any card art it needs first. For checking the look without clicking.
        /// </summary>
        public static int Screenshots(AppServices svc, string folder)
        {
            Exception error = null;
            var t = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null) new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var data = AppData.Load(svc);
                    if (data.Error != null) Console.WriteLine("Data error: " + data.Error);
                    // Card art (banners) and tiles (card lists), downloaded if missing.
                    ArtCache.Prefetch(data.Picks.Select(p => p.SignatureCardId).Where(x => x != null), TimeSpan.FromSeconds(30));
                    foreach (var id in data.Decks.SelectMany(dk => dk.Cards).Distinct()) CardTiles.Get(id);
                    foreach (var m in PracticePage.QuizHands(data).Take(1).SelectMany(g => g.Mulligan)) CardTiles.GetRender(m.CardId);
                    var v = -1;
                    for (var i = 0; i < 40 && v != CardTiles.Version; i++) { v = CardTiles.Version; Thread.Sleep(500); }

                    Directory.CreateDirectory(folder);
                    var shell = new AppShell { Width = 1280, Height = 800 };
                    TextOptions.SetTextFormattingMode(shell, TextFormattingMode.Ideal);
                    shell.SetData(data);
                    // The replay of the newest game, half way through, with its art.
                    var rgames = ReplayPage.Replayable(data);
                    if (rgames.Count > 0)
                    {
                        var rp = ReplayPage.Get(rgames[0], data);
                        if (rp != null && rp.Frames.Count > 0)
                        {
                            var mid = rp.TurnStarts.Where(kv => kv.Key >= rp.Turns / 2).Select(kv => kv.Value).DefaultIfEmpty(rp.Frames.Count / 2).Min() + 3;
                            shell.ReplayFrame = Math.Min(mid, rp.Frames.Count - 1);
                            var fr = rp.Frames[shell.ReplayFrame];
                            var ids = new[] { fr.You, fr.Them }.SelectMany(sd => sd.Board.Select(m => m.CardId).Append(sd.HeroCardId).Append(sd.HeroPowerCardId).Append(sd.WeaponCardId)).Where(x => x != null).ToList();
                            ArtCache.Prefetch(ids, TimeSpan.FromSeconds(30));
                            ArtCache.Prefetch(ids.Where(ArtCache.HasFailed).Select(ReplayPage.BaseHero).Where(x => x != null), TimeSpan.FromSeconds(30));
                            foreach (var h in fr.You.Hand.Concat(fr.Them.Hand)) if (h.CardId != null) CardTiles.GetRender(h.CardId);
                            foreach (var sec in rp.AdviceAt(shell.ReplayFrame)) foreach (var l in sec.Lines) if (l.CardId != null) CardTiles.Get(l.CardId);
                            v = -1;
                            for (var i = 0; i < 40 && v != CardTiles.Version; i++) { v = CardTiles.Version; Thread.Sleep(500); }
                        }
                    }

                    // Decks: the deck with the most recorded games, so its versions show.
                    var played = data.Decks.Select((dk, i) => (i, n: data.Games.Count(g => DeckVersions.DeckOf(g, data.Decks, data.Cards) == dk))).OrderByDescending(x => x.n).FirstOrDefault();
                    if (played.n > 0) shell.SelectedDeck = played.i;
                    // Every page, plus the Play page's game button in each of its states.
                    var shots = AppShell.Pages.Select(pg => (Page: pg, Name: pg.ToLowerInvariant(), State: (GameButton.State?)null, H: 800)).ToList();
                    shots.Add(("Play", "play-launch", GameButton.State.Closed, 800));
                    shots.Add(("Play", "play-starting", GameButton.State.Starting, 800));
                    shots.Add(("Play", "play-running", GameButton.State.Running, 800));
                    // Whole pages, for what's below the fold.
                    shots.Add(("Stats", "stats-full", null, 1700));
                    shots.Add(("Ladder", "ladder-full", null, 1500));
                    shots.Add(("History", "history-full", null, 1500));
                    shots.Add(("Decks", "decks-full", null, 1500));
                    shots.Add(("Play", "play-full", null, 1500));
                    shots.Add(("Settings", "settings-full", null, 2600));
                    shots.Add(("Replays", "replays-large", null, 1060));   // at the default window size on a big screen
                    // The replay mid-animation: an attack, and a card being played.
                    var rg = rgames.FirstOrDefault();
                    var rpl = rg != null ? ReplayPage.Get(rg, data) : null;
                    var midFrame = shell.ReplayFrame;
                    var steps = new List<(string Name, int Frame, double T)>();
                    if (rpl != null)
                    {
                        var atk = rpl.Frames.FindIndex(midFrame, x => x.AttackerId != 0 && !x.TargetIsHero);
                        if (atk > 0) steps.Add(("replays-attack", atk, 0.45));
                        var play = rpl.Frames.FindIndex(midFrame, x => x.PlayedCardId != null && x.PlayedByYou);
                        if (play > 0) steps.Add(("replays-play", play, 0.5));
                        // Still frames with effects drawn over minions, to check they line up (T < 0: no animation).
                        var shield = rpl.Frames.FindIndex(1, x => x.You.Board.Concat(x.Them.Board).Any(m => m.DivineShield));
                        if (shield > 0) steps.Add(("replays-shield", shield, -1));
                        var frozen = rpl.Frames.FindIndex(1, x => x.You.Board.Concat(x.Them.Board).Any(m => m.Frozen));
                        if (frozen > 0) steps.Add(("replays-frozen", frozen, -1));
                        // "Show hidden cards" on: the opponent's revealed cards must match the card backs' size.
                        var hidden = rpl.Frames.FindIndex(midFrame, x => x.Them.Hand.Count >= 4 && x.Them.Hand.Any(h => h.CardId == null && rpl.FinalCardIds.ContainsKey(h.EntityId)));
                        if (hidden > 0) steps.Add(("replays-hidden", hidden, -1));
                        foreach (var st in steps)
                        {
                            var fr = rpl.Frames[st.Frame];
                            var pf = rpl.Frames[st.Frame - 1];
                            var ids = new[] { fr.You, fr.Them, pf.You, pf.Them }.SelectMany(sd => sd.Board.Select(m => m.CardId).Append(sd.HeroCardId).Append(sd.HeroPowerCardId).Append(sd.WeaponCardId).Concat(sd.Secrets)).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                            ArtCache.Prefetch(ids, TimeSpan.FromSeconds(30));
                            foreach (var h in fr.You.Hand) if (h.CardId != null) CardTiles.GetRender(h.CardId);
                            foreach (var h in fr.Them.Hand) if (rpl.FinalCardIds.TryGetValue(h.EntityId, out var fid)) CardTiles.GetRender(fid);
                            if (fr.PlayedCardId != null) CardTiles.GetRender(fr.PlayedCardId);
                        }
                        ArtCache.Prefetch(rpl.Frames.Where(x => x.PlayedCardId != null).Select(x => x.PlayedCardId).Distinct(), TimeSpan.FromSeconds(30));
                        v = -1;
                        for (var i = 0; i < 40 && v != CardTiles.Version; i++) { v = CardTiles.Version; Thread.Sleep(500); }
                    }
                    ReplayBoard.Animate = false;
                    foreach (var (page, name, state, hgt) in shots.Concat(steps.Select(st => ("Replays", st.Name, (GameButton.State?)null, 800))))
                    {
                        var step = steps.FirstOrDefault(st => st.Name == name);
                        ReplayBoard.FixedProgress = null;
                        // Switching pages draws the page twice; show it first so the step is the only draw that animates.
                        if (step.Name != null)
                        {
                            shell.ReplayFrame = step.Frame; shell.Show("Replays");
                            if (step.T >= 0) { ReplayPage.PrimeStep(rg, step.Frame); ReplayBoard.FixedProgress = step.T; }
                        }
                        else if (page == "Replays") shell.ReplayFrame = midFrame;
                        shell.ReplayShowHidden = name == "replays-hidden";
                        GameButton.Override = state;
                        shell.Height = hgt;
                        shell.Show(page);
                        var wid = name == "replays-large" ? 1640 : 1280;
                        shell.Width = wid;
                        shell.Measure(new Size(wid, hgt));
                        shell.Arrange(new Rect(0, 0, wid, hgt));
                        shell.UpdateLayout();
                        var bmp = new RenderTargetBitmap(wid, hgt, 96, 96, PixelFormats.Pbgra32);
                        bmp.Render(shell);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        var file = Path.Combine(folder, "app-" + name + ".png");
                        using (var f = File.Create(file)) enc.Save(f);
                        Console.WriteLine("Wrote " + Path.GetFullPath(file));
                    }
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw error;
            return 0;
        }
    }
}
