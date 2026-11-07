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

            var list = new StackPanel();
            void Rebuild() { list.Children.Clear(); Fill(list, shell, mine); }
            p.Children.Add(Toolbar(shell, Rebuild));
            p.Children.Add(list);
            Rebuild();

            var holder = new Grid();
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 1280 });
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Auto) });
            holder.Children.Add(p);
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
        private static void Fill(StackPanel p, AppShell shell, Dictionary<int, int> mine)
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
                    foreach (var a in Sorted(cls)) p.Children.Add(Row(a, maxShare, mine, indent: true));
                }
            }
            else
                foreach (var a in Sorted(rows)) p.Children.Add(Row(a, maxShare, mine, indent: false));
        }

        private static Grid Columns(double height)
        {
            var g = new Grid { Height = height };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
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

            var bar = new Grid { VerticalAlignment = VerticalAlignment.Center };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Raised });
            bar.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, 150 * a.Share / maxShare), Background = Theme.B(color, 0.85) });
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
