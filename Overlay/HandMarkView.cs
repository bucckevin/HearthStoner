using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Overlay
{
    /// <summary>Draws the marks over the opponent's hand (shared by the overlay and 'HearthStoner preview').</summary>
    public static class HandMarkView
    {
        private static Color KindColor(HandMarkKind k)
        {
            switch (k)
            {
                case HandMarkKind.Kept:
                case HandMarkKind.Mulliganed: return Color.FromRgb(255, 209, 92);   // gold: from the opening hand
                case HandMarkKind.Coin: return Color.FromRgb(230, 200, 120);
                case HandMarkKind.Created: return Color.FromRgb(200, 150, 255);     // purple: created by a card
                case HandMarkKind.Returned: return Color.FromRgb(255, 170, 80);     // orange: a card you've seen
                case HandMarkKind.Stolen: return Color.FromRgb(255, 110, 100);      // red: one of your cards
                default: return Colors.White;
            }
        }

        private static string Label(HandMark m)
        {
            switch (m.Kind)
            {
                case HandMarkKind.Kept: return "Mull";
                case HandMarkKind.Mulliganed: return "Mull";
                case HandMarkKind.Coin: return "Coin";
                case HandMarkKind.Created: return m.Turn > 0 ? m.Turn.ToString() : "New";
                case HandMarkKind.Returned: return "Back";
                case HandMarkKind.Stolen: return "Yours";
                default: return m.Turn.ToString();   // the turn it arrived
            }
        }

        private static string Caption(HandMark m)
        {
            switch (m.Kind)
            {
                case HandMarkKind.Kept: return "kept";
                case HandMarkKind.Mulliganed: return "redrawn";
                case HandMarkKind.Created: return m.CardName ?? (m.Note != null ? "by " + m.Note : "created");
                case HandMarkKind.Returned: return m.CardName ?? "returned";
                case HandMarkKind.Stolen: return m.CardName ?? "stolen";
                default: return m.CardName;   // a drawn card is only named if it's public
            }
        }

        /// <summary>One mark: a big label (the turn it arrived as a plain number, Mull, Coin...) over a small caption.</summary>
        public static FrameworkElement Build(HandMark m, double scale)
        {
            var color = KindColor(m.Kind);
            var label = Label(m);
            var number = label.All(char.IsDigit);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = (number ? 16 : 14) * scale, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 3, Color = Colors.Black, Opacity = 1 },
            });
            var caption = Caption(m);
            if (!string.IsNullOrEmpty(caption))
                stack.Children.Add(new TextBlock
                {
                    Text = caption, FontSize = 9.5 * scale, Foreground = new SolidColorBrush(Color.FromRgb(225, 228, 236)),
                    HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 92 * scale, TextTrimming = TextTrimming.CharacterEllipsis,
                });
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(215, 16, 17, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B)),
                BorderThickness = new Thickness(Math.Max(1, 1.2 * scale)),
                CornerRadius = new CornerRadius(5 * scale),
                Padding = new Thickness(5 * scale, 1 * scale, 5 * scale, 2 * scale),
                MinWidth = (number && string.IsNullOrEmpty(caption) ? 26 : 34) * scale,
                IsHitTestVisible = false,
                Child = stack,
            };
        }

        /// <summary>Size of the marks for a game window this tall (designed at 1080p).</summary>
        public static double ScaleFor(double height) => Math.Max(0.6, Math.Min(1.6, height / 1080.0));

        /// <summary>Puts a mark over each card of a hand of <paramref name="handCount"/> cards.</summary>
        public static void Place(Canvas canvas, IList<HandMark> marks, int handCount, double width, double height)
        {
            canvas.Children.Clear();
            if (marks == null || marks.Count == 0 || width < 100 || height < 100) return;
            var scale = ScaleFor(height);
            foreach (var m in marks)
            {
                var el = Build(m, scale);
                el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var (x, y) = HandGeometry.Position(handCount, m.Slot, width, height);
                Canvas.SetLeft(el, x - el.DesiredSize.Width / 2);
                Canvas.SetTop(el, Math.Max(5, y - el.DesiredSize.Height / 3));
                canvas.Children.Add(el);
            }
        }
    }
}
