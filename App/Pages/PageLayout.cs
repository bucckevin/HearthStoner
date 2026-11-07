using System.Windows;
using System.Windows.Controls;

namespace HearthStoner.Pages
{
    /// <summary>How much room the page area has. Pages pick their number of columns from this.</summary>
    public enum PageWidth
    {
        /// <summary>A small window: one column, side panels go under the main content.</summary>
        Compact,
        /// <summary>A normal window (the default 1640 px window): the two-column layouts.</summary>
        Standard,
        /// <summary>A maximized window on a 1920 px or wider screen: three columns where a page has them.</summary>
        Wide,
    }

    /// <summary>
    /// The width rules every page shares: the breakpoints between <see cref="PageWidth"/>s, the widest the content
    /// gets (centred beyond that, so an ultrawide screen doesn't stretch a table across 3,000 px), and small helpers
    /// for laying out columns.
    /// </summary>
    public static class PageLayout
    {
        /// <summary>Width of the navigation rail on the left.</summary>
        public const double RailWidth = 212;
        /// <summary>Page areas narrower than this are <see cref="PageWidth.Compact"/>.</summary>
        public const double CompactBelow = 1000;
        /// <summary>Page areas at least this wide are <see cref="PageWidth.Wide"/>.</summary>
        public const double WideFrom = 1600;
        /// <summary>
        /// Page areas at least this wide (an ultrawide screen) have room for a page's extra side column, like
        /// History's review beside its chart. Crossing it rebuilds the page like a breakpoint does.
        /// </summary>
        public const double ExtraWideFrom = 2200;
        /// <summary>The widest a page's content gets.</summary>
        public const double MaxContent = 2400;
        /// <summary>The gap between columns.</summary>
        public const double Gap = 32;

        /// <summary>The size class for a page area <paramref name="width"/> px wide.</summary>
        public static PageWidth For(double width) =>
            width < CompactBelow ? PageWidth.Compact : width >= WideFrom ? PageWidth.Wide : PageWidth.Standard;

        /// <summary>
        /// Columns side by side with <see cref="Gap"/> between them. Each column is (content, width): a width above 0
        /// is in px, 0 or below is a share of what's left (-1.5 means 1.5 shares).
        /// </summary>
        public static Grid Columns(params (UIElement Content, double Width)[] cols)
        {
            var g = new Grid();
            for (var i = 0; i < cols.Length; i++)
            {
                if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
                var w = cols[i].Width;
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(w == 0 ? 1 : -w, GridUnitType.Star) });
                if (cols[i].Content == null) continue;
                Grid.SetColumn(cols[i].Content, i * 2);
                g.Children.Add(cols[i].Content);
            }
            return g;
        }

        /// <summary>The elements one under the other, with <paramref name="gap"/> px between them.</summary>
        public static StackPanel Stack(double gap, params UIElement[] items)
        {
            var p = new StackPanel();
            foreach (var it in items)
            {
                if (it == null) continue;
                if (p.Children.Count > 0 && it is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + gap, fe.Margin.Right, fe.Margin.Bottom);
                p.Children.Add(it);
            }
            return p;
        }
    }
}
