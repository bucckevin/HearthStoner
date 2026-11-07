using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;

namespace HearthStoner
{
    /// <summary>
    /// The app's frame: the navigation rail on the left (with your name and rank at the bottom) and the current
    /// page on the right. Pages are rebuilt from <see cref="AppData"/> whenever the data or the page changes.
    /// </summary>
    public sealed class AppShell : Grid
    {
        public static readonly string[] Pages = { "Play", "Decks", "Ladder", "History", "Replays", "Practice", "Stats", "Meta", "Settings" };

        private readonly Border _content = new Border();
        private readonly Dictionary<string, RadioButton> _nav = new Dictionary<string, RadioButton>();
        private readonly TextBlock _name = Theme.Text("", 15, Theme.Vellum, FontWeights.SemiBold);
        private readonly TextBlock _rank = Theme.Text("", 13, Theme.Gold, FontWeights.SemiBold);
        private readonly TextBlock _metaLine = Theme.Text("", 12.5, Theme.Dusk);
        private readonly TextBlock _season = Theme.Text("", 12.5, Theme.Vellum);

        public AppData Data { get; private set; }
        public string Current { get; private set; } = "Play";
        /// <summary>Selected item on pages with a list (deck index, game index).</summary>
        public int SelectedDeck, SelectedGame;
        /// <summary>Replays page: which game (index into the replayable games, newest first), which frame, hidden cards shown.</summary>
        public int ReplayGame, ReplayFrame;
        public bool ReplayShowHidden;
        /// <summary>Called when a page asks for fresh data (Refresh, collection read, settings change).</summary>
        public Action ReloadRequested;
        public Action<string> Status;

        public AppShell()
        {
            Background = Theme.Night;
            TextElement.SetFontFamily(this, Theme.Body);
            TextElement.SetForeground(this, Theme.Vellum);
            Resources.MergedDictionaries.Add(Theme.Styles);
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rail = new DockPanel { Background = Theme.Rail, LastChildFill = false };
            var railBorder = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = rail };
            var mark = new TextBlock { FontFamily = Theme.Display, FontWeight = FontWeights.Bold, FontSize = 25, Margin = new Thickness(22, 20, 0, 24) };
            mark.Inlines.Add(new System.Windows.Documents.Run("Hearth") { Foreground = Theme.Vellum });
            mark.Inlines.Add(new System.Windows.Documents.Run("Stoner") { Foreground = Theme.Mana });
            DockPanel.SetDock(mark, Dock.Top);
            rail.Children.Add(mark);
            foreach (var p in Pages)
            {
                var rb = new RadioButton { Content = p, GroupName = "nav", Style = (Style)Theme.Styles["Nav"], IsChecked = p == Current };
                var page = p;
                rb.Checked += (s, e) => Show(page);
                DockPanel.SetDock(rb, Dock.Top);
                rail.Children.Add(rb);
                _nav[p] = rb;
            }
            var who = new StackPanel { Margin = new Thickness(22, 14, 16, 18) };
            who.Children.Add(_name);
            who.Children.Add(_rank);
            _season.Margin = new Thickness(0, 2, 0, 0);
            who.Children.Add(_season);
            _metaLine.Margin = new Thickness(0, 2, 0, 0);
            who.Children.Add(_metaLine);
            var whoBorder = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = who };
            DockPanel.SetDock(whoBorder, Dock.Bottom);
            rail.Children.Add(whoBorder);
            SetColumn(railBorder, 0);
            Children.Add(railBorder);

            SetColumn(_content, 1);
            _content.Margin = new Thickness(0, 8, 0, 0);   // room for the window buttons above the pages
            Children.Add(_content);
            var buttons = WindowButtons();
            SetColumn(buttons, 1);
            Children.Add(buttons);
            _content.Child = Loading();
        }

        /// <summary>
        /// Minimize, maximize and close, drawn in the app's own colours: the window has no Windows title bar (see
        /// MainWindow), so the top strip of the app is where you drag it from and these replace the usual buttons.
        /// </summary>
        private UIElement WindowButtons()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            Button B(string glyph, string tip, Action<Window> act, bool close = false)
            {
                var b = new Button
                {
                    Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    Width = 40, Height = 28, ToolTip = tip, Focusable = false, Cursor = Cursors.Arrow,
                    Template = ChromeButtonTemplate(close ? Theme.Rgb(0xE8, 0x11, 0x23) : Theme.Rgb(0x3A, 0x42, 0x72)),
                };
                b.Click += (s, e) => { var w = Window.GetWindow(this); if (w != null) act(w); };
                System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                row.Children.Add(b);
                return b;
            }
            B("\uE921", "Minimize", w => SystemCommands.MinimizeWindow(w));
            var max = B("\uE922", "Maximize", w => { if (w.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(w); else SystemCommands.MaximizeWindow(w); });
            B("\uE8BB", "Close", w => SystemCommands.CloseWindow(w), close: true);
            // Show "restore" instead of "maximize" while maximized.
            Loaded += (s, e) =>
            {
                var w = Window.GetWindow(this);
                if (w == null) return;
                void Sync() { ((TextBlock)max.Content).Text = w.WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; max.ToolTip = w.WindowState == WindowState.Maximized ? "Restore" : "Maximize"; }
                w.StateChanged += (s2, e2) => Sync();
                Sync();
            };
            return row;
        }

        /// <summary>A flat button with a white icon that lights up on hover (red for close, like Windows).</summary>
        private static ControlTemplate ChromeButtonTemplate(Color hover)
        {
            var t = new ControlTemplate(typeof(Button));
            var bg = new FrameworkElementFactory(typeof(Border), "bg");
            bg.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bg.AppendChild(cp);
            t.VisualTree = bg;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, Theme.B(hover), "bg"));
            t.Triggers.Add(over);
            return t;
        }

        private static UIElement Loading()
        {
            var t = Theme.Text("Loading your decks and games...", 15, Theme.Dusk);
            t.Margin = new Thickness(34, 30, 0, 0);
            return t;
        }

        public void SetData(AppData data)
        {
            Data = data;
            _name.Text = data.PlayerName ?? "";
            _name.Visibility = string.IsNullOrEmpty(data.PlayerName) ? Visibility.Collapsed : Visibility.Visible;
            _rank.Text = AppData.PrettyRank(data.Rank) ?? "";
            _rank.Visibility = string.IsNullOrEmpty(_rank.Text) ? Visibility.Collapsed : Visibility.Visible;
            var end = data.Quests?.SeasonEnd;
            _season.Text = QuestLog.SeasonEndsText(end, DateTime.UtcNow) ?? "";
            _season.ToolTip = end is DateTime e ? "Ladder resets " + e.ToLocalTime().ToString("ddd MMM d, h:mm tt") : null;
            _season.Visibility = _season.Text == "" ? Visibility.Collapsed : Visibility.Visible;
            _metaLine.Text = data.Meta != null ? $"Meta: {AppData.RankBracketName(data.Meta.Rank)}, {AppData.PeriodName(data.Meta.Period)}" : "";
            Show(Current);
        }

        /// <summary>Switch to a page (and rebuild it from the current data).</summary>
        public void Show(string page)
        {
            Current = page;
            if (_nav.TryGetValue(page, out var rb) && rb.IsChecked != true) rb.IsChecked = true;
            if (Data == null) { _content.Child = Loading(); return; }
            UIElement el;
            try
            {
                if (Data.Error != null && page != "Settings") el = ErrorPage(Data.Error);
                else switch (page)
                {
                    case "Play": el = PlayPage.Build(this); break;
                    case "Ladder": el = LadderPage.Build(this); break;
                    case "Decks": el = DecksPage.Build(this); break;
                    case "History": el = HistoryPage.Build(this); break;
                    case "Replays": el = ReplayPage.Build(this); break;
                    case "Practice": el = PracticePage.Build(this); break;
                    case "Stats": el = StatsPage.Build(this); break;
                    case "Meta": el = MetaPage.Build(this); break;
                    default: el = SettingsPage.Build(this); break;
                }
            }
            catch (Exception ex) { el = ErrorPage(ex.Message); Data.Services?.Log?.Invoke("[app] " + page + ": " + ex); }
            _content.Child = el;
        }

        /// <summary>Rebuild the current page (e.g. when card art has downloaded).</summary>
        public void Redraw() => Show(Current);

        private static UIElement ErrorPage(string message)
        {
            var p = new StackPanel { Margin = new Thickness(34, 26, 34, 26) };
            p.Children.Add(Theme.Title("Something went wrong"));
            var t = Theme.Text($"HearthStoner couldn't load its data: {message}. Check your internet connection, then press Refresh on the Play page.", 14, Theme.Dusk, wrap: true);
            t.Margin = new Thickness(0, 8, 0, 0);
            t.MaxWidth = 640;
            t.HorizontalAlignment = HorizontalAlignment.Left;
            p.Children.Add(t);
            return p;
        }

        // ------------------------------------------------------------------ shared page parts

        /// <summary>A page with a title, a one-line description, optional actions on the right, and scrolling content.</summary>
        public static Grid Page(string title, string sub, UIElement body, params UIElement[] actions)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var head = new DockPanel { Margin = new Thickness(34, 24, 34, 18), LastChildFill = true };
            if (actions.Length > 0)
            {
                var act = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
                foreach (var a in actions) { if (a is FrameworkElement fe) fe.Margin = new Thickness(8, 0, 0, 0); act.Children.Add(a); }
                DockPanel.SetDock(act, Dock.Right);
                head.Children.Add(act);
            }
            var titles = new StackPanel();
            titles.Children.Add(Theme.Title(title));
            if (!string.IsNullOrEmpty(sub)) { var s = Theme.Text(sub, 14, Theme.Dusk); s.Margin = new Thickness(0, 4, 0, 0); titles.Children.Add(s); }
            head.Children.Add(titles);
            g.Children.Add(head);
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(34, 0, 26, 26), Focusable = false };
            SetRow(scroll, 1);
            g.Children.Add(scroll);
            return g;
        }

        /// <summary>Like <see cref="Page"/>, but the body fills the page's height instead of scrolling (the replay board).</summary>
        public static Grid PageFill(string title, string sub, UIElement body, params UIElement[] actions)
        {
            var g = Page(title, sub, new Grid(), actions);
            var scroll = g.Children.OfType<ScrollViewer>().First();
            g.Children.Remove(scroll);
            var holder = new Border { Child = body, Padding = new Thickness(34, 0, 26, 18) };
            SetRow(holder, 1);
            g.Children.Add(holder);
            return g;
        }

        /// <summary>Card artwork filling a box, anchored right, fading to <paramref name="into"/> on the left.</summary>
        public static Grid ArtBanner(string cardId, Color into, double artWidth, double opacity = 1.0, double fadeStart = 0.0, double fadeEnd = 0.55)
        {
            var g = new Grid { ClipToBounds = true };
            var img = ArtCache.Get(cardId);
            if (img != null)
            {
                g.Children.Add(new Border
                {
                    Width = artWidth, HorizontalAlignment = HorizontalAlignment.Right, Opacity = opacity,
                    Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top, Viewbox = new Rect(0, 0.12, 1, 0.76) },
                });
                g.Children.Add(new Border
                {
                    Width = artWidth, HorizontalAlignment = HorizontalAlignment.Right,
                    Background = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(255, into.R, into.G, into.B), fadeStart),
                        new GradientStop(Color.FromArgb(200, into.R, into.G, into.B), fadeStart + (fadeEnd - fadeStart) * 0.4),
                        new GradientStop(Color.FromArgb(0, into.R, into.G, into.B), fadeEnd),
                    }, new Point(0, 0), new Point(1, 0)),
                });
            }
            return g;
        }

        /// <summary>A small rounded chip like "vs Priest 66%".</summary>
        public static Border Chip(string label, string value, Brush valueBrush)
        {
            var t = new TextBlock { FontSize = 12.5, Foreground = Theme.Vellum, FontFamily = Theme.Body };
            t.Inlines.Add(new System.Windows.Documents.Run(label + " "));
            t.Inlines.Add(new System.Windows.Documents.Run(value) { Foreground = valueBrush, FontWeight = FontWeights.SemiBold });
            return new Border
            {
                Child = t, CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 8, 0),
                Background = Theme.B(Theme.NightC, 0.75), BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
            };
        }

        public static string Pct(double v, int decimals = 0) => (v * 100).ToString(decimals == 0 ? "0" : "0.0") + "%";
    }
}
