using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Pages
{
    /// <summary>
    /// Finds page content cut off by its scroll area: a scroll area hides whatever sticks out past its edges, so a
    /// negative margin on its first row slices off the row's top (the Stats page's mode chips, 1.5.0). Used by the
    /// screenshot command, which fails when anything is found.
    /// </summary>
    public static class ClipCheck
    {
        /// <summary>How far, in pixels, an element may stick out before it counts (rounding).</summary>
        private const double Tolerance = 0.5;

        /// <summary>
        /// Every element under <paramref name="root"/> that sticks out above or left of the scroll area holding it,
        /// while that area is scrolled to the top-left. Only the outermost element of a cut-off group is listed.
        /// </summary>
        public static List<string> Find(DependencyObject root)
        {
            var found = new List<string>();
            foreach (var sv in Descendants<ScrollViewer>(root))
            {
                if (sv.VerticalOffset > 0 || sv.HorizontalOffset > 0 || sv.Content is not DependencyObject content) continue;
                Walk(content, sv, found);
            }
            return found;
        }

        /// <summary>Checks <paramref name="node"/>, then its children unless it was listed or clips them itself.</summary>
        private static void Walk(DependencyObject node, ScrollViewer sv, List<string> found)
        {
            if (node is FrameworkElement fe)
            {
                if (fe.Visibility != Visibility.Visible) return;
                if (fe.ActualWidth > 0 && fe.ActualHeight > 0 && TopLeft(fe, sv) is Point p && (p.Y < -Tolerance || p.X < -Tolerance))
                {
                    found.Add($"{Describe(fe)} sticks out {Math.Max(0, -Math.Round(p.Y, 1))}px at the top and {Math.Max(0, -Math.Round(p.X, 1))}px at the left");
                    return;
                }
                // Anything inside an element that clips is cut by that element, not the scroll area.
                if (fe.ClipToBounds || fe.Clip != null || fe is ScrollViewer) return;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i), sv, found);
        }

        /// <summary>The element's top-left corner in the scroll area's coordinates, or null if it can't be placed.</summary>
        private static Point? TopLeft(FrameworkElement fe, ScrollViewer sv)
        {
            try { return fe.TransformToAncestor(sv).Transform(new Point(0, 0)); }
            catch (InvalidOperationException swallowed) { Diag.Ignored(swallowed, "ClipCheck"); return null; }
        }

        /// <summary>The element's type and the first text in it, so the message says which part of the page it is.</summary>
        private static string Describe(FrameworkElement fe)
        {
            var text = fe is TextBlock own ? own.Text : null;
            if (string.IsNullOrEmpty(text))
                foreach (var t in Descendants<TextBlock>(fe)) { if (!string.IsNullOrEmpty(t.Text)) { text = t.Text; break; } }
            return string.IsNullOrEmpty(text) ? fe.GetType().Name : $"{fe.GetType().Name} \"{text}\"";
        }

        /// <summary>Every visual descendant of type <typeparamref name="T"/>, depth first.</summary>
        private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T match) yield return match;
                foreach (var d in Descendants<T>(child)) yield return d;
            }
        }
    }
}