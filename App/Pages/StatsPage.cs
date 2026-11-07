using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HearthStoner.Pages
{
    /// <summary>Stats: your record, recent form, results by matchup and by deck, then habits and tilt notes.</summary>
    public static class StatsPage
    {
        public static UIElement Build(AppShell shell)
        {
            var d = shell.Data;
            var s = d.Stats;
            var p = new StackPanel();
            if (s == null || s.Games == 0)
            {
                p.Children.Add(PlayPage.Empty("No finished games yet", "Play with HearthStoner running and your record, matchups and habits build up here."));
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
            p.Children.Add(Theme.Heading($"Last {s.Recent.Count} game{(s.Recent.Count == 1 ? "" : "s")}"));
            var form = new WrapPanel { Margin = new Thickness(0, -2, 0, 26) };
            foreach (var won in s.Recent)
                form.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 5, 5), Background = won ? Theme.Win : Theme.B(Theme.LossC, 0.8), ToolTip = won ? "Win" : "Loss" });
            p.Children.Add(form);

            if (d.Mana != null && d.Mana.Turns > 0) p.Children.Add(Mana(d.Mana));

            // Matchups on the left; decks and how early HearthStoner names the opponent's deck on the right.
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.Children.Add(Table("By opponent's class", s.ByClass, r => Theme.ClassColor(r.Class)));
            var right = new StackPanel();
            right.Children.Add(Table("By deck", s.ByDeck, r => Theme.ClassColor(r.Class)));
            var score = Scorecard(PredictionScore.Compute(d.Games));
            ((FrameworkElement)score).Margin = new Thickness(0, 26, 0, 0);
            right.Children.Add(score);
            Grid.SetColumn(right, 2);
            cols.Children.Add(right);
            p.Children.Add(cols);

            // Habits, tilt and recurring review notes, from the stats report (its ladder notes are on the Ladder page).
            var notes = new WrapPanel { Margin = new Thickness(0, 26, 0, 0) };
            foreach (var sec in d.StatsNotes.Where(x => x.Lines.Count > 0 && x.Title != "Ladder"))
            {
                var box = new StackPanel { Width = 380, Margin = new Thickness(0, 0, 30, 22) };
                box.Children.Add(Theme.Heading(sec.Title));
                foreach (var l in sec.Lines)
                foreach (var part in l.Text.Split(new[] { "   " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = Theme.Text(part.Trim(), 13.5, l.Tone == Tone.Warn || l.Tone == Tone.Bad ? Theme.Gold : l.Tone == Tone.Good ? Theme.Win : l.Tone == Tone.Dim ? Theme.Dusk : Theme.Vellum, wrap: true);
                    t.Margin = new Thickness(0, -4, 0, 8);
                    box.Children.Add(t);
                }
                notes.Children.Add(box);
            }
            p.Children.Add(notes);

            return AppShell.Page("Stats", $"{s.Games} finished game{(s.Games == 1 ? "" : "s")} recorded with HearthStoner.", p);
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
            Num(AppShell.Pct(m.UsedRate), "of your mana spent", m.UsedRate >= 0.85 ? Theme.Win : m.UsedRate >= 0.75 ? Theme.Vellum : Theme.Gold);
            Num(AppShell.Pct(m.CurveRate), "turns 1-6 with all of it used", Theme.Vellum);
            Num(m.AvgUnspent.ToString("0.0"), "left over per turn", m.AvgUnspent <= 1 ? Theme.Vellum : Theme.Gold);
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
                    BorderBrush = stall ? Theme.Gold : null, BorderThickness = new Thickness(stall ? 1 : 0) };
                var spent = new Border { Height = barH * r.AvgSpent / maxAvail, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(4), Background = Theme.Mana };
                cell.Children.Add(track);
                cell.Children.Add(spent);
                cell.ToolTip = $"Turn {r.Round}: {r.AvgSpent:0.0} of {r.AvgAvailable:0.0} mana spent on average over {r.Turns} turns; all of it {AppShell.Pct(r.FullRate)} of the time";
                var lbl = Theme.Text(r.Round.ToString(), 11.5, stall ? Theme.Gold : Theme.Dusk);
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
