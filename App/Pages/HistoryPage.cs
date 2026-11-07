using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HearthStoner.Pages
{
    /// <summary>History: your games on the left; the selected game's verdict, key numbers, swing chart and review.</summary>
    public static class HistoryPage
    {
        public static UIElement Build(AppShell shell)
        {
            var d = shell.Data;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var list = new StackPanel();
            var head = new StackPanel { Margin = new Thickness(22, 24, 22, 16) };
            head.Children.Add(Theme.Title("History", 32));
            var wins = d.Games.Count(x => x.Won);
            head.Children.Add(Theme.Text(d.Games.Count == 0 ? "No games yet" : $"{d.Games.Count} game{(d.Games.Count == 1 ? "" : "s")}, {wins} win{(wins == 1 ? "" : "s")}", 13, Theme.Dusk));
            list.Children.Add(head);
            if (shell.SelectedGame >= d.Games.Count) shell.SelectedGame = 0;
            if (d.Games.Count > 0) list.Children.Add(FilterChips(shell));
            // One small header per day instead of the date on every row.
            DateTime? day = null;
            for (var i = 0; i < d.Games.Count; i++)
            {
                if (!Matches(d.Games[i], shell.HistoryFilter)) continue;
                if (day != d.Games[i].Start.Date)
                {
                    day = d.Games[i].Start.Date;
                    list.Children.Add(DayHeader(day.Value));
                }
                list.Children.Add(GameRow(d.Games[i], i, shell));
            }
            if (d.OtherGames.Count > 0)
            {
                // Modes HearthStoner doesn't track card by card: how often you played them and how it went.
                var other = new StackPanel { Margin = new Thickness(22, 20, 22, 16) };
                other.Children.Add(Theme.Text("Other modes", 15, Theme.Vellum, FontWeights.SemiBold));
                other.Children.Add(Theme.Text("Battlegrounds and Mercenaries: when you played and the result.", 12, Theme.Dusk, wrap: true));
                foreach (var grp in d.OtherGames.GroupBy(x => GameModes.Name(x.GameType, x.Format)))
                {
                    int n = grp.Count(), w = grp.Count(x => x.Won);
                    var line = Theme.Text($"{grp.Key}: {n} game{(n == 1 ? "" : "s")}, {w} win{(w == 1 ? "" : "s")}, last {grp.First().Start:MMM d}", 13, Theme.Vellum, wrap: true);
                    line.Margin = new Thickness(0, 6, 0, 0);
                    other.Children.Add(line);
                }
                list.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = other });
            }
            var listScroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            g.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = listScroll });

            UIElement detail = d.Games.Count == 0
                ? PlayPage.Empty("No games recorded yet", "Play a game with HearthStoner running and it appears here, with a review of the turns that decided it.")
                : Detail(d.Games[shell.SelectedGame], shell);
            var scroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(34, 26, 30, 26), Focusable = false };
            Grid.SetColumn(scroll, 1);
            g.Children.Add(scroll);
            return g;
        }

        private static string Opponent(GameRecord g) => g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass);

        private static readonly string[] Filters = { "All", "Ranked", "Casual", "Wins", "Losses" };

        /// <summary>Whether a game shows under the History filter chip.</summary>
        public static bool Matches(GameRecord g, string filter)
        {
            switch (filter)
            {
                case "Ranked": return g.GameType == "GT_RANKED";
                case "Casual": return g.GameType == "GT_CASUAL";
                case "Wins": return g.Won;
                case "Losses": return g.Result == "LOST";
                default: return true;
            }
        }

        /// <summary>The row of filter chips above the game list; the chosen one is outlined in mana blue.</summary>
        private static UIElement FilterChips(AppShell shell)
        {
            var wrap = new WrapPanel { Margin = new Thickness(22, 0, 22, 12) };
            foreach (var f in Filters)
            {
                var on = f == shell.HistoryFilter;
                var n = shell.Data.Games.Count(g => Matches(g, f));
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(13), Padding = new Thickness(11, 4, 11, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = System.Windows.Input.Cursors.Hand,
                    Background = on ? Theme.B(Theme.Rgb(0x2E, 0x35, 0x66)) : Theme.Raised, BorderBrush = on ? Theme.Mana : Theme.Line, BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = f == "All" ? $"All {n}" : f, FontSize = 13, Foreground = on ? Theme.Vellum : Theme.Mist },
                    ToolTip = $"{n} game{(n == 1 ? "" : "s")}",
                };
                var pick = f;
                chip.MouseLeftButtonUp += (s, e) =>
                {
                    shell.HistoryFilter = pick;
                    // Keep the detail on a game the list shows.
                    var first = shell.Data.Games.FindIndex(g => Matches(g, pick));
                    if (first >= 0 && !Matches(shell.Data.Games[shell.SelectedGame], pick)) shell.SelectedGame = first;
                    shell.Redraw();
                };
                wrap.Children.Add(chip);
            }
            return wrap;
        }

        private static UIElement DayHeader(DateTime day)
        {
            var text = day == DateTime.Today ? "Today" : day == DateTime.Today.AddDays(-1) ? "Yesterday" : day.ToString("ddd, MMM d");
            var t = AppShell.ColumnLabel(text);
            t.Margin = new Thickness(22, 12, 22, 6);
            return t;
        }

        private static UIElement GameRow(GameRecord g, int index, AppShell shell)
        {
            var row = new Grid { Margin = new Thickness(22, 11, 22, 11) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var c = g.Won ? Theme.WinC : g.Result == "LOST" ? Theme.LossC : Theme.DuskC;
            var res = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Theme.B(c, 0.14), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = g.Won ? "W" : g.Result == "LOST" ? "L" : "-", Foreground = Theme.B(c), FontFamily = Theme.Display, FontWeight = FontWeights.Bold, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            row.Children.Add(res);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var vs = Theme.Text("vs " + Opponent(g), 14.5, Theme.Vellum, FontWeights.SemiBold, wrap: true);
            names.Children.Add(vs);
            var mode = GameModes.Name(g.GameType, g.Format, brief: true);
            names.Children.Add(Theme.Text((g.DeckName ?? MetaStats.ClassName(g.MyClass)) + (mode != null ? " · " + mode : ""), 12.5, Theme.Dusk));
            Grid.SetColumn(names, 1);
            row.Children.Add(names);
            var when = new TextBlock { FontSize = 12, Foreground = Theme.Dusk, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            when.Text = g.Start.ToString("h:mm tt") + "\n" + $"{(g.Turns + 1) / 2} turns";   // the date is in the day header
            Grid.SetColumn(when, 2);
            row.Children.Add(when);

            var selected = index == shell.SelectedGame;
            var b = new Border
            {
                Child = row, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Background = selected ? new LinearGradientBrush(Color.FromArgb(31, 90, 160, 255), Colors.Transparent, 0) : (Brush)Brushes.Transparent,
            };
            if (selected)
            {
                var wrap = new Grid();
                wrap.Children.Add(b);
                wrap.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.Mana });
                return wrap;
            }
            b.MouseLeftButtonUp += (s, e) => { shell.SelectedGame = index; shell.Redraw(); };
            return b;
        }

        private static UIElement Detail(GameRecord g, AppShell shell)
        {
            var d = shell.Data;
            var sum = GameSummary.From(g);
            var replay = ReplayPage.TryGet(g, shell);
            var analysis = replay != null ? ReplayPage.Analyze(g, replay, d) : default;
            var review = replay != null ? analysis.Review : g.Review;
            var replayIndex = ReplayPage.Replayable(d).IndexOf(g);
            void Watch(int frame) { shell.ReplayGame = replayIndex; shell.ReplayFrame = frame; shell.Show("Replays"); }
            var p = new StackPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };

            var verdict = new StackPanel { Orientation = Orientation.Horizontal };
            var word = g.Won ? "Win" : g.Result == "LOST" ? "Loss" : g.Result == "TIED" ? "Tie" : "Unfinished";
            verdict.Children.Add(Theme.Text(word, 46, g.Won ? Theme.Win : g.Result == "LOST" ? Theme.Loss : Theme.Dusk, FontWeights.Bold, display: true));
            var titles = new StackPanel { Margin = new Thickness(16, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Theme.Text($"{g.DeckName ?? MetaStats.ClassName(g.MyClass)} vs {Opponent(g)}", 26, Theme.Vellum, FontWeights.SemiBold, display: true));
            var who = new List<string>();
            if (!string.IsNullOrEmpty(g.OpponentName)) who.Add(g.OpponentName.Split('#')[0]);
            if (!string.IsNullOrEmpty(g.Rank)) who.Add(AppData.PrettyRank(g.Rank));
            who.Add(g.WentFirst ? "you went first" : "you had the coin");
            who.Add(g.Start.ToString("MMM d, h:mm tt"));
            titles.Children.Add(Theme.Text(string.Join(", ", who), 13.5, Theme.Dusk));
            verdict.Children.Add(titles);
            p.Children.Add(verdict);
            if (replayIndex >= 0)
            {
                var watch = Theme.Button("Watch replay", () => Watch(0));
                watch.Margin = new Thickness(0, 14, 0, 0);
                p.Children.Add(watch);
            }

            var facts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            void Fact(string v, string label) { var f = new StackPanel { Margin = new Thickness(0, 0, 40, 0) }; f.Children.Add(Theme.Text(v, 20, Theme.Vellum, FontWeights.SemiBold, display: true)); f.Children.Add(Theme.Text(label, 12.5, Theme.Dusk)); facts.Children.Add(f); }
            Fact(sum.Rounds.ToString(), "turns");
            Fact($"{sum.RoundsAhead} of {sum.Swing.Count}", "rounds you were ahead");
            var chance = replay != null ? analysis.Chance : null;
            if (chance != null)
            {
                // The last frames are the result itself (a hero at 0): leave them out, and nothing is ever certain.
                var peak = Math.Min(0.98, chance.Values.Take(Math.Max(1, chance.Values.Count - 1)).Max());
                Fact(AppShell.Pct(peak), "best win chance");
                var low = Math.Max(0.02, chance.Values.Take(Math.Max(1, chance.Values.Count - 1)).Min());
                Fact(AppShell.Pct(low), "worst win chance");
                if (chance.Drops.Count > 0) Fact("Turn " + chance.Drops[0].Round, "where it turned");
            }
            else
            {
                if (sum.BestLead > 0) Fact("+" + sum.BestLead, $"best lead (turn {sum.BestLeadRound})");
                if (sum.TurningDrop >= 10) Fact("Turn " + sum.TurningRound, "where it turned");
            }
            Fact(review.Count.ToString(), review.Count == 1 ? "thing to review" : "things to review");
            p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(0, 14, 0, 14), Margin = new Thickness(0, 20, 0, 22), Child = facts });

            if (chance != null)
            {
                p.Children.Add(Theme.Heading("Win chance"));
                var sub = Theme.Text($"HearthStoner's estimate at each moment, starting from the matchup's {AppShell.Pct(chance.Prior)}. Click a red dot to see that moment in the replay.", 12.5, Theme.Dusk, wrap: true);
                sub.Margin = new Thickness(0, -6, 0, 8);
                p.Children.Add(sub);
                var c = new Canvas { Width = 760, Height = 210, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
                WinChanceView.Draw(c, chance, -1, replay, Watch);
                p.Children.Add(c);
                foreach (var m in chance.Drops.Concat(chance.Rises).OrderBy(m => m.Frame))
                {
                    var down = chance.Drops.Contains(m);
                    var line = new TextBlock { FontSize = 13, Foreground = Theme.Vellum, Margin = new Thickness(0, 6, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, TextWrapping = TextWrapping.Wrap };
                    line.Inlines.Add(new System.Windows.Documents.Run($"Turn {m.Round}  ") { Foreground = Theme.Dusk });
                    line.Inlines.Add(new System.Windows.Documents.Run($"{m.Before:P0} to {m.After:P0}  ") { Foreground = down ? Theme.Loss : Theme.Win, FontWeight = FontWeights.SemiBold });
                    line.Inlines.Add(new System.Windows.Documents.Run(m.Action ?? ""));
                    line.ToolTip = "Show in the replay";
                    var frame = m.Frame;
                    line.MouseLeftButtonUp += (s2, e2) => Watch(frame);
                    p.Children.Add(line);
                }
            }
            else
            {
                p.Children.Add(Theme.Heading("How the game swung"));
                var sub = Theme.Text(!string.IsNullOrEmpty(g.LogFile) ? "Working out win chances from the game's log..." : "Health, armor and board stats, yours minus theirs, after each round.", 12.5, Theme.Dusk);
                sub.Margin = new Thickness(0, -6, 0, 8);
                p.Children.Add(sub);
                p.Children.Add(sum.Swing.Count >= 2 ? Chart(sum) : Theme.Text("Not enough turns were recorded to draw this game.", 13.5, Theme.Dusk));
            }

            var reviewHead = Theme.Heading("Review");
            reviewHead.Margin = new Thickness(0, 22, 0, 6);
            p.Children.Add(reviewHead);
            if (review.Count == 0) p.Children.Add(Theme.Text("Nothing stood out: no missed lethal, wasted mana, held cards, lost trades or mulligan against the data.", 13.5, Theme.Dusk, wrap: true));
            foreach (var n in review.OrderByDescending(n => n.Severity).ThenBy(n => n.Turn))
                p.Children.Add(Note(n, replay != null && n.Turn > 0 ? () => Watch(ReplayPage.FrameOf(n, replay)) : (Action)null));

            if (g.Mulligan.Count > 0)
            {
                var mh = Theme.Heading("Mulligan");
                mh.Margin = new Thickness(0, 22, 0, 8);
                p.Children.Add(mh);
                var wrap = new WrapPanel();
                foreach (var m in g.Mulligan) wrap.Children.Add(MulliganCard(m, d));
                p.Children.Add(wrap);
            }
            return p;
        }

        /// <summary>
        /// One opening-hand card as a small card picture: kept in full colour, tossed faded, and an amber frame when
        /// what you did goes against the data. Falls back to a name chip until the picture is downloaded.
        /// </summary>
        private static UIElement MulliganCard(MulliganPick m, AppData d)
        {
            var against = m.Advice != null && ((m.Advice == "KEEP" && !m.Kept) || (m.Advice == "TOSS" && m.Kept));
            var name = d.Cards.NameOf(m.CardId);
            var p = new StackPanel { Width = 112, Margin = new Thickness(0, 0, 12, 8) };
            var img = CardTiles.GetRender(m.CardId);
            UIElement pic = img != null
                ? new Image { Source = img, Width = 112, Opacity = m.Kept ? 1 : 0.4 }
                : (UIElement)new Border { Height = 150, CornerRadius = new CornerRadius(8), Background = Theme.Raised, Opacity = m.Kept ? 1 : 0.5, Padding = new Thickness(8), Child = Theme.Text(name, 13, wrap: true) };
            p.Children.Add(against ? new Border { BorderBrush = Theme.Amber, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10), Child = pic } : pic);
            var label = Theme.Text(m.Kept ? (against ? "kept · data says toss" : "kept") : (against ? "tossed · data says keep" : "tossed"), 12.5, against ? Theme.Amber : Theme.Mist, wrap: true);
            label.TextAlignment = TextAlignment.Center;
            label.Margin = new Thickness(0, 4, 0, 0);
            p.Children.Add(label);
            p.ToolTip = name;
            return p;
        }

        private static UIElement Note(ReviewNote n, Action watch)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = n.Severity >= 3 ? Theme.Loss : Theme.Amber });
            var t = new TextBlock { FontSize = 13.5, Foreground = Theme.Vellum, TextWrapping = TextWrapping.Wrap };
            t.Inlines.Add(new System.Windows.Documents.Run((n.Turn > 0 ? $"Turn {(n.Turn + 1) / 2}: " : "") + n.Text));
            if (watch != null)
            {
                var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("See it")) { Foreground = Theme.Mana, TextDecorations = null, Cursor = System.Windows.Input.Cursors.Hand };
                link.Click += (s, e) => watch();
                t.Inlines.Add(new System.Windows.Documents.Run("  "));
                t.Inlines.Add(link);
            }
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }

        /// <summary>Area chart of the swing per round: green above zero, red below, the turning point marked.</summary>
        private static UIElement Chart(GameSummary s)
        {
            const double w = 700, h = 190, left = 34, right = 10, top = 8, bottom = 8;
            var c = new Canvas { Width = w, Height = h + 24, HorizontalAlignment = HorizontalAlignment.Left };
            var max = Math.Max(20, s.Swing.Max(p => Math.Abs(p.Value)));
            double X(int i) => left + i * (w - left - right) / (s.Swing.Count - 1);
            double Y(int v) => top + (h - top - bottom) / 2 - v * (h - top - bottom) / 2 / max;
            var zero = Y(0);

            c.Children.Add(new Line { X1 = left, X2 = w - right, Y1 = zero, Y2 = zero, Stroke = Theme.Line, StrokeThickness = 1 });
            var fig = new PathFigure { StartPoint = new Point(X(0), zero), IsClosed = true };
            for (var i = 0; i < s.Swing.Count; i++) fig.Segments.Add(new LineSegment(new Point(X(i), Y(s.Swing[i].Value)), false));
            fig.Segments.Add(new LineSegment(new Point(X(s.Swing.Count - 1), zero), false));
            var area = new PathGeometry(new[] { fig });
            Path Fill(Color col, bool above) => new Path
            {
                Data = area,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(120, col.R, col.G, col.B), above ? 0 : 1),
                    new GradientStop(Color.FromArgb(0, col.R, col.G, col.B), above ? 1 : 0),
                }, new Point(0, 0), new Point(0, 1)) { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, above ? top : zero), EndPoint = new Point(0, above ? zero : h - bottom) },
                Clip = new RectangleGeometry(above ? new Rect(0, 0, w, zero) : new Rect(0, zero, w, h - zero)),
            };
            c.Children.Add(Fill(Theme.WinC, true));
            c.Children.Add(Fill(Theme.LossC, false));
            var line = new Polyline { Stroke = Theme.Vellum, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            for (var i = 0; i < s.Swing.Count; i++) line.Points.Add(new Point(X(i), Y(s.Swing[i].Value)));
            c.Children.Add(line);

            void Label(string text, double x, double y, Brush b, TextAlignment align = TextAlignment.Left)
            {
                var t = Theme.Text(text, 11.5, b);
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(t, align == TextAlignment.Right ? x - t.DesiredSize.Width : align == TextAlignment.Center ? x - t.DesiredSize.Width / 2 : x);
                Canvas.SetTop(t, y);
                c.Children.Add(t);
            }
            Label("ahead", 0, top, Theme.Dusk);
            Label("behind", 0, h - bottom - 16, Theme.Dusk);
            var step = Math.Max(1, (int)Math.Ceiling(s.Swing.Count / 10.0));
            for (var i = 0; i < s.Swing.Count; i += step) Label(s.Swing[i].Round.ToString(), X(i), h + 4, Theme.Dusk, TextAlignment.Center);

            if (s.TurningDrop >= 10)
            {
                var i = s.Swing.FindIndex(p => p.Round == s.TurningRound);
                if (i >= 0)
                {
                    var dot = new Ellipse { Width = 11, Height = 11, Fill = Theme.Loss, Stroke = Theme.Night, StrokeThickness = 2 };
                    Canvas.SetLeft(dot, X(i) - 5.5); Canvas.SetTop(dot, Y(s.Swing[i].Value) - 5.5);
                    c.Children.Add(dot);
                    var text = $"Turn {s.TurningRound}: down {s.TurningDrop} in one round";
                    var onLeft = X(i) > w / 2;
                    Label(text, onLeft ? X(i) - 10 : X(i) + 10, Math.Min(h - 20, Y(s.Swing[i].Value) - 8), Theme.Loss, onLeft ? TextAlignment.Right : TextAlignment.Left);
                }
            }
            return c;
        }
    }
}
