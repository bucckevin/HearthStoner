using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HearthStoner.Pages
{
    /// <summary>
    /// Stats: your record, recent form, results by matchup and by deck, the best time to play, then habits and tilt
    /// notes. Ranked and casual games by default (the ladder the advice is about), or every card mode.
    /// </summary>
    public static class StatsPage
    {
        public static UIElement Build(AppShell shell)
        {
            var d = shell.Data;
            var p = new StackPanel();
            if (d.AllStats.Stats == null || d.AllStats.Stats.Games == 0)
            {
                p.Children.Add(PlayPage.Empty("No finished games yet", "Play with HearthStoner running and your record, matchups and habits build up here."));
                return AppShell.Page("Stats", "How you've been doing.", p);
            }
            var scope = Scope(shell);
            var view = scope == AppShell.StatsLadder ? d.LadderStats : d.AllStats;
            var s = view.Stats;
            p.Children.Add(ScopeChips(shell, scope));
            if (s == null || s.Games == 0)
            {
                p.Children.Add(Theme.Text("No finished ranked or casual games yet. \"All card modes\" shows your Arena, Tavern Brawl and solo games.", 13.5, Theme.Dusk, wrap: true));
                return AppShell.Page("Stats", "How you've been doing.", p);
            }

            // Headline numbers.
            var tiles = new Grid { Margin = new Thickness(0, 0, 0, 26) };
            for (var i = 0; i < 4; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Tile(int col, string value, string label, Brush color, string sub = null)
            {
                var t = new StackPanel();
                t.Children.Add(Theme.Text(value, 44, color, FontWeights.SemiBold, display: true));
                t.Children.Add(Theme.Text(label, 13, Theme.Dusk));
                if (sub != null) t.Children.Add(Theme.Text(sub, 12, Theme.Dusk));
                var b = new Border { Child = t, BorderBrush = Theme.Line, BorderThickness = new Thickness(col == 0 ? 0 : 1, 0, 0, 0), Padding = new Thickness(col == 0 ? 0 : 24, 0, 0, 0) };
                Grid.SetColumn(b, col);
                tiles.Children.Add(b);
            }
            Tile(0, $"{s.Wins}-{s.Losses}", "wins and losses", Theme.Vellum);
            Tile(1, AppShell.Pct(s.WinRate, 1), "win rate", Theme.RateBrush(s.WinRate));
            Tile(2, s.First.Games > 0 ? AppShell.Pct(s.First.WinRate) : "-", "going first", s.First.Games > 0 ? Theme.RateBrush(s.First.WinRate) : Theme.Dusk, $"{s.First.Wins}-{s.First.Losses}");
            Tile(3, s.Coin.Games > 0 ? AppShell.Pct(s.Coin.WinRate) : "-", "on the coin", s.Coin.Games > 0 ? Theme.RateBrush(s.Coin.WinRate) : Theme.Dusk, $"{s.Coin.Wins}-{s.Coin.Losses}");
            p.Children.Add(tiles);

            // Recent form: one square per game.
            var trend = WinTables.Rolling(view.Games, TrendWindow);
            if (trend.Count >= 2)
            {
                p.Children.Add(Theme.Heading($"Win rate over time"));
                p.Children.Add(TrendChart(trend));
            }
            p.Children.Add(Theme.Heading($"Last {s.Recent.Count} game{(s.Recent.Count == 1 ? "" : "s")}"));
            p.Children.Add(RecentForm(s, shell));

            if (view.Mana != null && view.Mana.Turns > 0) p.Children.Add(Mana(view.Mana));

            // Matchups, decks, and how early HearthStoner names the opponent's deck: three columns on a wide screen,
            // two (decks and the scorecard sharing one) on a normal one, one under the other in a small window.
            var byClass = Table("By opponent's class", s.ByClass, r => Theme.ClassColor(r.Class));
            // One deck: its table would repeat "By opponent's class", so that space shows first against coin instead.
            var byDeck = s.ByDeck.Count >= 2 ? Table("By deck", s.ByDeck, r => Theme.ClassColor(r.Class)) : FirstOrCoin(view.Games);
            var score = Scorecard(PredictionScore.Compute(view.Games));
            switch (shell.Size)
            {
                case PageWidth.Wide:
                    p.Children.Add(PageLayout.Columns((byClass, 0), (byDeck, 0), (score, 0)));
                    break;
                case PageWidth.Compact:
                    p.Children.Add(PageLayout.Stack(26, byClass, byDeck, score));
                    break;
                default:
                    p.Children.Add(PageLayout.Columns((byClass, 0), (PageLayout.Stack(26, byDeck, score), 0)));
                    break;
            }

            if (s.ByDeck.Count >= 2) p.Children.Add(DeckGrid(view.Games));

            // When you win most: ranked and casual games by the hour they started (moved here from the Ladder page,
            // where it repeated under each format).
            var (times, best) = LadderReport.ByTimeOfDay(d.Games);
            var timesBox = new StackPanel { Margin = new Thickness(0, 26, 0, 0), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
            timesBox.Children.Add(Theme.Heading("Best time to play"));
            Times(timesBox, times, best);
            p.Children.Add(timesBox);

            // Habits, tilt and recurring review notes, from the stats report (its ladder notes are on the Ladder page).
            var notes = new WrapPanel { Margin = new Thickness(0, 26, 0, 0) };
            foreach (var sec in view.Notes.Where(x => x.Lines.Count > 0 && x.Title != "Ladder"))
            {
                var box = new StackPanel { Width = 440, Margin = new Thickness(0, 0, 30, 22) };
                box.Children.Add(Theme.Heading(sec.Title));
                var table = sec.Lines.Where(l => l.Cells != null).ToList();
                if (table.Count > 0) box.Children.Add(CellsTable(sec.Title, table));
                foreach (var l in sec.Lines.Where(l => l.Cells == null))
                foreach (var part in l.Text.Split(new[] { "   " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = Theme.Text(part.Trim(), 13.5, l.Tone == Tone.Warn || l.Tone == Tone.Bad ? Theme.Amber : l.Tone == Tone.Good ? Theme.Win : l.Tone == Tone.Dim ? Theme.Dusk : Theme.Vellum, wrap: true);
                    t.Margin = new Thickness(0, table.Count > 0 ? 8 : -4, 0, 8);
                    box.Children.Add(t);
                }
                notes.Children.Add(box);
            }
            p.Children.Add(notes);

            var which = scope == AppShell.StatsLadder ? "ranked and casual" : "every card mode";
            return AppShell.Page("Stats", $"{s.Games} finished game{(s.Games == 1 ? "" : "s")} recorded with HearthStoner: {which}.", p);
        }

        /// <summary>Games in the rolling win rate.</summary>
        private const int TrendWindow = 10;

        /// <summary>
        /// The win rate over your last <see cref="TrendWindow"/> games at each game, a line over a dashed 50%, green
        /// above and red below where it ends; your overall rate on hover.
        /// </summary>
        private static UIElement TrendChart(List<double> values)
        {
            const double h = 120, left = 40, bottom = 4;
            var c = new Canvas { Height = h + bottom, Margin = new Thickness(0, -2, 0, 4), Background = Brushes.Transparent, ClipToBounds = true };
            void Draw()
            {
                c.Children.Clear();
                var w = c.ActualWidth;
                if (w <= left + 10) return;
                double X(int i) => left + (w - left - 4) * i / (values.Count - 1);
                double Y(double v) => 4 + (h - 8) * (1 - v);
                foreach (var (v, label) in new[] { (1.0, "100%"), (0.5, "50%"), (0.0, "0%") })
                {
                    var t = Theme.Text(label, 11, Theme.Dusk);
                    Canvas.SetLeft(t, 0); Canvas.SetTop(t, Y(v) - 8);
                    c.Children.Add(t);
                    c.Children.Add(new Line { X1 = left, X2 = w, Y1 = Y(v), Y2 = Y(v), Stroke = Theme.Line, StrokeThickness = 1, StrokeDashArray = v == 0.5 ? new DoubleCollection { 4, 4 } : null });
                }
                var line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, Stroke = values[values.Count - 1] >= 0.5 ? Theme.Win : Theme.Loss };
                for (var i = 0; i < values.Count; i++) line.Points.Add(new Point(X(i), Y(values[i])));
                c.Children.Add(line);
            }
            c.SizeChanged += (o, e) => { if (e.WidthChanged) Draw(); };
            c.ToolTip = $"Win rate over the last {TrendWindow} games at each game, oldest on the left. Now: {AppShell.Pct(values[values.Count - 1])}.";
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            p.Children.Add(c);
            p.Children.Add(Theme.Text($"Each point: your win rate over the {TrendWindow} games up to it. Dashed line: 50%.", 12, Theme.Dusk));
            return p;
        }

        /// <summary>Your record going first and on the coin against each class, most played first (shown when you play one deck).</summary>
        private static UIElement FirstOrCoin(List<GameRecord> games)
        {
            var p = new StackPanel();
            p.Children.Add(Theme.Heading("First or on the coin"));
            var cross = WinTables.Cross(games, g => MetaStats.Key(g.OpponentClass), g => g.WentFirst ? "first" : "coin");
            var classes = cross.Keys.Select(k => k.Row).Distinct().OrderByDescending(cls => cross.Where(kv => kv.Key.Row == cls).Sum(kv => kv.Value.Games)).Take(10).ToList();
            if (classes.Count == 0) { p.Children.Add(Theme.Text("Shows up once a few games are recorded.", 13.5, Theme.Dusk, wrap: true)); return p; }
            Grid Row(double hgt)
            {
                var g = new Grid { Height = hgt };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                return g;
            }
            void Put(Grid g, FrameworkElement el, int col) { el.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(el, col); g.Children.Add(el); }
            var head = Row(24);
            Put(head, AppShell.ColumnLabel("Against"), 1);
            Put(head, AppShell.ColumnLabel("First", TextAlignment.Right), 2);
            Put(head, AppShell.ColumnLabel("Coin", TextAlignment.Right), 3);
            p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = head });
            TextBlock CellText(WinTables.Cell c)
            {
                var t = new TextBlock { FontSize = 13.5, TextAlignment = TextAlignment.Right, Foreground = Theme.Dusk };
                if (c.Games == 0) { t.Text = "-"; return t; }
                t.Inlines.Add(new System.Windows.Documents.Run($"{c.Wins}-{c.Losses}  ") { Foreground = Theme.Mist });
                t.Inlines.Add(new System.Windows.Documents.Run(c.Games < 3 ? "few" : AppShell.Pct(c.WinRate)) { Foreground = c.Games < 3 ? Theme.Dusk : Theme.RateBrush(c.WinRate), FontWeight = FontWeights.SemiBold });
                return t;
            }
            foreach (var cls in classes)
            {
                var r = Row(38);
                Put(r, new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Theme.B(Theme.ClassColor(cls)), HorizontalAlignment = HorizontalAlignment.Left }, 0);
                Put(r, Theme.Text(MetaStats.ClassName(cls), 14), 1);
                Put(r, CellText(cross.TryGetValue((cls, "first"), out var f) ? f : default), 2);
                Put(r, CellText(cross.TryGetValue((cls, "coin"), out var cn) ? cn : default), 3);
                p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = r });
            }
            return p;
        }

        /// <summary>
        /// Each deck against each class as a grid: the cell's colour is the win rate (dim under 3 games), its text the
        /// record. Shown when you play two or more decks.
        /// </summary>
        private static UIElement DeckGrid(List<GameRecord> games)
        {
            var cross = WinTables.Cross(games, g => g.DeckLabel, g => MetaStats.Key(g.OpponentClass));
            int Games(Func<(string Row, string Col), bool> pick) => cross.Where(kv => pick(kv.Key)).Sum(kv => kv.Value.Games);
            var decks = cross.Keys.Select(k => k.Row).Distinct().OrderByDescending(dk => Games(k => k.Row == dk)).Take(8).ToList();
            var classes = cross.Keys.Select(k => k.Col).Distinct().OrderByDescending(cls => Games(k => k.Col == cls)).Take(11).ToList();
            var p = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
            p.Children.Add(Theme.Heading("Decks against classes"));
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            foreach (var _ in classes) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            for (var i = 0; i < classes.Count; i++)
            {
                var t = AppShell.ColumnLabel(MetaStats.ClassName(classes[i]), TextAlignment.Center);
                t.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(t, i + 1);
                g.Children.Add(t);
            }
            for (var r = 0; r < decks.Count; r++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
                var name = Theme.Text(decks[r], 14);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.ToolTip = decks[r];
                Grid.SetRow(name, r + 1);
                g.Children.Add(name);
                for (var c = 0; c < classes.Count; c++)
                {
                    if (!cross.TryGetValue((decks[r], classes[c]), out var cell)) continue;
                    var few = cell.Games < 3;
                    var rate = cell.WinRate;
                    var color = rate >= 0.52 ? Theme.WinC : rate < 0.48 ? Theme.LossC : Theme.DuskC;
                    var box = new Border
                    {
                        Margin = new Thickness(3), CornerRadius = new CornerRadius(6),
                        Background = Theme.B(color, few ? 0.10 : 0.18 + 0.30 * Math.Abs(rate - 0.5) * 2),
                        Child = new TextBlock { Text = $"{cell.Wins}-{cell.Losses}", FontSize = 13, Foreground = few ? Theme.Dusk : Theme.Vellum, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                        ToolTip = $"{decks[r]} vs {MetaStats.ClassName(classes[c])}: {cell.Wins}-{cell.Losses}" + (few ? " (too few games to judge)" : $", {AppShell.Pct(rate)}"),
                    };
                    Grid.SetRow(box, r + 1);
                    Grid.SetColumn(box, c + 1);
                    g.Children.Add(box);
                }
            }
            // The class columns share the width up to 96 px each (no scroll box: it would take the page's mouse wheel).
            var fit = new Grid();
            fit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 200 + 96 * classes.Count });
            fit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            fit.Children.Add(g);
            p.Children.Add(fit);
            var note = Theme.Text("Green: you win more than you lose there, red: less; stronger colour, further from 50%. Faint: under 3 games.", 12, Theme.Dusk, wrap: true);
            note.Margin = new Thickness(0, 6, 0, 0);
            p.Children.Add(note);
            return p;
        }

        /// <summary>The games shown: your pick, else ranked and casual when there are any, else every card mode.</summary>
        private static string Scope(AppShell shell)
        {
            if (shell.StatsScope == AppShell.StatsLadder || shell.StatsScope == AppShell.StatsAll) return shell.StatsScope;
            return shell.Data.LadderStats.Stats?.Games > 0 ? AppShell.StatsLadder : AppShell.StatsAll;
        }

        /// <summary>"Ranked and casual 18" / "All card modes 29": which games the page counts.</summary>
        private static UIElement ScopeChips(AppShell shell, string scope)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            int ladder = shell.Data.LadderStats.Stats?.Games ?? 0, all = shell.Data.AllStats.Stats?.Games ?? 0;
            row.Children.Add(AppShell.FilterChip($"Ranked and casual {ladder}", scope == AppShell.StatsLadder,
                () => { shell.StatsScope = AppShell.StatsLadder; shell.Redraw(); }, "Your ladder games: the ones the advice and the meta data are about."));
            row.Children.Add(AppShell.FilterChip($"All card modes {all}", scope == AppShell.StatsAll,
                () => { shell.StatsScope = AppShell.StatsAll; shell.Redraw(); }, "Adds Arena, Tavern Brawl and solo games."));
            return row;
        }

        /// <summary>Win rate by time of day: one row per block, the bar's length is the win rate; too few games get a dashed track.</summary>
        private static void Times(Panel p, List<LadderReport.TimeBlock> times, LadderReport.TimeBlock best)
        {
            foreach (var t in times)
            {
                var enough = t.Games >= LadderReport.MinGames;
                var g = new Grid { Height = 28 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                var n = Theme.Text(t.Name, 13.5, t == best ? Theme.Vellum : enough ? Theme.Mist : Theme.Dusk, t == best ? FontWeights.SemiBold : FontWeights.Normal);
                n.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(n);
                UIElement track = enough
                    ? AppShell.StatBar(t.WinRate, Theme.RateBrush(t.WinRate), 8)
                    : new Border { Height = 8, CornerRadius = new CornerRadius(4), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Background = Brushes.Transparent };
                ((FrameworkElement)track).VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(track, 1);
                g.Children.Add(track);
                var v = new TextBlock { FontSize = 13, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Mist };
                if (t.Games == 0) v.Text = "no games";
                else if (!enough) v.Text = $"{t.Games} game{(t.Games == 1 ? "" : "s")} · too few";
                else
                {
                    v.Inlines.Add(new System.Windows.Documents.Run(AppShell.Pct(t.WinRate)) { Foreground = Theme.RateBrush(t.WinRate), FontWeight = FontWeights.SemiBold });
                    v.Inlines.Add(new System.Windows.Documents.Run($" of {t.Games}"));
                }
                Grid.SetColumn(v, 2);
                g.Children.Add(v);
                p.Children.Add(g);
            }
            var note = Theme.Text(best != null
                ? $"You win most in the {best.Name.ToLowerInvariant()}. Ranked and casual games, by the hour they started; bar length is the win rate."
                : $"Needs {LadderReport.MinGames}+ ranked or casual games in at least two of these to compare. Bar length is the win rate.", 12, Theme.Dusk, wrap: true);
            note.Margin = new Thickness(0, 6, 0, 0);
            p.Children.Add(note);
        }

        /// <summary>
        /// One square per recent game, OLDER to NEWEST, the newest outlined; hovering one shows the game and clicking
        /// opens it in History.
        /// </summary>
        private static UIElement RecentForm(StatsSummary s, AppShell shell)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -2, 0, 26) };
            var older = AppShell.ColumnLabel("Older");
            older.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(older);
            row.Children.Add(AppShell.ResultSquares(shell, s.RecentGames));

            var newer = AppShell.ColumnLabel("Newest");
            newer.Foreground = Theme.Vellum;
            newer.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(newer);
            return row;
        }

        /// <summary>Column headings for the stats notes drawn as tables.</summary>
        private static readonly Dictionary<string, string[]> TableHeads = new Dictionary<string, string[]>
        {
            ["Mulligan habits"] = new[] { "Card", "You keep", "Worth", "Do" },
            ["Tilt check"] = new[] { "When", "Record", "Win" },
        };

        /// <summary>
        /// Stats-report lines with cells as a table. Mulligan habits get a green "Keep more" or red "Toss more" pill;
        /// a win-rate column is coloured like the rest of the page.
        /// </summary>
        private static UIElement CellsTable(string title, List<AdviceLine> lines)
        {
            var cols = lines.Max(l => l.Cells.Length);
            var p = new StackPanel();
            Grid Row(double height)
            {
                var g = new Grid { Height = height };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (var i = 1; i < cols; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == cols - 1 && cols == 4 ? 96 : 72) });
                return g;
            }
            if (TableHeads.TryGetValue(title, out var heads))
            {
                var h = Row(24);
                for (var i = 0; i < Math.Min(cols, heads.Length); i++)
                {
                    var t = AppShell.ColumnLabel(heads[i], i == 0 ? TextAlignment.Left : TextAlignment.Right);
                    Grid.SetColumn(t, i);
                    h.Children.Add(t);
                }
                p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = h });
            }
            foreach (var l in lines)
            {
                var r = Row(34);
                for (var i = 0; i < l.Cells.Length; i++)
                {
                    var cell = l.Cells[i];
                    FrameworkElement el;
                    if (cell == "Keep more" || cell == "Toss more")
                    {
                        var c = cell == "Keep more" ? Theme.WinC : Theme.LossC;
                        el = new Border
                        {
                            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3), Background = Theme.B(c, 0.16), HorizontalAlignment = HorizontalAlignment.Right,
                            Child = new TextBlock { Text = cell, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.B(c) },
                        };
                    }
                    else
                    {
                        var brush = i == 0 ? Theme.Vellum : Theme.Mist;
                        if (cell.EndsWith("%") && double.TryParse(cell.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var v))
                            brush = cell.StartsWith("+") ? Theme.Win : cell.StartsWith("-") ? Theme.Loss : i == cols - 1 ? Theme.RateBrush(v / 100) : Theme.Mist;
                        var t = Theme.Text(cell, 13.5, brush, i == cols - 1 ? FontWeights.SemiBold : FontWeights.Normal);
                        t.TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right;
                        el = t;
                    }
                    el.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(el, i);
                    r.Children.Add(el);
                }
                p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = r });
            }
            return p;
        }

        /// <summary>Mana spent per turn (bars: crystals available, filled: spent), headline rates, and per deck.</summary>
        private static UIElement Mana(ManaStats m)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 26) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            left.Children.Add(Theme.Heading("Mana use"));
            var nums = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -2, 0, 14) };
            void Num(string v, string label, Brush c)
            {
                var b = new StackPanel { Margin = new Thickness(0, 0, 34, 0) };
                b.Children.Add(Theme.Text(v, 26, c, FontWeights.SemiBold, display: true));
                b.Children.Add(Theme.Text(label, 12, Theme.Dusk));
                nums.Children.Add(b);
            }
            Num(AppShell.Pct(m.UsedRate), "of your mana spent", m.UsedRate >= 0.85 ? Theme.Win : m.UsedRate >= 0.75 ? Theme.Vellum : Theme.Amber);
            Num(AppShell.Pct(m.CurveRate), "turns 1-6 with all of it used", Theme.Vellum);
            Num(m.AvgUnspent.ToString("0.0"), "left over per turn", m.AvgUnspent <= 1 ? Theme.Vellum : Theme.Amber);
            left.Children.Add(nums);

            // Bars per turn: the track is the crystals you had on average, the fill what you spent.
            const double barH = 92;
            var bars = new Grid { Height = barH + 20, HorizontalAlignment = HorizontalAlignment.Left };
            var maxAvail = Math.Max(1, m.ByRound.Max(r => r.AvgAvailable));
            for (var i = 0; i < m.ByRound.Count; i++)
            {
                var r = m.ByRound[i];
                bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                var cell = new Grid { Width = 22, HorizontalAlignment = HorizontalAlignment.Center };
                cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(barH) });
                cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
                var stall = m.StallRound == r;
                var track = new Border { Height = barH * r.AvgAvailable / maxAvail, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(4), Background = Theme.Raised,
                    BorderBrush = stall ? Theme.Amber : null, BorderThickness = new Thickness(stall ? 1 : 0) };
                var spent = new Border { Height = barH * r.AvgSpent / maxAvail, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(4), Background = Theme.Mana };
                cell.Children.Add(track);
                cell.Children.Add(spent);
                cell.ToolTip = $"Turn {r.Round}: {r.AvgSpent:0.0} of {r.AvgAvailable:0.0} mana spent on average over {r.Turns} turns; all of it {AppShell.Pct(r.FullRate)} of the time";
                var lbl = Theme.Text(r.Round.ToString(), 11.5, stall ? Theme.Amber : Theme.Dusk);
                lbl.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(lbl, 1);
                cell.Children.Add(lbl);
                Grid.SetColumn(cell, i);
                bars.Children.Add(cell);
            }
            left.Children.Add(bars);
            var key = Theme.Text("Each bar is a turn: its height is the mana you had, the blue part what you spent (averages).", 12, Theme.Dusk, wrap: true);
            key.Margin = new Thickness(0, 6, 0, 0);
            left.Children.Add(key);
            row.Children.Add(left);

            var right = new StackPanel();
            right.Children.Add(Theme.Heading("Where it goes unspent"));
            var stallText = m.StallRound != null
                ? $"Turn {m.StallRound.Round} is where you stall most: {m.StallRound.AvgUnspent:0.0} mana left over on average, all of it used only {AppShell.Pct(m.StallRound.FullRate)} of the time. A cheaper card or two at that cost would smooth it out."
                : "No turn stands out: you use your mana about evenly through the early and mid game.";
            var st = Theme.Text(stallText, 13.5, m.StallRound != null ? Theme.Vellum : Theme.Dusk, wrap: true);
            st.Margin = new Thickness(0, -4, 0, 12);
            right.Children.Add(st);
            foreach (var dr in m.ByDeck.Take(5))
            {
                var g = new Grid { Height = 36 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Theme.B(Theme.ClassColor(dr.Class)), HorizontalAlignment = HorizontalAlignment.Left });
                var n = Theme.Text(dr.Name, 14); n.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(n, 1); g.Children.Add(n);
                var u = Theme.Text($"{AppShell.Pct(dr.UsedRate)} spent", 13, Theme.Vellum); u.VerticalAlignment = VerticalAlignment.Center; u.TextAlignment = TextAlignment.Right; Grid.SetColumn(u, 2); g.Children.Add(u);
                var c = Theme.Text($"{AppShell.Pct(dr.CurveRate)} on curve", 13, Theme.Dusk); c.VerticalAlignment = VerticalAlignment.Center; c.TextAlignment = TextAlignment.Right; Grid.SetColumn(c, 3); g.Children.Add(c);
                g.ToolTip = $"{dr.Turns} turns, {dr.AvgUnspent:0.0} mana left over per turn";
                right.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g });
            }
            var foot = Theme.Text($"Your turns 1-10 in {m.Games} game{(m.Games == 1 ? "" : "s")}; the last turn of each game is left out.", 12, Theme.Dusk, wrap: true);
            foot.Margin = new Thickness(0, 8, 0, 0);
            right.Children.Add(foot);
            Grid.SetColumn(right, 2);
            row.Children.Add(right);
            return row;
        }

        /// <summary>How often HearthStoner's deck guess at the start of each round matched its final answer.</summary>
        private static UIElement Scorecard(PredictionScore score)
        {
            var p = new StackPanel();
            p.Children.Add(Theme.Heading("How early it reads their deck"));
            if (score.Games == 0)
            {
                p.Children.Add(Theme.Text("Shows up once a few games are recorded: how often the overlay's guess at the opponent's deck was already right on each turn.", 13.5, Theme.Dusk, wrap: true));
                return p;
            }
            foreach (var r in score.Rounds)
            {
                var g = new Grid { Height = 26 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                var n = Theme.Text($"Turn {r.Number}", 13.5, Theme.Dusk); n.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(n);
                var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Raised, VerticalAlignment = VerticalAlignment.Center };
                var fill = new Grid();
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, r.Accuracy), GridUnitType.Star) });
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - r.Accuracy), GridUnitType.Star) });
                fill.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Mana });
                track.Child = fill;
                track.ToolTip = $"Right in {r.Right} of {r.Games} games; it was {AppShell.Pct(r.AverageConfidence)} sure on average";
                Grid.SetColumn(track, 1); g.Children.Add(track);
                var v = Theme.Text(AppShell.Pct(r.Accuracy), 13.5, Theme.Vellum, FontWeights.SemiBold); v.TextAlignment = TextAlignment.Right; v.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(v, 2); g.Children.Add(v);
                p.Children.Add(g);
            }
            var note = Theme.Text($"Share of {score.Games} game{(score.Games == 1 ? "" : "s")} where the overlay already named the deck it settled on by the end. Only games it ended up at least 80% sure about count.", 12, Theme.Dusk, wrap: true);
            note.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(note);
            return p;
        }

        /// <summary>Rows with a swatch, name, record and a win-rate bar.</summary>
        private static UIElement Table(string title, System.Collections.Generic.List<StatsSummary.Row> rows, Func<StatsSummary.Row, Color> color)
        {
            var p = new StackPanel();
            p.Children.Add(Theme.Heading(title));
            foreach (var r in rows.Take(12))
            {
                var g = new Grid { Height = 38 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Theme.B(color(r)), HorizontalAlignment = HorizontalAlignment.Left });
                var n = Theme.Text(r.Name, 14); n.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(n, 1); g.Children.Add(n);
                var rec = Theme.Text($"{r.Wins}-{r.Losses}", 13.5, Theme.Dusk); rec.VerticalAlignment = VerticalAlignment.Center; rec.TextAlignment = TextAlignment.Right; rec.Margin = new Thickness(0, 0, 12, 0); Grid.SetColumn(rec, 2); g.Children.Add(rec);
                var bar = new Grid { VerticalAlignment = VerticalAlignment.Center };
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Raised };
                var fillGrid = new Grid();
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, r.WinRate), GridUnitType.Star) });
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - r.WinRate), GridUnitType.Star) });
                fillGrid.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.RateBrush(r.WinRate) });
                track.Child = fillGrid;
                bar.Children.Add(track);
                var pct = Theme.Text(AppShell.Pct(r.WinRate), 13.5, Theme.RateBrush(r.WinRate), FontWeights.SemiBold); pct.TextAlignment = TextAlignment.Right; Grid.SetColumn(pct, 1); bar.Children.Add(pct);
                Grid.SetColumn(bar, 3);
                g.Children.Add(bar);
                p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g });
            }
            return p;
        }
    }
}
