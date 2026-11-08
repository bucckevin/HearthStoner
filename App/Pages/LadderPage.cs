using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HearthStoner.Pages
{
    /// <summary>
    /// Ladder, one format at a time (tabs when you play more than one): your rank card with the way to the next floor,
    /// the season's climb chart, pace to your goals, the deck that climbs fastest per hour, your sessions and your
    /// opponents' ranks. A format with only a game or two gets a short version until there's enough to show.
    /// </summary>
    public static class LadderPage
    {
        /// <summary>Below this many ranked games in the season, a format gets the short version.</summary>
        public const int FullFrom = 3;

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
            var f = Ladder.Pick(formats, shell.LadderFormat, DateTime.Now);
            // The meta data is Standard's: other formats get your own record only.
            var decks = f.FormatType == "FT_STANDARD"
                ? d.Picks.Select(x => (x.Deck.Name, x.Deck.Class, x.Archetype != null ? x.Expected : (double?)null)).ToList()
                : new System.Collections.Generic.List<(string, string, double?)>();
            var r = LadderReport.Make(f, d.Games, decks, end, DateTime.Now);
            if (f.Latest.Games.Count < FullFrom) Short(p, r);
            else Full(p, r, d, shell.Size);
            var tabs = formats.Count > 1
                ? formats.OrderBy(x => x.FormatType == "FT_STANDARD" ? 0 : 1).ThenBy(x => x.Name)
                    .Select(x => (UIElement)AppShell.Tab(Ladder.Short(x), TabCount(x), x == f,
                        () => { shell.LadderFormat = x.FormatType; shell.Redraw(); })).ToArray()
                : new UIElement[0];
            return AppShell.Page("Ladder", sub, p, tabs);
        }

        /// <summary>A tab's note: "17 games" this season, or "last Oct" for a format not played since an earlier season.</summary>
        private static string TabCount(Ladder.Format f)
        {
            if (f.Latest.Month != Ladder.SeasonOf(DateTime.Now)) return "last " + f.Latest.Month.ToString("MMM");
            var n = f.Latest.Games.Count;
            return $"{n} game{(n == 1 ? "" : "s")}";
        }

        private static void Full(StackPanel p, LadderReport r, AppData d, PageWidth size)
        {
            var f = r.Format;
            var season = f.Latest;
            var legend = f.Current.IsLegend;
            p.Children.Add(RankCard(r, size));

            // The climb chart: shorter than before, and saying why games are missing from it.
            p.Children.Add(Theme.Heading($"{Ladder.Short(f)} climb, {season.Name}"));
            var chart = new Canvas { Height = 150, ClipToBounds = true, Margin = new Thickness(0, 2, 0, 0) };
            chart.SizeChanged += (o, e) => DrawLadder(chart, season);
            p.Children.Add(chart);
            var unranked = season.Games.Count - season.Points.Count;
            var missing = unranked > 0
                ? $" {unranked} game{(unranked == 1 ? " this season isn't" : "s this season aren't")} on it: added from Hearthstone's logs, which don't say your rank." : "";
            var extra = r.BonusStars + r.StreakStars > 0
                ? $" Extra stars this season: {r.BonusStars:N0} from your star bonus and {r.StreakStars:N0} from win streaks." : "";
            Note(p, "Each dot is a game at the rank you started it from (filled green: won, hollow red: lost); gold lines are floors you can't drop below." + missing + extra, 6, 24);

            // Pace, sessions, decks and opponents: four blocks laid out to fit the window.
            var pace = new StackPanel();
            pace.Children.Add(Theme.Heading("Will you make it?"));
            Goals(pace, r, f);
            var sessions = new StackPanel();
            sessions.Children.Add(Theme.Heading("Sessions"));
            Session(sessions, r, d);
            var past = f.Seasons.Take(f.Seasons.Count - 1).Reverse().Take(6).ToList();
            if (past.Count > 0)
            {
                Caption(sessions, "Previous seasons");
                foreach (var ps in past) Para(sessions, $"{ps.Name}: peak {ps.Peak.Rank}, finished {ps.Last.Rank}, {ps.Wins}-{ps.Losses}", Theme.Vellum);
            }
            var climb = new StackPanel();
            climb.Children.Add(Theme.Heading("Best deck for climbing"));
            Decks(climb, r, legend, d);
            var opponents = new StackPanel();
            opponents.Children.Add(Theme.Heading("Your opponents"));
            Opponents(opponents, r.Opp);
            p.Children.Add(Blocks(size, pace, sessions, climb, opponents));
        }

        /// <summary>
        /// A format with fewer than <see cref="FullFrom"/> ranked games this season: a small rank card with the season in
        /// a sentence, what the rest needs, and your own record with each deck.
        /// </summary>
        private static void Short(StackPanel p, LadderReport r)
        {
            var f = r.Format;
            var name = Ladder.Short(f);
            var season = f.Latest;
            var card = new Grid();
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(AppShell.ColumnLabel($"Your {name} rank"));
            left.Children.Add(Theme.Text(f.Current.ToString(), 40, Theme.Gold, FontWeights.SemiBold, display: true));
            card.Children.Add(left);
            var n = season.Games.Count;
            var when = season.Month == Ladder.SeasonOf(DateTime.Now) ? "this season" : $"in {season.Name}";
            var stars = LadderReport.NetStars(season.Games, season.Games) is int s ? $", {(s >= 0 ? "+" : "")}{s} star{(Math.Abs(s) == 1 ? "" : "s")}" : "";
            var bonus = !f.Current.IsLegend && r.StarBonus > 1
                ? $" Your x{r.StarBonus} star bonus {(r.BonusEndsAt is int e ? $"lasts until {Ladder.Name(e)}" : "lasts to Legend")}." : "";
            var line = Theme.Text($"{n} game{(n == 1 ? "" : "s")} {when}, {season.Wins}-{season.Losses}{stars}.{bonus}", 14, Theme.Vellum, wrap: true);
            line.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(line, 1);
            card.Children.Add(line);
            p.Children.Add(Box(card, new Thickness(0, 4, 0, 16)));

            var more = new StackPanel();
            more.Children.Add(Theme.Text($"The climb chart, pace and best deck need a few more {name} games", 14.5, Theme.Vellum, FontWeights.SemiBold, wrap: true));
            more.Children.Add(Theme.Text($"Shown from {FullFrom} ranked {name} games this season (pace needs {LadderReport.MinGames} finished). {name} games so far: {n}.", 12.5, Theme.Dusk, wrap: true));
            var box = Box(more, new Thickness(0, 0, 0, 24));
            box.BorderBrush = Theme.Line;
            box.Background = Brushes.Transparent;
            p.Children.Add(box);

            p.Children.Add(Theme.Heading($"Your decks in {name}"));
            var played = r.Decks.Where(x => x.Games > 0).Take(6).ToList();
            if (played.Count == 0) { Para(p, $"No {name} games with a saved deck yet.", Theme.Dusk); return; }
            var table = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
            table.Children.Add(DeckRow(null, "Deck", "Record", "Min/game", "", Theme.Dusk, null, 12, false));
            foreach (var x in played)
                table.Children.Add(DeckRow(x.Class, x.Name, $"{x.Wins}-{x.Games - x.Wins}", x.Minutes is double m ? $"{m:0}" : "—", "", Theme.Dusk, null, 13.5, false));
            if (f.FormatType != "FT_STANDARD")
                Note(table, $"No meta estimates here: HearthStoner's meta data is for Standard. Your own {name} record only.", 6, 0);
            p.Children.Add(table);
        }

        /// <summary>
        /// Your rank, its stars and how far it is to the next floor, with the star bonus and win streak beside it (under
        /// it in a small window).
        /// </summary>
        private static UIElement RankCard(LadderReport r, PageWidth size)
        {
            var f = r.Format;
            var season = f.Latest;
            var cur = f.Current;
            var legend = cur.IsLegend;
            var thisSeason = season.Month == Ladder.SeasonOf(DateTime.Now);

            var rank = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 28, 0) };
            rank.Children.Add(AppShell.ColumnLabel(thisSeason ? "Your rank" : $"Your rank, last seen {season.Last.When:MMM d}"));
            rank.Children.Add(Theme.Text(cur.ToString(), 44, Theme.Gold, FontWeights.SemiBold, display: true));
            if (!legend) rank.Children.Add(Stars(cur.Stars));
            if (season.Peak.Rank.Score > cur.Score) rank.Children.Add(Theme.Text($"season peak {season.Peak.Rank}, {season.Peak.When:MMM d}", 12, Theme.Dusk));

            var way = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (legend)
                way.Children.Add(Theme.Text("You're in Legend: your number moves with everyone else's games too, so there's no star count to the next step.", 13, Theme.Mist, wrap: true));
            else
            {
                var (from, to, fraction) = LadderRules.FloorProgress(cur.Level, cur.Stars);
                var labels = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var right = Theme.Text($"{Ladder.Name(to)}{(to < LadderRules.LegendLevel ? " · floor" : "")}", 12.5, Theme.Gold);
                DockPanel.SetDock(right, Dock.Right);
                labels.Children.Add(right);
                labels.Children.Add(Theme.Text($"{Ladder.Name(from)} · floor", 12.5, Theme.Dusk));
                way.Children.Add(labels);
                way.Children.Add(AppShell.StatBar(fraction, Theme.Gold, 10));
                var next = r.Goals.FirstOrDefault(g => g.Level == to) ?? r.Goals.FirstOrDefault();
                var pace = next == null ? $"Needs {LadderReport.MinGames} finished ranked games for a pace estimate."
                    : next.Games is int gms ? $"About {gms} game{(gms == 1 ? "" : "s")} to {next.Target} at your pace."
                    : $"Not climbing toward {next.Target} at your current win rate.";
                var bonus = r.StarBonus > 1 ? $" Each win is worth {r.StarBonus} stars with your bonus." : "";
                var t = Theme.Text(pace + bonus, 12.5, Theme.Mist, wrap: true);
                t.Margin = new Thickness(0, 8, 0, 0);
                way.Children.Add(t);
            }

            var chips = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            chips.Children.Add(Fact("Star bonus", legend || r.StarBonus <= 1 ? "none" : $"x{r.StarBonus}", r.StarBonus > 1 && !legend ? Theme.Mana : Theme.Dusk,
                legend ? "no stars in Legend" : r.StarBonus <= 1 ? "1 star a win" : r.BonusEndsAt is int e ? $"gone at {Ladder.Name(e)}" : "lasts to Legend"));
            chips.Children.Add(Fact("Win streak", r.Streak.ToString(), r.Streak >= 2 ? Theme.Win : Theme.Vellum,
                legend || cur.Level >= LadderRules.DiamondFive ? "no streak bonus from Diamond 5 up"
                : r.NextWinDoubles ? "your next win counts double" : $"{3 - r.Streak} more in a row doubles stars"));

            UIElement body;
            if (size == PageWidth.Compact)
            {
                body = PageLayout.Stack(16, rank, way, chips);
            }
            else
            {
                // The bar stops at a readable length on a wide screen; the facts stay beside it.
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 820 });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
                g.Children.Add(rank);
                Grid.SetColumn(way, 1);
                g.Children.Add(way);
                chips.Margin = new Thickness(28, 0, 0, 0);
                Grid.SetColumn(chips, 2);
                g.Children.Add(chips);
                body = g;
            }
            return Box(body, new Thickness(0, 4, 0, 26));
        }

        /// <summary>Three star pips, the ones you have in this rank filled.</summary>
        private static UIElement Stars(int have)
        {
            have = Math.Clamp(have, 0, 2);   // the third star ranks you up, as the rest of the ladder counts it
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4), ToolTip = $"{have} of 3 stars in this rank" };
            for (var i = 0; i < 3; i++)
            {
                var star = new Polygon
                {
                    Points = new PointCollection { new Point(8, 1), new Point(10, 6), new Point(15, 6), new Point(11, 9.5), new Point(12.5, 15), new Point(8, 12), new Point(3.5, 15), new Point(5, 9.5), new Point(1, 6), new Point(6, 6) },
                    Fill = i < have ? Theme.Gold : Brushes.Transparent, Stroke = i < have ? Theme.Gold : Theme.Dusk, StrokeThickness = 1.2,
                    Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0),
                };
                row.Children.Add(star);
            }
            return row;
        }

        /// <summary>A small boxed fact: a grey label, a value and a note under it.</summary>
        private static Border Fact(string label, string value, Brush valueBrush, string note)
        {
            var s = new StackPanel();
            s.Children.Add(AppShell.ColumnLabel(label));
            s.Children.Add(Theme.Text(value, 22, valueBrush, FontWeights.SemiBold, display: true));
            s.Children.Add(Theme.Text(note, 12, Theme.Dusk));
            return new Border
            {
                Child = s, Padding = new Thickness(14, 8, 14, 9), Margin = new Thickness(0, 0, 0, 8), MinWidth = 180,
                CornerRadius = new CornerRadius(10), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Background = Theme.B(Theme.NightC, 0.6),
            };
        }

        /// <summary>A rounded panel around a block of the page.</summary>
        private static Border Box(UIElement child, Thickness margin) => new Border
        {
            Child = child, Padding = new Thickness(20, 16, 20, 16), Margin = margin, CornerRadius = new CornerRadius(12),
            BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Background = Theme.B(Theme.NightC, 0.6),
        };

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
            var head = GoalRow("Goal", "Games", "At your pace", Theme.Dusk, Theme.Dusk, 12);
            p.Children.Add(head);
            foreach (var g in r.Goals)
            {
                var games = g.Games.HasValue ? $"~{g.Games:N0}" : "not climbing";
                var eta = !g.Games.HasValue ? "-" : g.EtaDate is DateTime t ? (r.SeasonEnd.HasValue && !g.InTime ? "after the reset" : t.ToString("MMM d")) : "-";
                var row = GoalRow(g.Target, games, eta, g.Games.HasValue ? Theme.Vellum : Theme.Gold, g.InTime ? Theme.Win : Theme.Gold, 13.5);
                // How many games a day it takes before the reset: rarely matters early in a season, so it's on hover.
                if (g.PerDayNeeded is double n) row.ToolTip = $"{n:0.#} games a day to get there before the season ends";
                p.Children.Add(row);
            }
            var pace = r.GamesPerDay > 0 ? $" You've played about {r.GamesPerDay:0.#} ranked games a day lately." : "";
            var bonus = r.StarBonus > 1 ? $", your x{r.StarBonus} star bonus (one less at each floor)" : "";
            var basis = r.WinRateThisSeason ? $"this season's {AppShell.Pct(r.WinRate)} win rate ({r.WinRateGames} games)"
                : $"your last {r.WinRateGames} games' {AppShell.Pct(r.WinRate)} win rate (this season's is used from 10 games)";
            Note(p, $"Simulated at {basis}{bonus}, double stars on 3+ win streaks below Diamond 5 and floors every 5 ranks.{pace} A rough guide: a few games swing it.", 6, 0);
        }

        private static Border GoalRow(string a, string b, string c, Brush bb, Brush cb, double size)
        {
            var g = new Grid { Height = size > 13 ? 32 : 24, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
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
            var standard = r.Format.FormatType == "FT_STANDARD";
            Note(p, $"Net {(legend ? "wins" : "stars")} an hour: your win rate (streaks included{(legend ? "; none in Legend" : "")}) over your average game length, so a fast deck can out-climb a slower one with a higher win rate. " +
                (standard ? $"\"meta\" = under {LadderReport.MinGames} games with it: the meta's expected win rate at your average game length."
                          : $"Your {Ladder.Short(r.Format)} games only: HearthStoner's meta data is for Standard, so there are no estimates."), 6, 0);
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
                Para(p, "Their ranks show after a few ranked games with HearthStoner running.", Theme.Dusk);
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
                // Won: a filled dot. Lost: a hollow ring. The shape carries the result, not just green against red.
                var lost = g.Result == "LOST";
                var dot = new Ellipse
                {
                    Width = 8, Height = 8, ToolTip = $"{pts[i].When:MMM d, h:mm tt}: {pts[i].Rank}, {(g.Won ? "won" : lost ? "lost" : "no result")}{opp}",
                    Fill = lost ? Theme.Night : g.Won ? Theme.Win : Theme.Dusk,
                    Stroke = lost ? Theme.Loss : Theme.Night, StrokeThickness = lost ? 2 : 1.5,
                };
                Canvas.SetLeft(dot, X(i) - 4); Canvas.SetTop(dot, Y(hs[i]) - 4);
                c.Children.Add(dot);
            }
            Text($"{pts[0].When:MMM d}", left, top + h + 6, Theme.Dusk, TextAlignment.Left);
            if (pts.Count > 1) Text($"{pts[pts.Count - 1].When:MMM d}", w - right, top + h + 6, Theme.Dusk, TextAlignment.Right);
        }
    }
}
