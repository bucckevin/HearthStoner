using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HearthStoner.Pages
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
                Format(p, r, d, shell.Size);
            }
            return AppShell.Page("Ladder", sub, p);
        }

        private static void Format(StackPanel p, LadderReport r, AppData d, PageWidth size)
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
                ? $" Extra stars this season: {r.BonusStars:N0} from your star bonus and {r.StreakStars:N0} from win streaks, on top of the 1 star each win gives." : "";
            Note(p, "Each dot is a game, at the rank you started it from (green: won, red: lost). Gold lines are floors (rank 10 and 5 of each league): once you reach one you can't drop below it." + extra, 6, 24);

            // Pace, this session, decks and opponents: four blocks laid out to fit the window.
            var pace = new StackPanel();
            pace.Children.Add(Theme.Heading("Will you make it?"));
            Goals(pace, r, f);
            var session = new StackPanel();
            session.Children.Add(Theme.Heading(r.Session?.Current == true ? "This session" : "Last session"));
            Session(session, r, d);
            var past = f.Seasons.Take(f.Seasons.Count - 1).Reverse().Take(6).ToList();
            if (past.Count > 0)
            {
                Caption(session, "Previous seasons");
                foreach (var ps in past) Para(session, $"{ps.Name}: peak {ps.Peak.Rank}, finished {ps.Last.Rank}, {ps.Wins}-{ps.Losses}", Theme.Vellum);
            }
            var climb = new StackPanel();
            climb.Children.Add(Theme.Heading("Best deck for climbing"));
            Decks(climb, r, legend, d);
            var opponents = new StackPanel();
            opponents.Children.Add(Theme.Heading("Your opponents"));
            Opponents(opponents, r.Opp);
            Caption(opponents, "Best time to play");
            Times(opponents, r);
            p.Children.Add(Blocks(size, pace, session, climb, opponents));
        }

        /// <summary>
        /// Wide: pace, decks and opponents side by side with this session under pace. Standard: two rows of two.
        /// Compact: one under the other.
        /// </summary>
        private static UIElement Blocks(PageWidth size, UIElement pace, UIElement session, UIElement climb, UIElement opponents)
        {
            UIElement layout;
            switch (size)
            {
                case PageWidth.Wide:
                    layout = PageLayout.Columns((PageLayout.Stack(28, pace, session), 0), (climb, 0), (opponents, 0));
                    break;
                case PageWidth.Compact:
                    layout = PageLayout.Stack(28, pace, session, climb, opponents);
                    break;
                default:
                    layout = PageLayout.Stack(28, PageLayout.Columns((pace, 0), (session, 0)), PageLayout.Columns((climb, 0), (opponents, 0)));
                    break;
            }
            ((FrameworkElement)layout).Margin = new Thickness(0, 0, 0, 28);
            return layout;
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
                Para(p, $"{c.LossStreak} losses in a row.{after} A short break often helps.", Theme.Amber);
            }
            else if (c.Current && s.Games.Count >= 3 && s.WinRate >= 0.6) Para(p, "Going well: keep it up.", Theme.Dusk, 12.5);
        }

        private static void Decks(Panel p, LadderReport r, bool legend, AppData d)
        {
            // Decks you've played first; an unplayed deck of the same archetype as one above it (its estimate is the
            // archetype's, so the same number) is merged into it, as on the Play page.
            var cardsByName = d.Picks.Where(x => !x.Deck.Imported).GroupBy(x => x.Deck.Name).ToDictionary(g => g.Key,
                g => g.First().Archetype != null ? "a:" + g.First().Archetype.Id : "c:" + DeckVersions.Key(g.First().Deck.Cards));
            var rows = new System.Collections.Generic.List<(LadderReport.DeckClimb Deck, int Copies)>();
            foreach (var x in r.Decks.Where(x => x.Best.HasValue || x.Games > 0).OrderByDescending(x => x.Games > 0))
            {
                var key = cardsByName.TryGetValue(x.Name ?? "", out var k) ? k : null;
                var same = x.Games == 0 && key != null ? rows.FindIndex(o => cardsByName.TryGetValue(o.Deck.Name ?? "", out var ok) && ok == key) : -1;
                if (same >= 0) rows[same] = (rows[same].Deck, rows[same].Copies + 1);
                else rows.Add((x, 0));
            }
            var list = rows.Take(6).ToList();
            if (list.Count == 0) { Para(p, "Play a few ranked games: this ranks your decks by stars per hour.", Theme.Dusk); return; }
            var unit = legend ? "wins/hour" : "stars/hour";
            p.Children.Add(DeckRow(null, "Deck", "Record", "Min/game", unit, Theme.Dusk, null, 12, false));
            foreach (var (x, copies) in list)
            {
                var played = x.Games > 0;
                var rec = played ? $"{x.Wins}-{x.Games - x.Wins}" : "—";
                var min = x.Minutes is double m ? $"{m:0}" : "—";
                var fromMeta = x.Games < LadderReport.MinGames && x.MetaStarsPerHour.HasValue;
                var val = x.Best is double b ? $"{b:+0.0;-0.0;0.0}" : "—";
                var name = copies > 0 ? $"{x.Name}  +{copies} more" : x.Name;
                p.Children.Add(DeckRow(x.Class, name, rec, min, val, !played ? Theme.Mist : x.Best > 0 ? Theme.Win : x.Best < 0 ? Theme.Loss : Theme.Dusk,
                    x.Expected is double e ? $"Meta expected win rate {AppShell.Pct(e)}" : null, 13.5, fromMeta, dim: !played));
            }
            Note(p, $"Net {(legend ? "wins" : "stars")} an hour: your win rate (streaks included{(legend ? "; none in Legend" : "")}) over your average game length, so a fast deck can out-climb a slower one with a higher win rate. " +
                $"\"meta\" = under {LadderReport.MinGames} games with it: the meta's expected win rate at your average game length.", 6, 0);
        }

        private static Border DeckRow(string cls, string name, string rec, string min, string val, Brush valBrush, string tip, double size, bool metaTag, bool dim = false)
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
            var text = header ? Theme.Dusk : dim ? Theme.Mist : Theme.Vellum;
            Cell(1, name, text, header || dim ? FontWeights.Normal : FontWeights.SemiBold);
            Cell(2, rec, text, FontWeights.Normal);
            Cell(3, min, header ? Theme.Dusk : Theme.Mist, FontWeights.Normal);
            if (metaTag)
            {
                // The estimate is explained where it's shown: a small "meta" tag after the number.
                var cell = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                cell.Children.Add(Theme.Text(val, size, valBrush, FontWeights.SemiBold));
                cell.Children.Add(new Border
                {
                    Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(5, 0, 5, 1), CornerRadius = new CornerRadius(3), Background = Theme.Raised, VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "meta", FontSize = 11, Foreground = Theme.Mist },
                });
                Grid.SetColumn(cell, 4);
                g.Children.Add(cell);
            }
            else Cell(4, val, valBrush, header ? FontWeights.Normal : FontWeights.SemiBold);
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
            else if (o.Gap <= -5) { text = "Your opponents rank below you: your hidden rating is lower than your rank, so climbing may slow down for a while."; tone = Theme.Amber; }
            else text = "Your opponents are around your rank: your hidden rating matches your badge.";
            Para(p, text, tone, 12.5);
        }

        private static void Times(Panel p, LadderReport r)
        {
            foreach (var t in r.Times)
            {
                var enough = t.Games >= LadderReport.MinGames;
                var g = new Grid { Height = 28 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                var n = Theme.Text(t.Name, 13.5, t == r.BestTime ? Theme.Vellum : enough ? Theme.Mist : Theme.Dusk, t == r.BestTime ? FontWeights.SemiBold : FontWeights.Normal);
                n.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(n);
                // Bar length is the win rate (easy to compare); too few games get a dashed track and no percentage.
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
            Note(p, r.BestTime != null
                ? $"You win most in the {r.BestTime.Name.ToLowerInvariant()}. Ranked and casual games, by the hour they started; bar length is the win rate."
                : $"Needs {LadderReport.MinGames}+ ranked or casual games in at least two of these to compare. Bar length is the win rate.", 6, 0);
        }

        // ------------------------------------------------------------------ layout helpers

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
