using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HearthStoner
{
    /// <summary>
    /// Ladder: where you stand, the season's climb chart with the floors, pace to your goals before the reset, the deck
    /// that climbs fastest per hour, star bonus and streaks, opponents' ranks, this session and your best time to play.
    /// </summary>
    public static class LadderPage
    {
        public static UIElement Build(AppShell shell)
        {
            var d = shell.Data;
            var formats = Ladder.From(d.Games);
            var p = new StackPanel();
            var end = d.Quests?.SeasonEnd;
            var endText = QuestLog.SeasonEndsText(end, DateTime.UtcNow);
            var sub = endText != null ? $"{endText} ({end.Value.ToLocalTime():ddd MMM d, h:mm tt})." : "Your ranked climb, season by season.";
            if (formats.Count == 0)
            {
                p.Children.Add(PlayPage.Empty("No ranked games yet", "Play ranked with HearthStoner running: your rank is read as each game starts, and your climb builds up here."));
                return AppShell.Page("Ladder", sub, p);
            }
            var decks = d.Picks.Select(x => (x.Deck.Name, x.Deck.Class, x.Archetype != null ? x.Expected : (double?)null)).ToList();
            foreach (var f in formats)
            {
                var r = LadderReport.Make(f, d.Games, decks, end, DateTime.Now);
                if (formats.Count > 1) p.Children.Add(Theme.Text(f.Name, 22, Theme.Vellum, FontWeights.SemiBold, display: true));
                Format(p, r, d);
            }
            return AppShell.Page("Ladder", sub, p);
        }

        private static void Format(StackPanel p, LadderReport r, AppData d)
        {
            var f = r.Format;
            var season = f.Latest;
            var thisSeason = season.Month == Ladder.SeasonOf(DateTime.Now);
            var legend = f.Current.IsLegend;

            // Headline tiles.
            var tiles = new Grid { Margin = new Thickness(0, 4, 0, 24) };
            for (var i = 0; i < 4; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Tile(int col, string value, string label, Brush color, string note = null)
            {
                var t = new StackPanel();
                t.Children.Add(Theme.Text(value, 34, color, FontWeights.SemiBold, display: true));
                t.Children.Add(Theme.Text(label, 13, Theme.Dusk));
                if (note != null) t.Children.Add(Theme.Text(note, 12, Theme.Dusk, wrap: true));
                var b = new Border { Child = t, BorderBrush = Theme.Line, BorderThickness = new Thickness(col == 0 ? 0 : 1, 0, 0, 0), Padding = new Thickness(col == 0 ? 0 : 22, 0, 12, 0) };
                Grid.SetColumn(b, col);
                tiles.Children.Add(b);
            }
            Tile(0, f.Current.ToString(), thisSeason ? "now" : $"last seen {season.Last.When:MMM d}", Theme.Gold);
            Tile(1, season.Peak.Rank.ToString(), $"season peak, {season.Peak.When:MMM d}", Theme.Vellum, $"started at {season.Start.Rank}");
            Tile(2, legend || r.StarBonus <= 1 ? "none" : $"x{r.StarBonus}", "star bonus",
                r.StarBonus > 1 && !legend ? Theme.Mana : Theme.Dusk,
                legend ? "no stars in Legend" : r.StarBonus <= 1 ? "1 star a win" : r.BonusEndsAt is int e ? $"drops 1 at each floor, gone at {Ladder.Name(e)}" : "lasts to Legend");
            Tile(3, r.Streak == 0 ? "0" : $"{r.Streak} win{(r.Streak == 1 ? "" : "s")}", "win streak", r.Streak >= 2 ? Theme.Win : Theme.Vellum,
                legend || f.Current.Level >= LadderRules.DiamondFive ? "no streak bonus from Diamond 5 up"
                : r.NextWinDoubles ? "your next win counts double" : $"{3 - r.Streak} more in a row to double your stars");
            p.Children.Add(tiles);

            // The climb chart.
            p.Children.Add(Theme.Heading($"{f.Name.Replace("Ranked ", "")} ladder, {season.Name}"));
            var chart = new Canvas { Height = 220, ClipToBounds = true, Margin = new Thickness(0, 2, 0, 0) };
            chart.SizeChanged += (o, e) => DrawLadder(chart, season);
            p.Children.Add(chart);
            var extra = r.BonusStars + r.StreakStars > 0
                ? $" This season your star bonus earned you {r.BonusStars:N0} extra stars and win streaks {r.StreakStars:N0}, on top of 1 a win." : "";
            Note(p, "Each dot is a game, at the rank you started it from (green: won, red: lost). Gold lines are floors (rank 10 and 5 of each league): once you reach one you can't drop below it." + extra, 6, 24);

            // Pace on the left; this session and past seasons on the right.
            var row = Columns(out var left, out var right);
            left.Children.Add(Theme.Heading("Will you make it?"));
            Goals(left, r, f);
            right.Children.Add(Theme.Heading(r.Session?.Current == true ? "This session" : "Last session"));
            Session(right, r, d);
            var past = f.Seasons.Take(f.Seasons.Count - 1).Reverse().Take(6).ToList();
            if (past.Count > 0)
            {
                Caption(right, "Previous seasons");
                foreach (var ps in past) Para(right, $"{ps.Name}: peak {ps.Peak.Rank}, finished {ps.Last.Rank}, {ps.Wins}-{ps.Losses}", Theme.Vellum);
            }
            p.Children.Add(row);

            // Decks on the left; opponents and time of day on the right.
            var row2 = Columns(out var left2, out var right2);
            left2.Children.Add(Theme.Heading("Best deck for climbing"));
            Decks(left2, r, legend);
            right2.Children.Add(Theme.Heading("Your opponents"));
            Opponents(right2, r.Opp);
            Caption(right2, "Best time to play");
            Times(right2, r);
            p.Children.Add(row2);
        }

        // ------------------------------------------------------------------ parts

        private static void Goals(Panel p, LadderReport r, Ladder.Format f)
        {
            if (f.Current.IsLegend) { Para(p, "You're in Legend: your number moves with everyone else's games too, so there's no games-to-go estimate.", Theme.Dusk); return; }
            if (r.Goals.Count == 0) { Para(p, $"Needs at least {LadderReport.MinGames} finished ranked games for a win rate.", Theme.Dusk); return; }
            var head = GoalRow("Goal", "Games", "At your pace", "Needed", Theme.Dusk, Theme.Dusk, Theme.Dusk, 12);
            p.Children.Add(head);
            foreach (var g in r.Goals)
            {
                var games = g.Games.HasValue ? $"~{g.Games:N0}" : "not climbing";
                var eta = !g.Games.HasValue ? "-" : g.EtaDate is DateTime t ? (r.SeasonEnd.HasValue && !g.InTime ? "after the reset" : t.ToString("MMM d")) : "-";
                var need = g.PerDayNeeded is double n ? $"{n:0.#} a day" : "-";
                p.Children.Add(GoalRow(g.Target, games, eta, need, g.Games.HasValue ? Theme.Vellum : Theme.Gold, g.InTime ? Theme.Win : Theme.Gold, Theme.Vellum, 13.5));
            }
            var pace = r.GamesPerDay > 0 ? $" You've played about {r.GamesPerDay:0.#} ranked games a day lately." : "";
            var bonus = r.StarBonus > 1 ? $", your x{r.StarBonus} star bonus (one less at each floor)" : "";
            var basis = r.WinRateThisSeason ? $"this season's {AppShell.Pct(r.WinRate)} win rate ({r.WinRateGames} games)"
                : $"your last {r.WinRateGames} games' {AppShell.Pct(r.WinRate)} win rate (this season's is used from 10 games)";
            Note(p, $"Simulated at {basis}{bonus}, double stars on 3+ win streaks below Diamond 5 and floors every 5 ranks.{pace} A rough guide: a few games swing it.", 6, 0);
        }

        private static Border GoalRow(string a, string b, string c, string d, Brush bb, Brush cb, Brush db, double size)
        {
            var g = new Grid { Height = size > 13 ? 32 : 24 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Cell(int col, string text, Brush br, FontWeight w)
            {
                var t = Theme.Text(text, size, br, w);
                t.VerticalAlignment = VerticalAlignment.Center;
                if (col > 0) t.TextAlignment = TextAlignment.Right;
                Grid.SetColumn(t, col);
                g.Children.Add(t);
            }
            Cell(0, a, size > 13 ? Theme.Vellum : Theme.Dusk, size > 13 ? FontWeights.SemiBold : FontWeights.Normal);
            Cell(1, b, bb, FontWeights.Normal);
            Cell(2, c, cb, size > 13 ? FontWeights.SemiBold : FontWeights.Normal);
            Cell(3, d, db, FontWeights.Normal);
            return new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        }

        private static void Session(Panel p, LadderReport r, AppData d)
        {
            var c = r.Session;
            if (c == null) { Para(p, "No ranked games yet.", Theme.Dusk); return; }
            var s = c.Session;
            var stars = c.NetStars is int n ? $", {(n >= 0 ? "+" : "")}{n} star{(Math.Abs(n) == 1 ? "" : "s")}" : "";
            var tone = c.NetStars > 0 ? Theme.Win : c.NetStars < 0 ? Theme.Loss : Theme.Vellum;
            Para(p, $"{(c.Current ? "" : s.Games[0].Start.ToString("MMM d") + ": ")}{s.Games.Count} game{(s.Games.Count == 1 ? "" : "s")}, {s.Wins}-{s.Losses}{stars}", tone);
            if (s.From.Known && s.To.Known && s.Games.Count > 1) Para(p, $"From {s.From} at its first game to {s.To} at the start of its last.", Theme.Dusk, 12.5);
            if (c.Current && c.LossStreak >= 3)
            {
                var usual = d.Stats?.WinRate ?? 0.5;
                var after = c.AfterThreeLosses is double a
                    ? $" After 3 straight losses you've won {AppShell.Pct(a)} of the next games ({c.AfterThreeLossesGames} games){(a < usual - 0.05 ? $", below your usual {AppShell.Pct(usual)}" : "")}."
                    : "";
                Para(p, $"{c.LossStreak} losses in a row.{after} A short break often helps.", Theme.Gold);
            }
            else if (c.Current && s.Games.Count >= 3 && s.WinRate >= 0.6) Para(p, "Going well: keep it up.", Theme.Dusk, 12.5);
        }

        private static void Decks(Panel p, LadderReport r, bool legend)
        {
            var list = r.Decks.Where(x => x.Best.HasValue || x.Games > 0).Take(6).ToList();
            if (list.Count == 0) { Para(p, "Play a few ranked games: this ranks your decks by stars per hour.", Theme.Dusk); return; }
            var unit = legend ? "wins/hour" : "stars/hour";
            p.Children.Add(DeckRow(null, "Deck", "Record", "Min/game", unit, Theme.Dusk, null, 12));
            foreach (var x in list)
            {
                var rec = x.Games > 0 ? $"{x.Wins}-{x.Games - x.Wins}" : "no games";
                var min = x.Minutes is double m ? $"{m:0}" : "-";
                var fromMeta = x.Games < LadderReport.MinGames && x.MetaStarsPerHour.HasValue;
                var val = x.Best is double b ? $"{b:+0.0;-0.0;0.0}{(fromMeta ? "*" : "")}" : "-";
                p.Children.Add(DeckRow(x.Class, x.Name, rec, min, val, x.Best > 0 ? Theme.Win : x.Best < 0 ? Theme.Loss : Theme.Dusk,
                    x.Expected is double e ? $"Meta expected win rate {AppShell.Pct(e)}" : null, 13.5));
            }
            Note(p, $"Net {(legend ? "wins" : "stars")} an hour: your win rate (streaks included{(legend ? "; none in Legend" : "")}) over your average game length, so a fast deck can out-climb a slower one with a higher win rate. " +
                $"* = under {LadderReport.MinGames} games with it: the meta's expected win rate at your average game length.", 6, 0);
        }

        private static Border DeckRow(string cls, string name, string rec, string min, string val, Brush valBrush, string tip, double size)
        {
            var g = new Grid { Height = size > 13 ? 32 : 24, ToolTip = tip };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            if (cls != null) g.Children.Add(new Border { Width = 4, Height = 18, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.B(Theme.ClassColor(cls)) });
            void Cell(int col, string text, Brush br, FontWeight w)
            {
                var t = Theme.Text(text, size, br, w);
                t.VerticalAlignment = VerticalAlignment.Center;
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                if (col > 1) t.TextAlignment = TextAlignment.Right;
                Grid.SetColumn(t, col);
                g.Children.Add(t);
            }
            var header = size <= 13;
            Cell(1, name, header ? Theme.Dusk : Theme.Vellum, header ? FontWeights.Normal : FontWeights.SemiBold);
            Cell(2, rec, header ? Theme.Dusk : Theme.Vellum, FontWeights.Normal);
            Cell(3, min, Theme.Dusk, FontWeights.Normal);
            Cell(4, val, valBrush, header ? FontWeights.Normal : FontWeights.SemiBold);
            return new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        }

        private static string LevelName(double level) => level >= 50.5 ? "Legend" : Ladder.Name((int)Math.Round(Math.Max(1, level)));

        private static void Opponents(Panel p, LadderReport.Opponents o)
        {
            if (o.Games == 0)
            {
                Para(p, "Shows from your next ranked game: HearthStoner now saves your opponent's rank (the one the game shows you).", Theme.Dusk);
                return;
            }
            Para(p, $"Last {o.Games} game{(o.Games == 1 ? "" : "s")}: opponents around {LevelName(o.AverageTheirs)}, you around {LevelName(o.AverageYours)}.", Theme.Vellum);
            string text;
            Brush tone = Theme.Dusk;
            if (o.Gap >= 5) { text = "Matchmaking pairs players by a hidden rating, not the rank badge. Yours is well above your rank, so you'll climb fast, but expect tougher games than the badge suggests."; tone = Theme.Win; }
            else if (o.Gap <= -5) { text = "Your opponents rank below you: your hidden rating is lower than your rank, so climbing may slow down for a while."; tone = Theme.Gold; }
            else text = "Your opponents are around your rank: your hidden rating matches your badge.";
            Para(p, text, tone, 12.5);
        }

        private static void Times(Panel p, LadderReport r)
        {
            var max = Math.Max(1, r.Times.Max(t => t.Games));
            foreach (var t in r.Times)
            {
                var g = new Grid { Height = 28 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                var n = Theme.Text(t.Name, 13.5, t == r.BestTime ? Theme.Vellum : Theme.Dusk, t == r.BestTime ? FontWeights.SemiBold : FontWeights.Normal);
                n.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(n);
                var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                    Background = t.Games >= LadderReport.MinGames ? Theme.RateBrush(t.WinRate) : Theme.Dusk };
                var track = new Grid();
                track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Line, VerticalAlignment = VerticalAlignment.Center });
                track.Children.Add(bar);
                track.SizeChanged += (o, e) => bar.Width = Math.Max(t.Games > 0 ? 8 : 0, e.NewSize.Width * t.Games / max);
                Grid.SetColumn(track, 1);
                g.Children.Add(track);
                var v = Theme.Text(t.Games == 0 ? "no games" : $"{AppShell.Pct(t.WinRate)} of {t.Games}", 13, t.Games >= LadderReport.MinGames ? Theme.RateBrush(t.WinRate) : Theme.Dusk, FontWeights.SemiBold);
                v.TextAlignment = TextAlignment.Right; v.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(v, 2);
                g.Children.Add(v);
                p.Children.Add(g);
            }
            Note(p, r.BestTime != null
                ? $"You win most in the {r.BestTime.Name.ToLowerInvariant()}. Ranked and casual games, by the hour they started; bar length is how many games."
                : $"Needs {LadderReport.MinGames}+ ranked or casual games in at least two of these to compare. Bar length is how many games.", 6, 0);
        }

        // ------------------------------------------------------------------ layout helpers

        private static Grid Columns(out StackPanel left, out StackPanel right)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 28) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            left = new StackPanel();
            right = new StackPanel();
            row.Children.Add(left);
            Grid.SetColumn(right, 2);
            row.Children.Add(right);
            return row;
        }

        private static void Caption(Panel p, string text)
        {
            var t = Theme.Text(text, 13, Theme.Dusk, FontWeights.SemiBold);
            t.Margin = new Thickness(0, 14, 0, 6);
            p.Children.Add(t);
        }

        private static void Para(Panel p, string text, Brush b, double size = 13.5)
        {
            var t = Theme.Text(text, size, b, wrap: true);
            t.Margin = new Thickness(0, 0, 0, 4);
            p.Children.Add(t);
        }

        private static void Note(Panel p, string text, double top, double bottom)
        {
            var t = Theme.Text(text, 12, Theme.Dusk, wrap: true);
            t.Margin = new Thickness(0, top, 0, bottom);
            p.Children.Add(t);
        }

        /// <summary>A season's ranks game by game: star levels one step a rank (floors in gold), Legend above on a log scale.</summary>
        private static void DrawLadder(Canvas c, Ladder.Season s)
        {
            c.Children.Clear();
            double w = c.ActualWidth, full = c.Height;
            var pts = s.Points;
            if (w <= 0 || pts.Count == 0) return;
            const double left = 86, right = 10, top = 10, bottom = 24;
            var h = full - top - bottom;
            var cap = Ladder.LegendCap(pts.Select(x => x.Rank));
            var hs = pts.Select(x => Ladder.Height(x.Rank, cap)).ToList();
            double min = hs.Min(), max = hs.Max(), span = max - min;
            var pad = span > 0 ? span * 0.12 : pts.All(x => x.Rank.IsLegend) ? 0.05 : 1;
            double lo = min - pad, hi = max + pad;
            double X(int i) => pts.Count == 1 ? (left + w - right) / 2 : left + i * (w - left - right) / (pts.Count - 1);
            double Y(double v) => top + (hi - v) / (hi - lo) * h;

            void Text(string text, double x, double y, Brush b, TextAlignment align)
            {
                var t = Theme.Text(text, 11.5, b);
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(t, align == TextAlignment.Right ? x - t.DesiredSize.Width : align == TextAlignment.Center ? x - t.DesiredSize.Width / 2 : x);
                Canvas.SetTop(t, y);
                c.Children.Add(t);
            }

            // Floors in gold, labelled first; then the other gridlines, labelled where there's still room.
            var labelled = new System.Collections.Generic.List<double>();
            bool Room(double y) => labelled.All(o => Math.Abs(o - y) >= 16);
            for (var l = Math.Max(1, (int)Math.Ceiling(lo)); l <= Math.Min(50, hi); l++)
            {
                if (!LadderRules.IsFloor(l)) continue;
                var y = Y(l);
                c.Children.Add(new Line { X1 = left, X2 = w - right, Y1 = y, Y2 = y, Stroke = Theme.B(Theme.GoldC, 0.45), StrokeThickness = 1 });
                if (!Room(y)) continue;
                Text(Ladder.Name(l), left - 10, y - 8, Theme.B(Theme.GoldC, 0.8), TextAlignment.Right);
                labelled.Add(y);
            }
            foreach (var (th, label) in Ladder.Ticks(lo, hi, cap).OrderByDescending(t => t.H))
            {
                if (th < 51 && LadderRules.IsFloor((int)th) && th == Math.Floor(th)) continue;
                var y = Y(th);
                c.Children.Add(new Line { X1 = left, X2 = w - right, Y1 = y, Y2 = y, Stroke = Theme.Line, StrokeThickness = 1 });
                if (!Room(y)) continue;
                Text(label, left - 10, y - 8, Theme.Dusk, TextAlignment.Right);
                labelled.Add(y);
            }

            // Where the season started.
            var sy = Y(hs[0]);
            c.Children.Add(new Line { X1 = left, X2 = w - right, Y1 = sy, Y2 = sy, Stroke = Theme.Dusk, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } });
            Text("season start", w - right, sy < top + h / 2 ? sy + 3 : sy - 17, Theme.Dusk, TextAlignment.Right);

            var line = new Polyline { Stroke = Theme.Gold, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            for (var i = 0; i < pts.Count; i++) line.Points.Add(new Point(X(i), Y(hs[i])));
            c.Children.Add(line);
            for (var i = 0; i < pts.Count; i++)
            {
                var g = pts[i].Game;
                var opp = g.OpponentLegendRank > 0 || g.OpponentStarLevel > 0 ? $", vs {Ladder.Name(g.OpponentStarLevel, g.OpponentLegendRank)}" : "";
                var dot = new Ellipse { Width = 8, Height = 8, Fill = g.Won ? Theme.Win : g.Result == "LOST" ? Theme.Loss : Theme.Dusk, Stroke = Theme.Night, StrokeThickness = 1.5,
                    ToolTip = $"{pts[i].When:MMM d, h:mm tt}: {pts[i].Rank}, {(g.Won ? "won" : g.Result == "LOST" ? "lost" : "no result")}{opp}" };
                Canvas.SetLeft(dot, X(i) - 4); Canvas.SetTop(dot, Y(hs[i]) - 4);
                c.Children.Add(dot);
            }
            Text($"{pts[0].When:MMM d}", left, top + h + 6, Theme.Dusk, TextAlignment.Left);
            if (pts.Count > 1) Text($"{pts[pts.Count - 1].When:MMM d}", w - right, top + h + 6, Theme.Dusk, TextAlignment.Right);
        }
    }
}
