using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HearthStoner.Pages
{
    /// <summary>
    /// Replays: step through a recorded game without Hearthstone. The board in the middle (heroes, minions, hands,
    /// mana, secrets), what just happened and what HearthStoner advised that turn on the right, and a timeline you can
    /// click, with turns, the turning point and review notes marked. Keys: Left/Right step, Up/Down jump a turn,
    /// Space plays. "Show hidden cards" reveals opponent cards that the game showed later.
    /// </summary>
    public static class ReplayPage
    {
        private static readonly Dictionary<string, Replay> Cache = new Dictionary<string, Replay>();
        private static readonly HashSet<string> Building = new HashSet<string>();

        /// <summary>Makes the next draw of <paramref name="frame"/> animate as a step from the frame before (screenshots).</summary>
        public static void PrimeStep(AppShell shell, GameRecord g, int frame) { shell.ReplayView.ShownGame = g.LogFile; shell.ReplayView.ShownFrame = frame - 1; }

        /// <summary>Games that can be replayed (they have their log), newest first.</summary>
        public static List<GameRecord> Replayable(AppData d) => d.Games.Where(g => !string.IsNullOrEmpty(g.LogFile)).ToList();

        /// <summary>Builds (or returns) the replay of a game. Slow-ish (it re-runs the advisor each turn), so run off the UI thread.</summary>
        public static Replay Get(GameRecord g, AppData d)
        {
            lock (Cache) if (Cache.TryGetValue(g.LogFile, out var r)) return r;
            var lines = GameLog.Load(g);
            if (lines == null) return null;
            var replay = Replay.Build(lines, d.Cards, d.Meta, g.DeckCards?.Count > 0 ? g.DeckCards : null, g.DeckName, d.PlayerName);
            lock (Cache) Cache[g.LogFile] = replay;
            return replay;
        }

        private static readonly Dictionary<string, (WinChance Chance, List<ReviewNote> Review)> Analysis = new Dictionary<string, (WinChance, List<ReviewNote>)>();

        /// <summary>The replay if it's built; otherwise starts building it and redraws the page when done.</summary>
        public static Replay TryGet(GameRecord game, AppShell shell)
        {
            if (string.IsNullOrEmpty(game?.LogFile)) return null;
            var d = shell.Data;
            Replay replay;
            lock (Cache) Cache.TryGetValue(game.LogFile, out replay);
            if (replay != null) return replay;
            lock (Building)
                if (Building.Add(game.LogFile))
                    Task.Run(() =>
                    {
                        try { Get(game, d); }
                        catch (Exception ex) { d.Services?.Log?.Invoke("[replay] " + ex.Message); }
                        finally { lock (Building) Building.Remove(game.LogFile); }
                        shell.Dispatcher.Invoke(shell.Redraw);
                    });
            return null;
        }

        /// <summary>The game's win-chance line and the review worked out from its replay (cached).</summary>
        public static (WinChance Chance, List<ReviewNote> Review) Analyze(GameRecord g, Replay r, AppData d)
        {
            lock (Analysis) if (Analysis.TryGetValue(g.LogFile, out var a)) return a;
            var chance = WinChance.For(r, WinChance.MatchupPrior(g, d.Meta, d.Cards));
            List<ReviewNote> notes;
            try { notes = GameReview.Merge(g, GameReview.Analyze(r, d.Cards, chance)); }
            catch (Exception ex) { d.Services?.Log?.Invoke("[review] " + ex.Message); notes = g.Review.ToList(); }
            var res = (chance, notes);
            lock (Analysis) Analysis[g.LogFile] = res;
            return res;
        }

        /// <summary>The replay frame a review note points at.</summary>
        public static int FrameOf(ReviewNote n, Replay r) =>
            n.Frame >= 0 && n.Frame < r.Frames.Count ? n.Frame : r.TurnStarts.TryGetValue(n.Turn, out var i) ? i : 0;

        /// <summary>
        /// The advice sections in cards: each main section (the opponent, you, their deck, the board) starts a card
        /// and its small sub-sections (still in their deck, hand) stay in it, so the long column reads in groups.
        /// </summary>
        private static UIElement AdviceCards(IEnumerable<AdviceSection> sections)
        {
            var p = new StackPanel();
            StackPanel card = null;
            AdviceSection previous = null;
            foreach (var sec in sections)
            {
                // A sub-section about the other player ("their deck" after your own header) starts its own card too.
                if (card == null || !sec.Minor || sec.Opponent != previous?.Opponent)
                {
                    card = new StackPanel();
                    p.Children.Add(new Border
                    {
                        Child = card, Background = Theme.B(Theme.SlateC, 0.7), BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 10), Margin = new Thickness(0, 0, 0, 8),
                    });
                }
                SectionView.Fill(card, new[] { sec }, true, true, 14);
                previous = sec;
            }
            return p;
        }

        public static UIElement Build(AppShell shell)
        {
            var d = shell.Data;
            var games = Replayable(d);
            if (games.Count == 0)
            {
                var p = new StackPanel();
                p.Children.Add(PlayPage.Empty("No replays yet", "Every game you play with HearthStoner running can be replayed here. Games still in Hearthstone's log folders are added when the app starts."));
                return AppShell.Page("Replays", "Step through your games without the game.", p);
            }
            if (shell.ReplayGame >= games.Count) shell.ReplayGame = 0;
            var game = games[shell.ReplayGame];
            var replay = TryGet(game, shell);
            if (replay == null)
            {
                var loading = new StackPanel();
                loading.Children.Add(Theme.Text("Rebuilding the game from its log...", 15, Theme.Dusk));
                return AppShell.Page(Title(game), Subtitle(game), loading, GameSwitcher(shell, games));
            }
            if (shell.ReplayFrame < 0 || shell.ReplayFrame >= replay.Frames.Count) shell.ReplayFrame = 0;
            return Viewer(shell, game, games, replay);
        }

        private static string Title(GameRecord g) =>
            $"{(g.Won ? "Win" : g.Result == "LOST" ? "Loss" : "Game")} vs {g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass)}";

        private static string Subtitle(GameRecord g) =>
            $"{g.DeckName ?? MetaStats.ClassName(g.MyClass)}, {g.Start:MMM d, h:mm tt}, {g.TurnsText}";

        private static UIElement GameSwitcher(AppShell shell, List<GameRecord> games)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var newer = Theme.Button("Newer game", () => { shell.ReplayGame--; shell.ReplayFrame = 0; shell.Redraw(); }, primary: false);
            newer.IsEnabled = shell.ReplayGame > 0;
            var older = Theme.Button("Older game", () => { shell.ReplayGame++; shell.ReplayFrame = 0; shell.Redraw(); }, primary: false);
            older.IsEnabled = shell.ReplayGame < games.Count - 1;
            older.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(newer);
            row.Children.Add(older);
            return row;
        }

        // ------------------------------------------------------------------ the viewer

        private static UIElement Viewer(AppShell shell, GameRecord game, List<GameRecord> games, Replay r)
        {
            var d = shell.Data;
            var f = r.Frames[shell.ReplayFrame];
            void Go(int frame) { shell.ReplayFrame = Math.Max(0, Math.Min(r.Frames.Count - 1, frame)); shell.Redraw(); }
            int TurnStart(int turn) => r.TurnStarts.TryGetValue(turn, out var i) ? i : -1;
            void Turn(int delta)
            {
                var target = f.Turn + delta;
                while (target > 0 && target <= r.Turns + 1 && TurnStart(target) < 0) target += Math.Sign(delta);
                Go(delta < 0 && TurnStart(f.Turn) >= 0 && shell.ReplayFrame > TurnStart(f.Turn) ? TurnStart(f.Turn) : TurnStart(target) >= 0 ? TurnStart(target) : (delta < 0 ? 0 : r.Frames.Count - 1));
            }

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // the board takes the room there is
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Board.
            // Stepping forward one action animates it; jumps and redraws don't.
            var prev = shell.ReplayView.ShownGame == game.LogFile && shell.ReplayView.ShownFrame == shell.ReplayFrame - 1 && shell.ReplayFrame > 0 ? r.Frames[shell.ReplayFrame - 1] : null;
            shell.ReplayView.ShownGame = game.LogFile; shell.ReplayView.ShownFrame = shell.ReplayFrame;
            var board = ReplayBoard.Build(r, shell.ReplayFrame, prev, shell.ReplayShowHidden, d);
            body.Children.Add(board);

            // Controls + timeline under the board.
            var controls = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var buttons = new WrapPanel();   // in a narrow window the position text wraps instead of being cut off
            Button B(string text, Action a, string tip, bool primary = false) { var b = Theme.Button(text, a, primary); b.ToolTip = tip; b.Margin = new Thickness(0, 0, 6, 0); b.MinWidth = 42; return b; }
            buttons.Children.Add(B("⏮", () => Turn(-1), "Previous turn (Up)"));
            buttons.Children.Add(B("◀", () => Go(shell.ReplayFrame - 1), "Step back (Left)"));
            var playing = shell.ReplayView.Playing;
            buttons.Children.Add(B(playing ? "Pause" : "Play", () => TogglePlay(shell, r), "Play the game forward (Space)", primary: true));
            buttons.Children.Add(B("▶", () => Go(shell.ReplayFrame + 1), "Step forward (Right)"));
            buttons.Children.Add(B("⏭", () => Turn(+1), "Next turn (Down)"));
            var hidden = new CheckBox { IsChecked = shell.ReplayShowHidden, Style = (Style)Theme.Styles["Toggle"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
            hidden.Checked += (s, e) => { shell.ReplayShowHidden = true; shell.Redraw(); };
            hidden.Unchecked += (s, e) => { shell.ReplayShowHidden = false; shell.Redraw(); };
            buttons.Children.Add(hidden);
            var hl = Theme.Text("Show hidden cards", 13.5, Theme.Dusk);
            hl.VerticalAlignment = VerticalAlignment.Center;
            hl.ToolTip = "Reveal opponent cards the game showed later (what they were holding)";
            buttons.Children.Add(hl);
            var analysis = Analyze(game, r, d);
            var chanceNow = analysis.Chance.At(shell.ReplayFrame);
            var pos = new TextBlock { FontSize = 13, Foreground = Theme.Dusk };
            pos.Inlines.Add(new System.Windows.Documents.Run($"Turn {f.Round}, {(f.Mulligan ? "mulligan" : f.YourTurn ? "your turn" : "their turn")}   "));
            pos.Inlines.Add(new System.Windows.Documents.Run(AppShell.Pct(chanceNow)) { Foreground = chanceNow >= 0.5 ? Theme.Win : Theme.Loss, FontWeight = FontWeights.SemiBold });
            pos.Inlines.Add(new System.Windows.Documents.Run($" to win   {shell.ReplayFrame + 1} / {r.Frames.Count}"));
            pos.ToolTip = "HearthStoner's estimate of your win chance from health, boards, hands, mana and the matchup. Not a simulation: a guide to where the game turned.";
            pos.VerticalAlignment = VerticalAlignment.Center;
            pos.Margin = new Thickness(16, 0, 0, 0);
            buttons.Children.Add(pos);
            controls.Children.Add(buttons);
            controls.Children.Add(ChanceStrip(shell, r, analysis.Chance, Go));
            controls.Children.Add(Timeline(shell, game, r, analysis.Review, Go));
            Grid.SetRow(controls, 1);
            body.Children.Add(controls);

            // Right: tabs, then what happened and the advice at the start of this turn (Now), their deck, or the board.
            var side = new StackPanel();
            var sections = r.AdviceAt(shell.ReplayFrame).Where(s => !s.Lower || s.Opponent).ToList();   // your own deck list: not what the replay is about
            var tab = SideTabs.Contains(shell.ReplayTab) ? shell.ReplayTab : SideTabs[0];
            var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var t in SideTabs)
            {
                var n = sections.Count(s => TabOf(s) == t);
                tabs.Children.Add(AppShell.FilterChip(t, t == tab, () => { shell.ReplayTab = t; shell.Redraw(); },
                    t == "Now" ? "What just happened, health and this turn's advice" : n == 0 ? "Nothing here at this point of the game" : null));
            }
            side.Children.Add(tabs);
            var actionBox = new Border
            {
                CornerRadius = new CornerRadius(10), Background = Theme.Slate, Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 14),
                Child = new StackPanel(),
            };
            var ap = (StackPanel)actionBox.Child;
            ap.Children.Add(Theme.Text(f.Action ?? "", 17, Theme.Vellum, FontWeights.SemiBold, display: true, wrap: true));
            foreach (var ev in f.Events.Where(e => e != f.Action && !e.StartsWith("---")).Take(5))
            {
                var t = Theme.Text(ev, 12.5, Theme.Dusk, wrap: true);
                t.Margin = new Thickness(0, 3, 0, 0);
                ap.Children.Add(t);
            }
            if (tab == "Now") side.Children.Add(actionBox);
            // Turn, whose turn and the win chance at the top, so you don't have to look down at the controls.
            var adviceTitle = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Dusk, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            adviceTitle.Inlines.Add(new System.Windows.Documents.Run($"START OF TURN {f.Round}  ·  {(f.Mulligan ? "MULLIGAN" : f.YourTurn ? "YOUR TURN" : "THEIR TURN")}  ·  "));
            adviceTitle.Inlines.Add(new System.Windows.Documents.Run(AppShell.Pct(chanceNow)) { Foreground = chanceNow >= 0.5 ? Theme.Win : Theme.Loss });
            adviceTitle.Inlines.Add(new System.Windows.Documents.Run(" TO WIN"));
            side.Children.Add(adviceTitle);
            var shown = sections.Where(s => TabOf(s) == tab).ToList();
            if (shown.Count > 0) side.Children.Add(AdviceCards(shown));
            else side.Children.Add(Theme.Text(tab == "Board" ? "No minions on the board yet." : "Nothing known about their deck yet.", 13, Theme.Dusk, wrap: true));
            var sideScroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            // The side panel's text is drawn at the overlay's glance-in-game sizes; here you sit back and read it,
            // so it's scaled up a little, and more on a wide screen where the column is wider too.
            var (sideWidth, sideScale) = shell.Size switch
            {
                PageWidth.Wide => (400.0, 1.2),
                PageWidth.Compact => (280.0, 1.0),
                _ => (330.0, 1.1),
            };
            body.ColumnDefinitions[2].Width = new GridLength(sideWidth);
            side.LayoutTransform = new ScaleTransform(sideScale, sideScale);
            Grid.SetColumn(sideScroll, 2);
            Grid.SetRowSpan(sideScroll, 2);
            body.Children.Add(sideScroll);

            // Very wide screens: every action of the game down the left, grouped by turn, next to the board.
            if (shell.Size == PageWidth.Wide && shell.PageAreaWidth >= PageLayout.ExtraWideFrom)
            {
                body.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = new GridLength(LogWidth) });
                body.ColumnDefinitions.Insert(1, new ColumnDefinition { Width = new GridLength(22) });
                foreach (UIElement child in body.Children) Grid.SetColumn(child, Grid.GetColumn(child) + 2);
                var log = TurnLog(r, shell.ReplayFrame, Go);
                Grid.SetColumn(log, 0);
                Grid.SetRowSpan(log, 2);
                body.Children.Add(log);
            }

            var page = AppShell.PageFill(Title(game), Subtitle(game), body, GameSwitcher(shell, games));
            page.Focusable = true;
            page.FocusVisualStyle = null;
            page.Loaded += (s, e) => page.Focus();
            page.PreviewKeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case Key.Left: Go(shell.ReplayFrame - 1); e.Handled = true; break;
                    case Key.Right: Go(shell.ReplayFrame + 1); e.Handled = true; break;
                    case Key.Up: Turn(-1); e.Handled = true; break;
                    case Key.Down: Turn(+1); e.Handled = true; break;
                    case Key.Space: TogglePlay(shell, r); e.Handled = true; break;
                }
            };
            return page;
        }

        /// <summary>The side panel's tabs, in order.</summary>
        private static readonly string[] SideTabs = { "Now", "Their deck", "Board" };

        /// <summary>Which tab an advice section goes under: the board, the opponent's deck (their likely deck and cards left), or now.</summary>
        private static string TabOf(AdviceSection s) =>
            s.Title == "Board" ? "Board"
            : s.Opponent && (s.Lower || (s.Title ?? "").StartsWith("Their deck", StringComparison.Ordinal)) ? "Their deck"
            : "Now";

        /// <summary>The turn log's width on a very wide screen.</summary>
        private const double LogWidth = 280;

        /// <summary>
        /// Every action of the game, grouped under its turn, the current one highlighted and scrolled into view; clicking
        /// one jumps there.
        /// </summary>
        private static UIElement TurnLog(Replay r, int current, Action<int> go)
        {
            var p = new StackPanel();
            var head = Theme.Heading("Turn by turn");
            head.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(head);
            var lastTurn = int.MinValue;
            FrameworkElement now = null;
            for (var i = 0; i < r.Frames.Count; i++)
            {
                var f = r.Frames[i];
                if (string.IsNullOrEmpty(f.Action) || f.Action.StartsWith("---", StringComparison.Ordinal)) continue;
                if (f.Turn != lastTurn)
                {
                    lastTurn = f.Turn;
                    var th = AppShell.ColumnLabel(f.Mulligan ? "Mulligan" : $"Turn {f.Round} · {(f.YourTurn ? "you" : "them")}");
                    th.Margin = new Thickness(0, 10, 0, 3);
                    th.Foreground = f.YourTurn ? Theme.Mana : Theme.Dusk;
                    p.Children.Add(th);
                }
                var on = i == current;
                var t = Theme.Text(f.Action, 12.5, on ? Theme.Vellum : Theme.Mist, on ? FontWeights.SemiBold : FontWeights.Normal, wrap: true);
                var row = new Border
                {
                    Child = t, Padding = new Thickness(8, 3, 6, 3), Cursor = Cursors.Hand, ToolTip = "Go to this moment",
                    Background = on ? Theme.B(Theme.ManaC, 0.14) : Brushes.Transparent,
                    BorderBrush = on ? Theme.Mana : Brushes.Transparent, BorderThickness = new Thickness(2, 0, 0, 0),
                };
                var frame = i;
                row.MouseLeftButtonUp += (s, e) => go(frame);
                p.Children.Add(row);
                if (on) now = row;
            }
            // Scroll the current action into view when the log opens.
            if (now != null) now.Loaded += (s, e) => now.BringIntoView();
            return new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        }

        private static void TogglePlay(AppShell shell, Replay r)
        {
            if (shell.ReplayView.Playing) { shell.ReplayView.Player.Stop(); shell.Redraw(); return; }
            shell.ReplayView.Player = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            shell.ReplayView.Player.Tick += (s, e) =>
            {
                if (shell.Current != "Replays" || shell.ReplayFrame >= r.Frames.Count - 1) { shell.ReplayView.Player.Stop(); shell.Redraw(); return; }
                shell.ReplayFrame++;
                shell.Redraw();
            };
            shell.ReplayView.Player.Start();
            shell.Redraw();
        }

        // ------------------------------------------------------------------ timeline

        /// <summary>Your win chance through the game, lined up with the timeline under it; click to jump.</summary>
        private static UIElement ChanceStrip(AppShell shell, Replay r, WinChance wc, Action<int> go)
        {
            var c = new Canvas { Height = 30, Margin = new Thickness(0, 10, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ClipToBounds = true,
                ToolTip = "Your win chance through the game (HearthStoner's estimate). Red dots: the biggest drops." };
            c.SizeChanged += (s, e) => WinChanceView.Draw(c, wc, shell.ReplayFrame, null, go);
            c.MouseLeftButtonDown += (s, e) => go((int)Math.Round(e.GetPosition(c).X / Math.Max(1, c.ActualWidth) * (r.Frames.Count - 1)));
            return c;
        }

        private static UIElement Timeline(AppShell shell, GameRecord g, Replay r, List<ReviewNote> review, Action<int> go)
        {
            const double h = 46;
            var c = new Canvas { Height = h, Margin = new Thickness(0, 4, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ClipToBounds = false };
            c.SizeChanged += (s, e) => Draw();
            c.MouseLeftButtonDown += (s, e) =>
            {
                var w = Math.Max(1, c.ActualWidth);
                go((int)Math.Round(e.GetPosition(c).X / w * (r.Frames.Count - 1)));
            };
            void Draw()
            {
                c.Children.Clear();
                var w = c.ActualWidth;
                if (w <= 0) return;
                double X(int frame) => r.Frames.Count <= 1 ? 0 : frame * w / (r.Frames.Count - 1);
                c.Children.Add(Pos(new Rectangle { Width = w, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Theme.Raised }, 0, 14));
                // Turn shading: your turns slightly lighter.
                foreach (var kv in r.TurnStarts.OrderBy(k => k.Key))
                {
                    var start = kv.Value;
                    var end = r.TurnStarts.Where(x => x.Key > kv.Key).Select(x => x.Value).DefaultIfEmpty(r.Frames.Count - 1).Min();
                    if (start < r.Frames.Count && r.Frames[Math.Min(start, r.Frames.Count - 1)].YourTurn)
                        c.Children.Add(Pos(new Rectangle { Width = Math.Max(1, X(end) - X(start)), Height = 6, Fill = Theme.B(Theme.ManaC, 0.35) }, X(start), 14));
                    c.Children.Add(Pos(new Rectangle { Width = 1, Height = 12, Fill = Theme.Line }, X(start), 11));
                    var round = (kv.Key + 1) / 2;
                    if (kv.Key % 2 == 1 || r.TurnStarts.Count < 14)
                    {
                        var t = Theme.Text(round.ToString(), 11, Theme.Dusk);
                        if (r.Frames[Math.Min(start, r.Frames.Count - 1)].YourTurn || r.TurnStarts.Count >= 14) c.Children.Add(Pos(t, X(start) + 2, 26));
                    }
                }
                // Review notes above the bar (red: major); the win-chance drops are on the strip above.
                foreach (var n in review.Where(n => n.Turn > 0 && n.Kind != "Swing"))
                {
                    var fi = FrameOf(n, r);
                    var dot = Pos(new Ellipse { Width = 9, Height = 9, Fill = n.Severity >= 3 ? Theme.Loss : Theme.Amber, ToolTip = n.Text }, X(fi) - 4.5, 0);
                    dot.MouseLeftButtonDown += (s, e) => { e.Handled = true; go(fi); };
                    c.Children.Add(dot);
                }
                // Current position.
                var x = X(shell.ReplayFrame);
                c.Children.Add(Pos(new Rectangle { Width = 3, Height = 20, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.Vellum }, x - 1.5, 7));
            }
            return c;
        }

        private static T Pos<T>(T el, double x, double y) where T : UIElement { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); return el; }

        public static string BaseHero(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(id, @"^HERO_\d+");
            return m.Success ? m.Value : id;
        }
    }
}
