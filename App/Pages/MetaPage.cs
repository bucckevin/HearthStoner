using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner
{
    /// <summary>Meta: every archetype at your rank, grouped by class, with how often it's played and how it does.</summary>
    public static class MetaPage
    {
        public static UIElement Build(AppShell shell)
        {
            var m = shell.Data.Meta;
            var p = new StackPanel();
            if (m == null || m.Archetypes.Count == 0)
            {
                p.Children.Add(PlayPage.Empty("No meta data yet", "HearthStoner downloads it from Firestone's public stats. Check your internet connection, then refresh."));
                return AppShell.Page("Meta", "", p);
            }
            var maxShare = m.Archetypes.Max(a => a.Share);
            var header = Row("Archetype", "Played", "Games", "Win rate", null, true);
            p.Children.Add(header);
            foreach (var cls in m.Archetypes.GroupBy(a => a.Class).OrderByDescending(g => g.Sum(a => a.Games)))
            {
                var color = Theme.ClassColor(cls.Key);
                var title = new DockPanel { Margin = new Thickness(0, 18, 0, 4) };
                var share = Theme.Text($"{AppShell.Pct(cls.Sum(a => a.Share), 1)} of games", 13, Theme.Dusk);
                share.VerticalAlignment = VerticalAlignment.Bottom;
                DockPanel.SetDock(share, Dock.Right);
                title.Children.Add(share);
                title.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = Theme.B(color), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                title.Children.Add(Theme.Text(MetaStats.ClassName(cls.Key), 20, Theme.Vellum, FontWeights.SemiBold, display: true));
                p.Children.Add(title);
                foreach (var a in cls.OrderByDescending(a => a.Games))
                    p.Children.Add(Row(a.Name, AppShell.Pct(a.Share, 1), $"{a.Games:N0}", AppShell.Pct(a.WinRate, 1), a, false, maxShare, color));
            }
            var holder = new Grid();
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 900 });
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Auto) });
            holder.Children.Add(p);
            return AppShell.Page("Meta", $"Every archetype at {AppData.RankBracketName(m.Rank)}, {AppData.PeriodName(m.Period)}: {m.TotalGames:N0} games from Firestone.", holder);
        }

        private static UIElement Row(string name, string share, string games, string wr, MetaArchetype a, bool header, double maxShare = 1, Color color = default)
        {
            var g = new Grid { Height = header ? 26 : 34 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            var fg = header ? Theme.Dusk : Theme.Vellum;
            var size = header ? 12.5 : 14;
            var n = Theme.Text(name, size, fg, header ? FontWeights.Normal : FontWeights.Medium); n.VerticalAlignment = VerticalAlignment.Center; n.Margin = new Thickness(header ? 0 : 22, 0, 0, 0);
            g.Children.Add(n);
            if (header)
            {
                var s = Theme.Text(share, size, fg); s.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(s, 1); g.Children.Add(s);
            }
            else
            {
                var bar = new Grid { VerticalAlignment = VerticalAlignment.Center };
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, 150 * a.Share / maxShare), Background = Theme.B(color, 0.85) };
                bar.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Raised });
                bar.Children.Add(fill);
                var s = Theme.Text(share, 13, Theme.Dusk); s.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(s, 1); bar.Children.Add(s);
                Grid.SetColumn(bar, 1); g.Children.Add(bar);
            }
            var gm = Theme.Text(games, size, header ? fg : Theme.Dusk); gm.TextAlignment = TextAlignment.Right; gm.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(gm, 2); g.Children.Add(gm);
            var w = Theme.Text(wr, size, header ? fg : Theme.RateBrush(a.WinRate), header ? FontWeights.Normal : FontWeights.SemiBold); w.TextAlignment = TextAlignment.Right; w.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(w, 3); g.Children.Add(w);
            return new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        }
    }
}
