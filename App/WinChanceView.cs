using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HearthStoner
{
    /// <summary>Draws a game's win-chance line: green above 50%, red below, with its biggest drops marked.</summary>
    public static class WinChanceView
    {
        /// <param name="current">Frame to mark (-1 for none).</param>
        /// <param name="r">When given, turn numbers go along the bottom and the drops get labels.</param>
        /// <param name="onFrame">Clicking a marked drop goes to its frame.</param>
        public static void Draw(Canvas c, WinChance wc, int current, Replay r, Action<int> onFrame = null)
        {
            c.Children.Clear();
            var w = double.IsNaN(c.Width) ? c.ActualWidth : c.Width;
            var full = double.IsNaN(c.Height) ? c.ActualHeight : c.Height;
            if (w <= 0 || full <= 0 || wc.Values.Count < 2) return;
            var detailed = r != null;
            double left = detailed ? 40 : 0, right = detailed ? 10 : 0, top = detailed ? 8 : 3, bottom = detailed ? 22 : 3;
            var h = full - top - bottom;
            var n = wc.Values.Count;
            double X(int i) => left + i * (w - left - right) / (n - 1);
            double Y(double v) => top + (1 - v) * h;
            var mid = Y(0.5);

            var fig = new PathFigure { StartPoint = new Point(X(0), mid), IsClosed = true };
            for (var i = 0; i < n; i++) fig.Segments.Add(new LineSegment(new Point(X(i), Y(wc.Values[i])), false));
            fig.Segments.Add(new LineSegment(new Point(X(n - 1), mid), false));
            var area = new PathGeometry(new[] { fig });
            Path Fill(Color col, bool above) => new Path
            {
                Data = area,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(110, col.R, col.G, col.B), above ? 0 : 1),
                    new GradientStop(Color.FromArgb(10, col.R, col.G, col.B), above ? 1 : 0),
                }, new Point(0, 0), new Point(0, 1)) { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, above ? top : mid), EndPoint = new Point(0, above ? mid : top + h) },
                Clip = new RectangleGeometry(above ? new Rect(0, 0, w, mid) : new Rect(0, mid, w, full - mid)),
            };
            c.Children.Add(Fill(Theme.WinC, true));
            c.Children.Add(Fill(Theme.LossC, false));
            c.Children.Add(new Line { X1 = left, X2 = w - right, Y1 = mid, Y2 = mid, Stroke = Theme.Line, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });
            var line = new Polyline { Stroke = Theme.Vellum, StrokeThickness = detailed ? 2 : 1.5, StrokeLineJoin = PenLineJoin.Round };
            for (var i = 0; i < n; i++) line.Points.Add(new Point(X(i), Y(wc.Values[i])));
            c.Children.Add(line);

            void Label(string text, double x, double y, Brush b, TextAlignment align = TextAlignment.Left, double size = 11.5)
            {
                var t = Theme.Text(text, size, b);
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(t, align == TextAlignment.Right ? x - t.DesiredSize.Width : align == TextAlignment.Center ? x - t.DesiredSize.Width / 2 : x);
                Canvas.SetTop(t, y);
                c.Children.Add(t);
            }

            if (detailed)
            {
                Label("100%", left - 6, top - 7, Theme.Dusk, TextAlignment.Right);
                Label("50%", left - 6, mid - 8, Theme.Dusk, TextAlignment.Right);
                Label("0%", left - 6, top + h - 9, Theme.Dusk, TextAlignment.Right);
                var starts = r.TurnStarts.Where(kv => kv.Value < n && r.Frames[kv.Value].YourTurn).OrderBy(kv => kv.Key).ToList();
                var step = Math.Max(1, (int)Math.Ceiling(starts.Count / 12.0));
                for (var i = 0; i < starts.Count; i += step)
                    Label(((starts[i].Key + 1) / 2).ToString(), X(starts[i].Value), top + h + 5, Theme.Dusk, TextAlignment.Center);
            }

            foreach (var m in wc.Drops)
            {
                var size = detailed ? 11 : 7;
                var dot = new Ellipse { Width = size, Height = size, Fill = Theme.Loss, Stroke = Theme.Night, StrokeThickness = detailed ? 2 : 1,
                    ToolTip = $"Turn {m.Round}: {m.Before:P0} to {m.After:P0} - {m.Action}" };
                Canvas.SetLeft(dot, X(m.Frame) - size / 2.0); Canvas.SetTop(dot, Y(m.After) - size / 2.0);
                if (onFrame != null)
                {
                    dot.Cursor = Cursors.Hand;
                    dot.MouseLeftButtonDown += (s, e) => { e.Handled = true; onFrame(m.Frame); };
                }
                c.Children.Add(dot);
            }
            if (current >= 0 && current < n)
                c.Children.Add(Pos(new Rectangle { Width = 2, Height = full, Fill = Theme.Vellum, Opacity = 0.85 }, X(current) - 1, 0));
        }

        private static T Pos<T>(T el, double x, double y) where T : UIElement { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); return el; }
    }
}
