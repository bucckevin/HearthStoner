using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Pages
{
    /// <summary>
    /// Meta: every archetype at your rank with how often it's played and how it does. Grouped by class, in one list,
    /// or only the archetypes your decks match; a search box filters as you type and the Games and Win rate headings
    /// sort. Your archetypes are marked.
    /// </summary>
    public static class MetaPage
    {
        private static readonly string[] Views = { "By class", "All in one list", "Only decks I play" };

        public static UIElement Build(AppShell shell)
        {
            var m = shell.Data.Meta;
            var p = new StackPanel();
            if (m == null || m.Archetypes.Count == 0)
            {
                p.Children.Add(PlayPage.Empty("No meta data yet", "HearthStoner downloads it from Firestone's public stats. Check your internet connection, then refresh."));
                return AppShell.Page("Meta", "", p);
            }
            // Your decks per archetype, for the "YOU" mark.
            var mine = shell.Data.Picks.Where(x => x.Archetype != null && !x.Deck.Imported)
                .GroupBy(x => x.Archetype.Id).ToDictionary(g => g.Key, g => g.Count());

            // Wide screens: click an archetype to see it in a panel on the right (your first archetype to begin with).
            var wide = shell.Size == PageWidth.Wide;
            MetaArchetype selected = null;
            if (wide)
            {
                selected = m.Archetypes.FirstOrDefault(a => a.Id == shell.MetaSelected)
                           ?? m.Archetypes.FirstOrDefault(a => mine.ContainsKey(a.Id)) ?? m.Archetypes[0];
                shell.MetaSelected = selected.Id;
            }
            var list = new StackPanel();
            void Rebuild() { list.Children.Clear(); Fill(list, shell, mine, wide); }
            p.Children.Add(Toolbar(shell, Rebuild));
            p.Children.Add(list);
            Rebuild();

            var holder = new Grid();
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 1280 });
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Auto) });
            holder.Children.Add(p);
            if (selected != null)
            {
                holder.ColumnDefinitions[1].Width = new GridLength(PageLayout.Gap);
                holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelWidth) });
                var panel = Panel(shell, selected, mine);
                Grid.SetColumn(panel, 2);
                holder.Children.Add(panel);
            }
            return AppShell.Page("Meta", $"Every archetype at {AppData.RankBracketName(m.Rank)}, {AppData.PeriodName(m.Period)}: {m.TotalGames:N0} games from Firestone.", holder);
        }

        /// <summary>The search box and the view chips. Typing filters in place (no page redraw, so the box keeps focus).</summary>
        private static UIElement Toolbar(AppShell shell, Action rebuild)
        {
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            var box = new TextBox
            {
                Width = 280, Height = 34, Padding = new Thickness(8, 6, 8, 6), FontSize = 13.5, FontFamily = Theme.Body, Text = shell.MetaSearch ?? "",
                Background = Theme.Slate, Foreground = Theme.Vellum, BorderBrush = Theme.Line, CaretBrush = Theme.Vellum, ToolTip = "Find an archetype",
            };
            var hint = Theme.Text("Find an archetype", 13.5, Theme.Dusk);
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(11, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (s, e) =>
            {
                hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                shell.MetaSearch = box.Text;
                rebuild();
            };
            var field = new Grid { Margin = new Thickness(0, 0, 18, 6) };
            field.Children.Add(box);
            field.Children.Add(hint);
            bar.Children.Add(field);
            foreach (var v in Views)
            {
                var on = v == shell.MetaView;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(0, 0, 6, 6), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                    Background = on ? Theme.B(Theme.Rgb(0x2E, 0x35, 0x66)) : Theme.Raised, BorderBrush = on ? Theme.Mana : Theme.Line, BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = v, FontSize = 13, Foreground = on ? Theme.Vellum : Theme.Mist },
                };
                var pick = v;
                chip.MouseLeftButtonUp += (s, e) => { shell.MetaView = pick; shell.Redraw(); };
                bar.Children.Add(chip);
            }
            return bar;
        }

        /// <summary>The table: a header (Games and Win rate sort when clicked), then the archetypes for the chosen view.</summary>
        /// <param name="selectable">Rows open the detail panel when clicked (wide screens).</param>
        private static void Fill(StackPanel p, AppShell shell, Dictionary<int, int> mine, bool selectable)
        {
            var m = shell.Data.Meta;
            var maxShare = m.Archetypes.Max(a => a.Share);
            var q = (shell.MetaSearch ?? "").Trim();
            var shown = m.Archetypes.Where(a => q.Length == 0 || (a.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                MetaStats.ClassName(a.Class).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            if (shell.MetaView == Views[2]) shown = shown.Where(a => mine.ContainsKey(a.Id));
            var rows = shown.ToList();

            p.Children.Add(Header(shell));
            if (rows.Count == 0)
            {
                var none = Theme.Text(shell.MetaView == Views[2] ? "None of your decks match an archetype here." : $"No archetype matches \"{q}\".", 13.5, Theme.Dusk);
                none.Margin = new Thickness(0, 14, 0, 0);
                p.Children.Add(none);
                return;
            }
            IEnumerable<MetaArchetype> Sorted(IEnumerable<MetaArchetype> xs) =>
                shell.MetaSort == "Win rate" ? xs.OrderByDescending(a => a.WinRate) : xs.OrderByDescending(a => a.Games);

            if (shell.MetaView == Views[0])
            {
                foreach (var cls in rows.GroupBy(a => a.Class).OrderByDescending(g => m.Archetypes.Where(a => a.Class == g.Key).Sum(a => a.Games)))
                {
                    var color = Theme.ClassColor(cls.Key);
                    var title = new DockPanel { Margin = new Thickness(0, 18, 0, 4) };
                    var share = Theme.Text($"{AppShell.Pct(m.Archetypes.Where(a => a.Class == cls.Key).Sum(a => a.Share), 1)} of games", 13, Theme.Mist);
                    share.VerticalAlignment = VerticalAlignment.Bottom;
                    DockPanel.SetDock(share, Dock.Right);
                    title.Children.Add(share);
                    title.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = Theme.B(color), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                    title.Children.Add(Theme.Text(MetaStats.ClassName(cls.Key), 20, Theme.Vellum, FontWeights.SemiBold, display: true));
                    p.Children.Add(title);
                    foreach (var a in Sorted(cls)) p.Children.Add(Selectable(Row(a, maxShare, mine, indent: true), a, shell, selectable));
                }
            }
            else
                foreach (var a in Sorted(rows)) p.Children.Add(Selectable(Row(a, maxShare, mine, indent: false), a, shell, selectable));
        }

        /// <summary>The detail panel's width (wide screens).</summary>
        private const double PanelWidth = 520;

        /// <summary>Days of share history in the panel's trend line.</summary>
        private const int TrendDays = 14;

        /// <summary>Archetypes whose detail download was started, so a failed one isn't retried on every redraw.</summary>
        private static readonly HashSet<int> DetailRequested = new HashSet<int>();

        /// <summary>Makes a row open its archetype in the panel when clicked, and marks the open one.</summary>
        private static UIElement Selectable(UIElement row, MetaArchetype a, AppShell shell, bool selectable)
        {
            if (!selectable) return row;
            var on = a.Id == shell.MetaSelected;
            var wrap = new Grid { Cursor = System.Windows.Input.Cursors.Hand, Background = on ? Theme.B(Theme.ManaC, 0.12) : Brushes.Transparent, ToolTip = on ? null : "Show this archetype on the right" };
            wrap.Children.Add(row);
            if (on) wrap.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Right, Background = Theme.Mana });
            wrap.MouseLeftButtonUp += (s, e) => { if (shell.MetaSelected == a.Id) return; shell.MetaSelected = a.Id; shell.Redraw(); };
            return wrap;
        }

        /// <summary>
        /// One archetype in detail: its numbers, its share over the last two weeks (HearthStoner's daily snapshots), how
        /// it does against your decks' classes and its core cards. The matchups download in the background if needed.
        /// </summary>
        private static UIElement Panel(AppShell shell, MetaArchetype a, Dictionary<int, int> mine)
        {
            var d = shell.Data;
            var m = d.Meta;
            var p = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = Theme.B(Theme.ClassColor(a.Class)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Theme.Text(a.Name, 26, Theme.Vellum, FontWeights.SemiBold, display: true));
            p.Children.Add(head);
            var place = m.Archetypes.OrderByDescending(x => x.Share).ToList().IndexOf(a) + 1;
            p.Children.Add(Theme.Text($"{MetaStats.ClassName(a.Class)}, #{place} of {m.Archetypes.Count} by games" + (mine.ContainsKey(a.Id) ? ", one of your decks" : ""), 13, Theme.Dusk));
            var nums = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };
            void Num(string v, string label, Brush c)
            {
                var b = new StackPanel { Margin = new Thickness(0, 0, 30, 6) };
                b.Children.Add(Theme.Text(v, 24, c, FontWeights.SemiBold, display: true));
                b.Children.Add(Theme.Text(label, 12, Theme.Dusk));
                nums.Children.Add(b);
            }
            Num(AppShell.Pct(a.Share, 1), "of games", Theme.Vellum);
            Num(AppShell.Pct(a.WinRate, 1), "win rate", Theme.RateBrush(a.WinRate));
            Num($"{a.Games:N0}", "games", Theme.Vellum);
            p.Children.Add(nums);

            // Share over the last two weeks.
            var trend = d.History.Snapshots.Where(s => s.Rank == m.Rank && s.Period == m.Period && s.Day >= DateTime.Today.AddDays(-TrendDays))
                .OrderBy(s => s.Day).Select(s => (s.Day, Share: s.Archetypes.TryGetValue(a.Key, out var x) ? x.Share : 0)).ToList();
            p.Children.Add(Small("Share, last two weeks"));
            if (trend.Count >= 2)
            {
                var change = trend[trend.Count - 1].Share - trend[0].Share;
                var row = new DockPanel();
                var line = AppShell.Sparkline(trend.Select(x => x.Share).ToList(), 200, 40, 2);
                line.Margin = new Thickness(16, 0, 0, 0);
                DockPanel.SetDock(line, Dock.Right);
                row.Children.Add(line);
                row.Children.Add(Theme.Text(Math.Abs(change) < 0.0005 ? $"Steady since {trend[0].Day:MMM d}." : $"{(change > 0 ? "Up" : "Down")} {Math.Abs(change) * 100:0.0} points since {trend[0].Day:MMM d}.", 13.5, Theme.Vellum, wrap: true));
                p.Children.Add(row);
            }
            else p.Children.Add(Theme.Text("HearthStoner saves the meta once a day: the trend shows from the second day.", 13, Theme.Dusk, wrap: true));

            // Against your decks: the archetype's record against each class you play (from its matchup data).
            p.Children.Add(Small("Against your decks"));
            var yours = d.Picks.Where(x => !x.Deck.Imported && x.Archetype != null).GroupBy(x => MetaStats.Key(x.Deck.Class)).ToList();
            var det = m.CachedDetail(a.Id);
            if (yours.Count == 0) p.Children.Add(Theme.Text("None of your decks match an archetype yet.", 13, Theme.Dusk, wrap: true));
            else if (det == null)
            {
                var tried = DetailRequested.Contains(a.Id);
                p.Children.Add(Theme.Text(tried ? "Its matchups couldn't be loaded (no connection?)." : "Loading its matchups...", 13, Theme.Dusk, wrap: true));
                if (!tried)
                {
                    DetailRequested.Add(a.Id);
                    var id = a.Id;
                    var log = d.Services?.Log;
                    System.Threading.Tasks.Task.Run(() => m.Detail(id, log)).ContinueWith(t =>
                    {
                        if (t.IsFaulted) Diag.Ignored(t.Exception, "MetaPage");
                        if (shell.Current == "Meta" && shell.MetaSelected == id) shell.Redraw();
                    }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
                }
            }
            else
            {
                foreach (var cls in yours)
                {
                    var g = new Grid { Height = 34 };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    var names = string.Join(", ", cls.Select(x => x.Deck.Name));
                    var who = Theme.Text($"vs {MetaStats.ClassName(cls.Key)} ({names})", 13.5, Theme.Vellum);
                    who.TextTrimming = TextTrimming.CharacterEllipsis;
                    who.ToolTip = names;
                    who.VerticalAlignment = VerticalAlignment.Center;
                    g.Children.Add(who);
                    TextBlock val;
                    if (det.Matchups.TryGetValue(cls.Key, out var mu) && mu.Games >= 50)
                    {
                        var you = 1 - mu.WinRate;
                        val = new TextBlock { FontSize = 13.5, TextAlignment = TextAlignment.Right, Foreground = Theme.Mist };
                        val.Inlines.Add(new System.Windows.Documents.Run("you win "));
                        val.Inlines.Add(new System.Windows.Documents.Run(AppShell.Pct(you)) { Foreground = Theme.RateBrush(you), FontWeight = FontWeights.SemiBold });
                        g.ToolTip = $"{a.Name} wins {AppShell.Pct(mu.WinRate, 1)} of {mu.Games:N0} games against {MetaStats.ClassName(cls.Key)}";
                    }
                    else val = Theme.Text("too few games", 13, Theme.Dusk);
                    val.TextAlignment = TextAlignment.Right;
                    val.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(val, 1);
                    g.Children.Add(val);
                    p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g });
                }
                var note = Theme.Text("Its win rate against each class you play, turned around: how often that class beats it in the data.", 12, Theme.Dusk, wrap: true);
                note.Margin = new Thickness(0, 6, 0, 0);
                p.Children.Add(note);
            }

            // Core cards: what every list of it runs.
            if (a.CoreCards.Count > 0)
            {
                p.Children.Add(Small("Core cards"));
                var cards = a.CoreCards.Distinct().Select(id => (Id: id, Info: d.Cards.ById(id))).OrderBy(x => x.Info?.Cost ?? 99).ThenBy(x => x.Info?.Name).Take(16).ToList();
                var cols = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
                foreach (var (id, info) in cards)
                {
                    var tile = SectionView.CardTile(AdviceLine.Card(info, id, info?.Name ?? id, Tone.Normal, null, null), true, true);
                    if (tile is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 0);
                    cols.Children.Add(tile);
                }
                p.Children.Add(cols);
            }
            return new Border
            {
                Child = p, Padding = new Thickness(22, 18, 22, 20), CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top,
                BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Background = Theme.B(Theme.NightC, 0.6),
            };
        }

        /// <summary>A small capitals label opening a part of the panel.</summary>
        private static TextBlock Small(string text)
        {
            var t = AppShell.ColumnLabel(text);
            t.Margin = new Thickness(0, 18, 0, 6);
            return t;
        }

        /// <summary>Archetype, played, games, win rate. The share bar takes the spare width, so names sit next to their bars.</summary>
        private static Grid Columns(double height)
        {
            var g = new Grid { Height = height };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            return g;
        }

        private static UIElement Header(AppShell shell)
        {
            var g = Columns(28);
            void Head(string text, int col, bool sortable)
            {
                var label = sortable && shell.MetaSort == text ? text + " ▼" : text;
                var t = AppShell.ColumnLabel(label, col >= 2 ? TextAlignment.Right : TextAlignment.Left);
                if (sortable)
                {
                    t.Cursor = System.Windows.Input.Cursors.Hand;
                    t.ToolTip = "Sort by " + text.ToLowerInvariant();
                    if (shell.MetaSort == text) t.Foreground = Theme.Vellum;
                    t.MouseLeftButtonUp += (s, e) => { shell.MetaSort = text; shell.Redraw(); };
                }
                Grid.SetColumn(t, col);
                g.Children.Add(t);
            }
            Head("Archetype", 0, false);
            Head("Played", 1, false);
            Head("Games", 2, true);
            Head("Win rate", 3, true);
            return new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        }

        /// <summary>One archetype: name (with a YOU tag and a blue edge when your decks match it), share bar, games, win rate.</summary>
        private static UIElement Row(MetaArchetype a, double maxShare, Dictionary<int, int> mine, bool indent)
        {
            var color = Theme.ClassColor(a.Class);
            var g = Columns(34);
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(indent ? 22 : 12, 0, 0, 0) };
            if (!indent) name.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Theme.B(color), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            name.Children.Add(Theme.Text(a.Name, 14, Theme.Vellum, FontWeights.Medium));
            var yours = mine.TryGetValue(a.Id, out var n) ? n : 0;
            if (yours > 0)
                name.Children.Add(new Border
                {
                    Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(7, 1, 7, 2), CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Center,
                    Background = Theme.B(Theme.Rgb(0x2E, 0x35, 0x66)),
                    Child = new TextBlock { Text = yours == 1 ? "YOU" : $"YOU · {yours} decks", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Theme.Mana },
                });
            g.Children.Add(name);

            var bar = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 420 });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            var track = AppShell.StatBar(Math.Max(0.01, a.Share / maxShare), Theme.B(color, 0.85), 6);
            track.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(track);
            var s = Theme.Text(AppShell.Pct(a.Share, 1), 13, Theme.Mist); s.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(s, 1); bar.Children.Add(s);
            Grid.SetColumn(bar, 1);
            g.Children.Add(bar);
            var gm = Theme.Text($"{a.Games:N0}", 14, Theme.Mist); gm.TextAlignment = TextAlignment.Right; gm.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(gm, 2); g.Children.Add(gm);
            var w = Theme.Text(AppShell.Pct(a.WinRate, 1), 14, Theme.RateBrush(a.WinRate), FontWeights.SemiBold); w.TextAlignment = TextAlignment.Right; w.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(w, 3); g.Children.Add(w);

            var row = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
            if (yours == 0) return row;
            // A blue edge and a faint tint so your archetypes stand out in a long list.
            var wrap = new Grid { Background = Theme.B(Theme.ManaC, 0.07) };
            wrap.Children.Add(row);
            wrap.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.Mana });
            return wrap;
        }
    }
}
