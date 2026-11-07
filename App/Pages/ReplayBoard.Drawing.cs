using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HearthStoner
{
    public static partial class ReplayBoard
    {

        /// <summary>White (or coloured) numbers with a dark outline, as the game draws them.</summary>
        private static FrameworkElement Outlined(string text, double size, Brush fill)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Theme.Display, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size, Brushes.White, 1.0);
            var geo = ft.BuildGeometry(new Point(0, 0));
            var b = geo.Bounds;
            if (b.IsEmpty) return new Grid();
            // Move the glyphs to the origin (plus room for the outline) in the geometry itself: a render transform
            // would be applied after layout clipping and cut the digits off.
            var stroke = Math.Max(1.5, size * 0.16);
            var pad = stroke / 2 + 0.5;
            geo.Transform = new TranslateTransform(pad - b.X, pad - b.Y);
            var g = new Grid { Width = b.Width + 2 * pad, Height = b.Height + 2 * pad, IsHitTestVisible = false };
            g.Children.Add(new Path { Data = geo, Stroke = Brushes.Black, StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round });
            g.Children.Add(new Path { Data = geo, Fill = fill });
            return g;
        }


        private static FrameworkElement Round(string cardId, double size, Color ring, AppData d)
        {
            var art = ArtCache.Get(cardId);
            return new Ellipse
            {
                Width = size, Height = size, Stroke = Theme.B(ring), StrokeThickness = 3.5, ToolTip = d.Cards.NameOf(cardId),
                Fill = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.2, 0.1, 0.6, 0.6) } : Theme.B(Stone),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.7 },
            };
        }


        private static TextBlock Text(string s, double size, Brush b, FontWeight? weight = null) =>
            new TextBlock { Text = s, FontSize = size, Foreground = b, FontWeight = weight ?? FontWeights.Normal, FontFamily = Theme.Display, TextTrimming = TextTrimming.CharacterEllipsis };


        private static TextBlock Shadowed(TextBlock t)
        {
            t.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };
            return t;
        }


        private static T At<T>(T el, double x, double y) where T : UIElement { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); return el; }
    }
}
