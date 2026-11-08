using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HearthStoner.Pages
{
    /// <summary>
    /// Play: your decks ranked by expected win rate against today's field. The best one is a large banner of its
    /// signature card's art; the rest are slimmer banners. On the right: the class mix of the field and what to craft.
    /// </summary>
    public static class PlayPage
    {
        public static UIElement Build(AppShell shell)
        {
            var d = shell.Data;
            var bracket = d.Meta != null ? AppData.RankBracketName(d.Meta.Rank) : "your rank";
            var left = new StackPanel();
            var all = d.Picks.Where(p => !p.Deck.Imported).ToList();   // pasted-in decks are analyzed on Decks only
            // Decks with the same 40 cards share one row: four rows of the same 55.0% read like a bug.
            var copies = SameLists(all);
            var mine = all.Where(copies.ContainsKey).ToList();
            var matched = mine.Where(p => p.Archetype != null).ToList();
            if (mine.Count == 0)
                left.Children.Add(Empty("No decks yet", "Open Hearthstone once. It writes your saved decks to its log, and they'll show up here."));
            else if (matched.Count > 0)
                left.Children.Add(Hero(matched[0], d, copies[matched[0]]));
            foreach (var p in mine.Where(p => p != matched.FirstOrDefault()))
                left.Children.Add(Row(p, shell, copies[p]));
            if (d.Meta?.Archetypes.Count > 0) left.Children.Add(MetaChanges(d, copies.Values.SelectMany(c => c).ToList()));
            if (d.Session.Recent.Count > 0) left.Children.Add(Session(shell));

            var wide = shell.Size == PageWidth.Wide;
            var field = matched.Count > 0 ? Field(matched[0], d, allClasses: wide) : null;
            var body = Arrange(shell.Size, left, field, Quests(d), Crafting(shell));

            var when = MetaStats.PartOfDay(DateTime.Now);
            var title = when == "tonight" ? "Tonight's queue" : char.ToUpper(when[0]) + when.Substring(1) + "'s queue";
            return AppShell.Page(mine.Count > 0 ? title : "Play",
                $"Your decks, ranked by expected win rate against today's {bracket} field.", body,
                new GameButton(shell),
                Theme.Button("Refresh", () => shell.ReloadRequested?.Invoke(), primary: false));
        }

        /// <summary>
        /// Wide: decks | the field | quests and crafting, so nothing waits below the fold. Standard: decks with a
        /// narrow side column. Compact: everything in one column, decks first.
        /// </summary>
        private static UIElement Arrange(PageWidth size, UIElement decks, UIElement field, UIElement quests, UIElement craft)
        {
            switch (size)
            {
                case PageWidth.Wide:
                    return PageLayout.Columns((decks, -1.5), (field, -1), (PageLayout.Stack(0, quests, craft), -1));
                case PageWidth.Compact:
                    return PageLayout.Stack(24, decks, field, quests, craft);
                default:
                    return PageLayout.Columns((decks, 0), (PageLayout.Stack(0, field, quests, craft), 330));
            }
        }

        /// <summary>
        /// The first deck of each archetype (in the given order), mapped to the names of your other decks of that
        /// archetype. The expected win rate comes from the archetype, so those decks would show the same number;
        /// decks that match no archetype group only with exact copies.
        /// </summary>
        public static Dictionary<DeckPlanner.DeckPick, List<string>> SameLists(IEnumerable<DeckPlanner.DeckPick> picks)
        {
            var result = new Dictionary<DeckPlanner.DeckPick, List<string>>();
            foreach (var g in picks.GroupBy(p => p.Archetype != null ? "a:" + p.Archetype.Id : "c:" + DeckVersions.Key(p.Deck.Cards)))
            {
                var list = g.ToList();
                result[list[0]] = list.Skip(1).Select(p => p.Deck.Name).ToList();
            }
            return result;
        }

        /// <summary>"End Of Turnadin · +3 more", with the other decks' names as a tooltip.</summary>
        private static TextBlock SubtitleWithCopies(string sub, List<string> copies, double size)
        {
            var t = Theme.Text(sub, size, Theme.Dusk);
            if (copies.Count == 0) return t;
            t.Inlines.Add(new System.Windows.Documents.Run($"  ·  +{copies.Count} more") { Foreground = Theme.Vellum });
            t.ToolTip = "Your other decks of this archetype (same expected win rate): " + string.Join(", ", copies);
            return t;
        }

        // ------------------------------------------------------------------ the best deck

        private static UIElement Hero(DeckPlanner.DeckPick p, AppData d, List<string> copies)
        {
            var into = Theme.Rgb(0x19, 0x14, 0x30);
            var g = new Grid { Height = 208 };
            g.Children.Add(AppShell.ArtBanner(p.SignatureCardId, into, 430, 1.0, 0.0, 0.62));
            var body = new Grid { Margin = new Thickness(28, 22, 22, 22) };
            var top = new StackPanel();
            top.Children.Add(Theme.Text("Best pick", 13, Theme.Gold, FontWeights.SemiBold));
            var name = Theme.Text(p.Deck.Name, 34, Theme.Vellum, FontWeights.SemiBold, display: true);
            name.Margin = new Thickness(0, 4, 0, 0);
            top.Children.Add(name);
            top.Children.Add(SubtitleWithCopies(p.Archetype.Name, copies, 13.5));
            body.Children.Add(top);

            var bottom = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom, LastChildFill = false };
            // The button docks first, so a narrow window squeezes the chips instead of cutting the button off.
            var copy = CopyButton(p.Deck.Code, d);
            copy.VerticalAlignment = VerticalAlignment.Bottom;
            copy.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(copy, Dock.Right);
            bottom.Children.Add(copy);
            var wr = new StackPanel { Orientation = Orientation.Horizontal };
            var big = Theme.Text(AppShell.Pct(p.Expected, 1), 58, Theme.Gold, FontWeights.SemiBold, display: true);
            big.Margin = new Thickness(0, 0, 0, -8);
            wr.Children.Add(big);
            var small = Theme.Text("expected\nwin rate", 13, Theme.Dusk, wrap: true);
            small.Margin = new Thickness(10, 0, 22, 4);
            small.VerticalAlignment = VerticalAlignment.Bottom;
            wr.Children.Add(small);
            DockPanel.SetDock(wr, Dock.Left);
            bottom.Children.Add(wr);
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
            if (p.Best != null) chips.Children.Add(AppShell.Chip("vs " + MetaStats.ClassName(p.Best.Class), AppShell.Pct(p.Best.WinRate), Theme.RateBrush(p.Best.WinRate)));
            if (p.Worst != null && p.Worst != p.Best) chips.Children.Add(AppShell.Chip("vs " + MetaStats.ClassName(p.Worst.Class), AppShell.Pct(p.Worst.WinRate), Theme.RateBrush(p.Worst.WinRate)));
            DockPanel.SetDock(chips, Dock.Left);
            bottom.Children.Add(chips);
            body.Children.Add(bottom);
            g.Children.Add(body);

            return new RoundedBorder
            {
                CornerRadius = new CornerRadius(14), Background = Theme.B(into), Child = g,
                BorderBrush = Theme.B(Theme.GoldC, 0.35), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 18),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 10, Opacity = 0.55 },
            };
        }

        public static Button CopyButton(string code, AppData d, bool primary = true) =>
            Theme.Button("Copy deck code", () =>
            {
                try { Clipboard.SetText(code ?? ""); } catch (Exception swallowed) { Diag.Ignored(swallowed, "PlayPage"); }
            }, primary);

        // ------------------------------------------------------------------ the other decks

        private static UIElement Row(DeckPlanner.DeckPick p, AppShell shell, List<string> copies)
        {
            var g = new Grid { Height = 66 };
            g.Children.Add(AppShell.ArtBanner(p.SignatureCardId, Theme.SlateC, 340, 0.6, 0.0, 0.7));
            // A dark fade behind the number so it stays readable on bright art.
            g.Children.Add(new Border
            {
                Width = 190, HorizontalAlignment = HorizontalAlignment.Right,
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, Theme.NightC.R, Theme.NightC.G, Theme.NightC.B), 0),
                    new GradientStop(Color.FromArgb(215, Theme.NightC.R, Theme.NightC.G, Theme.NightC.B), 0.5),
                }, new Point(0, 0), new Point(1, 0)),
            });
            g.Children.Add(new Border { Width = 4, HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.B(Theme.ClassColor(p.Deck.Class)) });
            var row = new DockPanel { Margin = new Thickness(20, 0, 20, 0), LastChildFill = true };
            var num = p.Archetype != null
                ? Theme.Text(AppShell.Pct(p.Expected, 1), 30, Theme.Vellum, FontWeights.SemiBold, display: true)
                : Theme.Text("No data", 15, Theme.Dusk, FontWeights.Medium);
            num.VerticalAlignment = VerticalAlignment.Center;
            num.Width = 90;
            num.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(num, Dock.Right);
            row.Children.Add(num);
            var mu = new TextBlock { FontSize = 12.5, Foreground = Theme.Dusk, VerticalAlignment = VerticalAlignment.Center, Width = 250 };
            if (p.Archetype == null) mu.Text = "Doesn't match a meta deck yet";
            else
            {
                void Add(string sep, DeckPlanner.ClassMatchup m)
                {
                    if (m == null) return;
                    mu.Inlines.Add(new System.Windows.Documents.Run(sep + "vs " + MetaStats.ClassName(m.Class) + " "));
                    mu.Inlines.Add(new System.Windows.Documents.Run(AppShell.Pct(m.WinRate)) { Foreground = Theme.RateBrush(m.WinRate) });
                }
                Add("", p.Best);
                if (p.Worst != p.Best) Add(", ", p.Worst);
            }
            DockPanel.SetDock(mu, Dock.Right);
            row.Children.Add(mu);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Theme.Text(p.Deck.Name, 17, Theme.Vellum, FontWeights.SemiBold));
            names.Children.Add(SubtitleWithCopies(p.Archetype?.Name ?? MetaStats.ClassName(p.Deck.Class), copies, 12.5));
            row.Children.Add(names);
            g.Children.Add(row);

            var b = new RoundedBorder { CornerRadius = new CornerRadius(10), Background = Theme.Slate, Child = g, Margin = new Thickness(0, 0, 0, 8), Cursor = System.Windows.Input.Cursors.Hand };
            b.ToolTip = "Open in Decks";
            b.MouseLeftButtonUp += (s, e) => { shell.SelectedDeck = shell.Data.Decks.IndexOf(p.Deck); shell.Show("Decks"); };
            return b;
        }

        // ------------------------------------------------------------------ what changed

        /// <summary>What moved since the last meta snapshot; alerts about a merged copy of a deck are left out (the first one already says it).</summary>
        private static UIElement MetaChanges(AppData d, List<string> copyNames)
        {
            var p = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            var h = Theme.Heading(d.MetaSince.HasValue ? $"Meta changes since {d.MetaSince.Value:MMM d}" : "Meta changes");
            p.Children.Add(h);
            if (d.MetaSince == null)
            {
                p.Children.Add(Theme.Text("HearthStoner keeps a daily snapshot of the meta. From tomorrow it points out here when your decks' odds or the field change.", 13, Theme.Dusk, wrap: true));
                return p;
            }
            if (d.MetaAlerts.Count == 0)
            {
                p.Children.Add(Theme.Text("Nothing notable: your decks' odds and the common decks' shares have held steady.", 13, Theme.Dusk, wrap: true));
                return p;
            }
            foreach (var a in d.MetaAlerts.Where(a => !(a.AboutYourDeck && copyNames.Any(n => a.Text.StartsWith(n + ":", StringComparison.Ordinal)))))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                // Your decks: green up / red down. The field: neutral arrows (more of a deck isn't good or bad by itself).
                var color = a.AboutYourDeck ? (a.Change > 0 ? Theme.Win : Theme.Loss) : Theme.Mana;
                var arrow = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(a.Change > 0 ? "M 0,8 L 5,0 L 10,8 Z" : "M 0,0 L 10,0 L 5,8 Z"),
                    Fill = color, Width = 10, Height = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                };
                row.Children.Add(arrow);
                var t = Theme.Text(a.Text, 13.5, a.AboutYourDeck ? Theme.Vellum : Theme.Dusk, wrap: true);
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                p.Children.Add(row);
            }
            return p;
        }

        // ------------------------------------------------------------------ today

        /// <summary>Today's record, time played and rank change (a day runs 4 AM to 4 AM), then your last results.</summary>
        private static UIElement Session(AppShell shell)
        {
            var d = shell.Data;
            var s = d.Session;
            var p = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            p.Children.Add(Theme.Heading("Today so far"));
            if (s.Games > 0)
            {
                var nums = new WrapPanel { Margin = new Thickness(0, -2, 0, 14) };
                void Num(string v, string label, Brush c)
                {
                    var b = new StackPanel { Margin = new Thickness(0, 0, 34, 6) };
                    b.Children.Add(Theme.Text(v, 26, c, FontWeights.SemiBold, display: true));
                    b.Children.Add(Theme.Text(label, 12, Theme.Dusk));
                    nums.Children.Add(b);
                }
                Num($"{s.Wins}-{s.Losses}", s.Games == 1 ? "1 game" : $"{s.Games} games", Theme.Vellum);
                if (s.Wins + s.Losses > 0) Num(AppShell.Pct(s.WinRate), "win rate", Theme.RateBrush(s.WinRate));
                Num(Duration(s.Played), "played", Theme.Vellum);
                if (s.RankAtStart != null && d.Rank != null)
                    Num(s.RankAtStart == d.Rank ? d.Rank : $"{s.RankAtStart} → {d.Rank}", s.RankAtStart == d.Rank ? "rank, no change" : "rank today", Theme.Vellum);
                p.Children.Add(nums);
            }
            else
            {
                var none = Theme.Text($"No games yet today (since {s.Since:h tt}).", 13, Theme.Dusk, wrap: true);
                none.Margin = new Thickness(0, -4, 0, 12);
                p.Children.Add(none);
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var label = AppShell.ColumnLabel($"Last {s.Recent.Count}");
            label.Margin = new Thickness(0, 0, 10, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            row.Children.Add(AppShell.ResultSquares(shell, s.Recent));
            p.Children.Add(row);
            return p;
        }

        /// <summary>"45m", "1h 05m".</summary>
        public static string Duration(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{Math.Max(0, (int)Math.Round(t.TotalMinutes))}m";

        // ------------------------------------------------------------------ the field

        /// <param name="allClasses">Show all eight rows (wide screens have the room) instead of five and a "more" toggle.</param>
        private static UIElement Field(DeckPlanner.DeckPick best, AppData d, bool allClasses)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
            p.Children.Add(Theme.Heading("The field " + MetaStats.PartOfDay(DateTime.Now)));
            var bar = new Grid { Height = 12 };
            // The gap goes on the outer box: inside it, the box's background showed as a dark band under the bar.
            var clip = new RoundedBorder { CornerRadius = new CornerRadius(6), Child = bar, Background = Theme.Raised, Margin = new Thickness(0, 0, 0, 12) };
            var shown = best.Matchups.Where(m => m.Share >= 0.02).ToList();
            foreach (var m in shown)
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(m.Share, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - shown.Sum(m => m.Share)), GridUnitType.Star) });
            for (var i = 0; i < shown.Count; i++)
            {
                var seg = new Border { Background = Theme.B(Theme.ClassColor(shown[i].Class)), ToolTip = $"{MetaStats.ClassName(shown[i].Class)}: {AppShell.Pct(shown[i].Share)}" };
                Grid.SetColumn(seg, i);
                bar.Children.Add(seg);
            }
            p.Children.Add(clip);
            p.Children.Add(FieldRow(null, AppShell.ColumnLabel("Class"), AppShell.ColumnLabel("Share", TextAlignment.Right), AppShell.ColumnLabel("You win", TextAlignment.Right), 24));
            const int maxRows = 8;
            var shownRows = allClasses ? maxRows : 5;
            foreach (var m in best.Matchups.Take(shownRows)) p.Children.Add(FieldMatchup(m, d));
            var more = best.Matchups.Skip(shownRows).Take(maxRows - shownRows).ToList();
            if (more.Count > 0)
            {
                var rest = new StackPanel();
                foreach (var m in more) rest.Children.Add(FieldMatchup(m, d));
                var c = AppShell.Collapsible($"{more.Count} more class{(more.Count == 1 ? "" : "es")}", rest);
                c.Margin = new Thickness(0, 6, 0, 0);
                p.Children.Add(c);
            }
            var since = d.ClassShareSince.HasValue && d.ClassShareChange.Count > 0 ? $" Arrows: share up or down by 0.5 points or more since {d.ClassShareSince.Value:MMM d}." : "";
            var note = Theme.Text($"Share of games at {AppData.RankBracketName(d.Meta.Rank)}, and {best.Deck.Name}'s win rate against each class.{since}", 12, Theme.Dusk, wrap: true);
            note.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(note);
            return p;
        }

        /// <summary>Share changes smaller than this (0.5 points) get no arrow.</summary>
        private const double ShareArrowFrom = 0.005;

        /// <summary>One class of the field: its colour, name, share of games (with an arrow when it moved) and your win rate against it.</summary>
        private static UIElement FieldMatchup(DeckPlanner.ClassMatchup m, AppData d)
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Theme.B(Theme.ClassColor(m.Class)), HorizontalAlignment = HorizontalAlignment.Left };
            var n = Theme.Text(MetaStats.ClassName(m.Class), 13.5);
            var s = Theme.Text(AppShell.Pct(m.Share), 13.5, Theme.Mist); s.TextAlignment = TextAlignment.Right;
            var v = m.FromData ? Theme.Text(AppShell.Pct(m.WinRate), 13.5, Theme.RateBrush(m.WinRate), FontWeights.SemiBold) : Theme.Text("-", 13.5, Theme.Dusk);
            v.TextAlignment = TextAlignment.Right;
            UIElement trend = null;
            if (d.ClassShareChange.TryGetValue(m.Class?.ToLowerInvariant() ?? "", out var ch) && Math.Abs(ch) >= ShareArrowFrom)
            {
                // Neutral blue: more of a class isn't good or bad by itself (the Meta changes list uses the same).
                trend = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(ch > 0 ? "M 0,7 L 4.5,0 L 9,7 Z" : "M 0,0 L 9,0 L 4.5,7 Z"), Fill = Theme.Mana, Width = 9, Height = 7,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = $"{(ch > 0 ? "Up" : "Down")} {Math.Abs(ch) * 100:0.0} points since {d.ClassShareSince:MMM d}",
                };
            }
            return FieldRow(dot, n, s, v, 34, trend);
        }

        /// <summary>The field table's columns: dot, class, share, a share arrow, your win rate.</summary>
        private static UIElement FieldRow(UIElement dot, TextBlock name, TextBlock share, TextBlock win, double height, UIElement trend = null)
        {
            var row = new Grid { Height = height };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            if (dot != null) row.Children.Add(dot);
            if (trend != null) { Grid.SetColumn(trend, 3); row.Children.Add(trend); }
            foreach (var (t, col) in new[] { (name, 1), (share, 2), (win, 4) })
            {
                t.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(t, col);
                row.Children.Add(t);
            }
            return new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        }

        // ------------------------------------------------------------------ quests

        private static UIElement Quests(AppData d)
        {
            var p = new StackPanel();
            var box = new Border
            {
                CornerRadius = new CornerRadius(12), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Padding = new Thickness(18),
                Background = new LinearGradientBrush(Color.FromArgb(18, 240, 185, 84), Colors.Transparent, 90), Child = p,
                Margin = new Thickness(0, 0, 0, 18),
            };
            p.Children.Add(Theme.Heading("Quests"));
            var log = d.Quests;
            if (log == null)
            {
                p.Children.Add(Theme.Text("Open Hearthstone once to load your quests.", 13.5, Theme.Dusk, wrap: true));
                return box;
            }
            var plan = d.QuestPlan;
            if (log.Track is TrackState t && t.XpNeeded > 0)
            {
                var top = new DockPanel();
                var lvl = Theme.Text($"{t.Xp:N0} / {t.XpNeeded:N0} XP", 12.5, Theme.Dusk);
                DockPanel.SetDock(lvl, Dock.Right);
                top.Children.Add(lvl);
                top.Children.Add(Theme.Text($"Reward track level {t.Level}", 13.5, Theme.Vellum, FontWeights.SemiBold));
                p.Children.Add(top);
                p.Children.Add(Bar((double)t.Xp / t.XpNeeded, Theme.Mana, 6, new Thickness(0, 5, 0, 5)));
                var weeks = plan.WeekliesGames is double wg ? $" All weeklies: ~{Math.Max(1, Math.Ceiling(wg - 0.05)):0} games" : "";
                var days = plan.DaysUntilWeeklyReset ?? 0;
                if (weeks != "" && days > 0) weeks += $", {days} day{(days == 1 ? "" : "s")} until they reset.";
                else if (weeks != "") weeks += ".";
                var note = Theme.Text($"{t.ToNext:N0} XP to level {t.Level + 1}.{weeks}", 12, Theme.Dusk, wrap: true);
                note.Margin = new Thickness(0, 0, 0, 12);
                p.Children.Add(note);
            }
            var any = false;
            foreach (var pool in new[] { "Daily", "Weekly" })
            {
                var qs = log.Quests.Where(q => q.Pool == pool && q.Open && !q.IsBattlegrounds).ToList();
                if (qs.Count == 0) continue;
                any = true;
                QuestGroup(p, pool.ToUpperInvariant(), qs, plan);
            }
            if (!any) p.Children.Add(Theme.Text("All your quests are done. New ones arrive with the next daily reset.", 13.5, Theme.Dusk, wrap: true));
            // The game keeps these in its separate Battlegrounds quest log, with their own reward track.
            // Collapsed: they don't move your main track, and open they pushed Craft next off the screen.
            var battlegrounds = log.Quests.Where(q => q.Open && q.IsBattlegrounds).ToList();
            if (battlegrounds.Count > 0)
            {
                var bg = new StackPanel();
                bg.Children.Add(Theme.Text("Separate quest log and reward track.", 12, Theme.Dusk));
                foreach (var q in battlegrounds) bg.Children.Add(QuestRow(q, plan));
                p.Children.Add(AppShell.Collapsible($"Battlegrounds quests ({battlegrounds.Count})", bg));
            }

            if (any)
            {
                p.Children.Add(Theme.Hairline(new Thickness(0, 4, 0, 12)));
                p.Children.Add(Theme.Text("Best deck for your quests", 13.5, Theme.Vellum, FontWeights.SemiBold));
                if (plan.Best is QuestPlan.DeckScore best)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
                    var xp = Theme.Text($"{best.XpPerGame:N0} XP/game", 13, Theme.Gold, FontWeights.SemiBold);
                    DockPanel.SetDock(xp, Dock.Right);
                    row.Children.Add(xp);
                    row.Children.Add(Theme.Text(best.Deck.Name, 15, Theme.Vellum, FontWeights.SemiBold, display: true));
                    p.Children.Add(row);
                    if (best.Reason != null) p.Children.Add(Theme.Text(best.Reason, 12.5, Theme.Dusk, wrap: true));
                }
                else p.Children.Add(Theme.Text("Play a few games with your decks and HearthStoner estimates which one moves your quests most.", 12.5, Theme.Dusk, wrap: true));
            }
            var foot = Theme.Text($"Read from Hearthstone {log.Updated:MMM d, h:mm tt}. Estimates come from your saved games.", 11.5, Theme.Dusk, wrap: true);
            foot.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(foot);
            return box;
        }

        /// <summary>A small heading and one row per quest.</summary>
        private static void QuestGroup(Panel p, string heading, List<QuestEntry> quests, QuestPlan plan)
        {
            var h = Theme.Text(heading, 11, Theme.Dusk, FontWeights.SemiBold);
            h.Margin = new Thickness(0, 2, 0, 6);
            p.Children.Add(h);
            foreach (var q in quests) p.Children.Add(QuestRow(q, plan));
        }

        private static UIElement QuestRow(QuestEntry q, QuestPlan plan)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var top = new DockPanel();
            var xp = Theme.Text($"{q.Xp:N0} XP", 12.5, Theme.Gold, FontWeights.SemiBold);
            xp.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(xp, Dock.Right);
            top.Children.Add(xp);
            top.Children.Add(Theme.Text(string.IsNullOrEmpty(q.Name) ? q.Text : q.Name + ": " + q.Text, 13.5, Theme.Vellum, wrap: true));
            p.Children.Add(top);
            p.Children.Add(Bar(q.Quota > 0 ? (double)q.Progress / q.Quota : 0, Theme.Gold, 5, new Thickness(0, 5, 0, 3)));
            var info = new TextBlock { FontSize = 12, Foreground = Theme.Dusk, FontFamily = Theme.Body };
            var est = q.IsBattlegrounds ? "play Battlegrounds"
                : !plan.GamesFor.TryGetValue(q.Id, out var g) ? "can't estimate"
                : double.IsInfinity(g) ? "your decks don't move it" : $"~{Math.Max(1, Math.Ceiling(g - 0.05)):0} game{(g <= 1.05 ? "" : "s")}";
            info.Inlines.Add(new System.Windows.Documents.Run($"{q.Progress:N0} / {q.Quota:N0}  ·  {est}"));
            if (plan.Reroll == q)
                info.Inlines.Add(new System.Windows.Documents.Run("  ·  Reroll this one") { Foreground = Theme.Gold, FontWeight = FontWeights.SemiBold });
            p.Children.Add(info);
            return p;
        }

        private static UIElement Bar(double fraction, Brush fill, double height, Thickness margin)
        {
            var b = AppShell.StatBar(fraction, fill, height);
            b.Margin = margin;
            return b;
        }

        // ------------------------------------------------------------------ crafting

        private static UIElement Crafting(AppShell shell)
        {
            var d = shell.Data;
            var p = new StackPanel();
            var box = new Border
            {
                CornerRadius = new CornerRadius(12), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Padding = new Thickness(18),
                Background = new LinearGradientBrush(Color.FromArgb(16, 90, 160, 255), Colors.Transparent, 90), Child = p,
            };
            p.Children.Add(Theme.Heading("Craft next"));
            if (d.Craft == null || !d.Craft.HasCollection)
            {
                var t = Theme.Text("HearthStoner reads your collection by itself once Hearthstone is at the main menu. Crafting suggestions show up here then.", 13.5, Theme.Dusk, wrap: true);
                t.Margin = new Thickness(0, -4, 0, 14);
                p.Children.Add(t);
                return box;
            }
            if (d.Craft.Options.Count == 0)
            {
                p.Children.Add(Theme.Text("No strong meta decklists to compare with right now.", 13.5, Theme.Dusk, wrap: true));
                return box;
            }
            foreach (var o in d.Craft.Options.Take(5))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var cost = o.Dust == 0 ? Theme.Text("Ready", 15, Theme.Win, FontWeights.SemiBold, display: true) : Theme.Text($"{o.Dust:N0}", 17, Theme.Vellum, FontWeights.SemiBold, display: true);
                cost.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(cost, Dock.Right);
                row.Children.Add(cost);
                var names = new StackPanel();
                names.Children.Add(Theme.Text(o.Archetype.Name, 14, Theme.Vellum, FontWeights.SemiBold));
                names.Children.Add(Theme.Text($"{AppShell.Pct(o.Deck.WinRate, 1)} win rate, {(o.Dust == 0 ? "you own every card" : $"{o.Missing.Count} card{(o.Missing.Count == 1 ? "" : "s")} to craft")}", 12.5, Theme.Dusk));
                row.Children.Add(names);
                p.Children.Add(row);
            }
            var foot = Theme.Text($"Dust to complete each deck from your collection (read {d.Craft.CollectionUpdated:MMM d}).", 12, Theme.Dusk, wrap: true);
            p.Children.Add(foot);
            return box;
        }

        public static UIElement Empty(string title, string text)
        {
            var p = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            p.Children.Add(Theme.Heading(title));
            var t = Theme.Text(text, 14, Theme.Dusk, wrap: true);
            t.MaxWidth = 520; t.HorizontalAlignment = HorizontalAlignment.Left;
            p.Children.Add(t);
            return p;
        }
    }
}
