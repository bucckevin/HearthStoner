using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HearthStoner
{
    /// <summary>Draws advice sections (text lines and card bars). Shared by the overlay and the app window.</summary>
    public static class SectionView
    {
        public static void Fill(Panel panel, IEnumerable<AdviceSection> sections, bool cardArt, bool hoverMode, double titleSize = 13)
        {
            foreach (var sec in sections)
            {
                var first = panel.Children.Count == 0;
                if (sec.Bar) panel.Children.Add(HeaderBar(sec.Title, sec.RightText, first, titleSize));
                else panel.Children.Add(new TextBlock
                {
                    Text = sec.Title,
                    FontWeight = FontWeights.Bold,
                    FontSize = titleSize,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 214, 120)),
                    Margin = new Thickness(0, first ? 0 : 8, 0, 2),
                    TextWrapping = TextWrapping.Wrap,
                });
                foreach (var line in sec.Lines)
                {
                    UIElement el;
                    if (line.CardId != null) el = CardTile(line, cardArt, hoverMode);
                    else
                    {
                        el = new TextBlock
                        {
                            Text = line.Text,
                            FontSize = line.Tone == Tone.Dim ? 11 : 12,
                            FontWeight = line.Tone == Tone.Heading ? FontWeights.SemiBold : FontWeights.Normal,
                            Foreground = new SolidColorBrush(ColorFor(line.Tone)),
                            TextWrapping = TextWrapping.Wrap,
                            TextDecorations = line.Struck ? TextDecorations.Strikethrough : null,
                            Opacity = line.Struck ? 0.6 : 1.0,
                        };
                    }
                    if (line.Pulse) Pulse(el);
                    panel.Children.Add(el);
                }
            }
        }

        /// <summary>Fade in and out forever, to draw the eye.</summary>
        private static void Pulse(UIElement el)
        {
            var anim = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>A gold glow that fades out over two seconds (the card you just drew).</summary>
        private static Border FlashOverlay()
        {
            var glow = new Border
            {
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 215, 90)),
                Background = new SolidColorBrush(Color.FromArgb(90, 255, 215, 90)),
                IsHitTestVisible = false,
            };
            glow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(2.2)) { BeginTime = TimeSpan.FromMilliseconds(300) });
            return glow;
        }


        private static T Frozen<T>(T b) where T : Freezable { b.Freeze(); return b; }

        // Deck-tracker look (as in HSReplay's / HDT's trackers): dark bars packed 1px apart, a blue mana crystal,
        // the name in outlined white, the card's art on the right fading into the bar, a gold count box.
        private const double RowHeight = 26;
        private static readonly Brush RowBase = Frozen(new SolidColorBrush(Color.FromRgb(28, 30, 38)));
        private static readonly Brush RowBorder = Frozen(new SolidColorBrush(Color.FromRgb(12, 13, 18)));
        private static readonly Brush ArtFade = Frozen(new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(255, 28, 30, 38), 0.0),
                new GradientStop(Color.FromArgb(200, 28, 30, 38), 0.25),
                new GradientStop(Color.FromArgb(0, 28, 30, 38), 0.7),
            }, new Point(0, 0), new Point(1, 0)));
        private static readonly Brush GemFill = Frozen(new LinearGradientBrush(Color.FromRgb(92, 160, 255), Color.FromRgb(22, 58, 150), 90));
        private static readonly Brush GemEdge = Frozen(new SolidColorBrush(Color.FromRgb(8, 20, 60)));
        private static readonly Brush CountBox = Frozen(new SolidColorBrush(Color.FromRgb(20, 21, 27)));
        private static readonly Brush CountGold = Frozen(new SolidColorBrush(Color.FromRgb(255, 209, 92)));
        private static readonly Brush OddsBox = Frozen(new SolidColorBrush(Color.FromArgb(220, 20, 21, 27)));
        private static readonly Brush OddsBrush = Frozen(new SolidColorBrush(Color.FromRgb(196, 202, 214)));
        private static readonly Brush DrawnShade = Frozen(new SolidColorBrush(Color.FromArgb(150, 10, 11, 15)));
        private static readonly Brush HeaderBrush = Frozen(new LinearGradientBrush(Color.FromRgb(58, 44, 22), Color.FromRgb(30, 26, 20), 90));
        private static readonly Brush HeaderEdge = Frozen(new SolidColorBrush(Color.FromRgb(150, 112, 46)));
        // A crystal: a hexagon with a lighter facet, like the in-game mana gem.
        private static readonly Geometry GemShape = Frozen(Geometry.Parse("M 10,0 L 19,5 L 19,15 L 10,20 L 1,15 L 1,5 Z"));
        private static readonly Geometry GemFacet = Frozen(Geometry.Parse("M 10,1.5 L 17.5,5.8 L 10,10 L 2.5,5.8 Z"));

        /// <summary>White text with a dark outline, readable over any card art.</summary>
        private static TextBlock Outlined(string text, double size, FontWeight weight, Brush fg = null) => new TextBlock
        {
            Text = text, FontSize = size, FontWeight = weight, Foreground = fg ?? Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 3, Color = Colors.Black, Opacity = 1 },
        };

        /// <summary>Header bar over a deck list: name on the left, a count on the right.</summary>
        private static UIElement HeaderBar(string title, string right, bool first, double size)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = Outlined(title, size, FontWeights.Bold, CountGold);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(t);
            if (!string.IsNullOrEmpty(right))
            {
                var r = Outlined(right, size - 1, FontWeights.SemiBold);
                Grid.SetColumn(r, 1);
                g.Children.Add(r);
            }
            return new Border
            {
                Background = HeaderBrush, BorderBrush = HeaderEdge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, first ? 0 : 8, 0, 2), Child = g,
            };
        }

        /// <summary>
        /// One card bar: mana crystal | name over the art | draw odds | count box (copies left, or a star for a
        /// legendary). Cards with no copies left are shaded dark.
        /// </summary>
        public static UIElement CardTile(AdviceLine line, bool showArt, bool hoverMode)
        {
            const double h = RowHeight;
            var grid = new Grid { Height = h, Margin = new Thickness(0, 0, 0, 1), Background = RowBase, ClipToBounds = true };
            if (hoverMode) AttachHoverCard(grid, line);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(h) });                    // gem
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // odds
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // count

            // Art: right-aligned at its natural shape (tiles are about 4.3:1), fading in from the left.
            var img = showArt ? CardTiles.Get(line.CardId) : null;
            if (img != null)
            {
                var artWidth = h * 4.6;
                var art = new Border
                {
                    Width = artWidth, HorizontalAlignment = HorizontalAlignment.Right,
                    Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Center },
                };
                var fade = new Border { Width = artWidth, HorizontalAlignment = HorizontalAlignment.Right, Background = ArtFade };
                Grid.SetColumn(art, 1); Grid.SetColumnSpan(art, 3);
                Grid.SetColumn(fade, 1); Grid.SetColumnSpan(fade, 3);
                grid.Children.Add(art);
                grid.Children.Add(fade);
            }

            // Mana crystal with the cost.
            var gem = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            gem.Children.Add(new System.Windows.Shapes.Path { Data = GemShape, Fill = GemFill, Stroke = GemEdge, StrokeThickness = 1.2 });
            gem.Children.Add(new System.Windows.Shapes.Path { Data = GemFacet, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) });
            var cost = Outlined(line.Cost?.ToString() ?? "-", 13, FontWeights.Bold);
            cost.HorizontalAlignment = HorizontalAlignment.Center;
            gem.Children.Add(cost);
            var gemCell = new Border { Background = RowBorder, Child = gem };
            Grid.SetColumn(gemCell, 0);
            grid.Children.Add(gemCell);

            var nameColor = line.Tone == Tone.Warn || line.Tone == Tone.Bad || line.Tone == Tone.Good
                ? new SolidColorBrush(ColorFor(line.Tone)) : Brushes.White;
            var name = Outlined(line.CardName ?? line.CardId, 12.5, FontWeights.SemiBold, nameColor);
            name.Margin = new Thickness(6, 0, 4, 0);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            if (!string.IsNullOrEmpty(line.Detail) && !line.Struck)
            {
                // In its own dark box so it stays readable over bright art.
                var odds = new Border
                {
                    MinWidth = 22, Padding = new Thickness(4, 0, 4, 0), Background = OddsBox, BorderBrush = RowBorder, BorderThickness = new Thickness(1, 0, 0, 0),
                    Child = new TextBlock { Text = line.Detail, FontSize = 10.5, Foreground = OddsBrush, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                };
                Grid.SetColumn(odds, 2);
                grid.Children.Add(odds);
            }

            // Count box: copies left (1 included), or a star for a legendary. Hidden for drawn-out cards.
            var badge = line.Badge;
            if (!string.IsNullOrEmpty(badge) && badge != "0")
            {
                var badgeBrush = badge == "\u2714" ? new SolidColorBrush(ColorFor(Tone.Good))
                    : badge == "\u2716" ? new SolidColorBrush(ColorFor(Tone.Bad)) : CountGold;
                var count = Outlined(badge, badge.Length > 2 ? 11 : 13, FontWeights.Bold, badgeBrush);
                count.HorizontalAlignment = HorizontalAlignment.Center;
                var box = new Border { MinWidth = 22, Padding = new Thickness(3, 0, 3, 0), Background = CountBox, BorderBrush = RowBorder, BorderThickness = new Thickness(1, 0, 0, 0), Child = count };
                Grid.SetColumn(box, 3);
                grid.Children.Add(box);
            }

            var outline = new Border { BorderBrush = RowBorder, BorderThickness = new Thickness(1), IsHitTestVisible = false };
            Grid.SetColumnSpan(outline, 4);
            grid.Children.Add(outline);

            if (line.Struck)
            {
                // No copies left: dark shade over the whole bar, like a drawn-out card in a deck tracker.
                var shade = new Border { Background = DrawnShade, IsHitTestVisible = false };
                Grid.SetColumnSpan(shade, 4);
                grid.Children.Add(shade);
                name.Foreground = new SolidColorBrush(Color.FromRgb(150, 154, 165));
            }
            if (line.Flash)
            {
                var glow = FlashOverlay();
                Grid.SetColumnSpan(glow, 4);
                grid.Children.Add(glow);
            }
            return grid;
        }

        /// <summary>
        /// Shows the card picture next to the mouse while hovering. A plain transparent Popup instead of a ToolTip,
        /// because the ToolTip's window style can draw a white box behind the picture.
        /// </summary>
        private static void AttachHoverCard(FrameworkElement target, AdviceLine line)
        {
            System.Windows.Controls.Primitives.Popup popup = null;
            target.MouseEnter += (s, e) =>
            {
                if (popup == null)
                {
                    popup = new System.Windows.Controls.Primitives.Popup
                    {
                        AllowsTransparency = true,
                        Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                        HorizontalOffset = 18,
                        VerticalOffset = -40,
                        IsHitTestVisible = false,
                        PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None,
                        PlacementTarget = target,
                    };
                }
                popup.Child = (UIElement)CardTooltip(line);   // rebuilt each time so a freshly downloaded picture shows
                popup.IsOpen = true;
            };
            target.MouseLeave += (s, e) => { if (popup != null) popup.IsOpen = false; };
            target.Unloaded += (s, e) => { if (popup != null) popup.IsOpen = false; };
        }

        /// <summary>Full card picture plus the advice text; the picture downloads the first time.</summary>
        /// <summary>
        /// The full card picture; the card's name and cost are on it, so nothing else is shown. Only while the
        /// picture is still downloading (first hover), the line's text stands in for it.
        /// </summary>
        private static object CardTooltip(AdviceLine line)
        {
            var panel = new StackPanel { MaxWidth = 280, Background = Brushes.Transparent };
            var render = CardTiles.GetRender(line.CardId);
            if (render != null)
            {
                panel.Children.Add(new Image { Source = render, Width = 256, Stretch = Stretch.Uniform });
                return panel;
            }
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 18, 20, 28)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 4),
                Margin = new Thickness(0, 2, 0, 0),
                Child = new TextBlock { Text = line.Text.Trim(), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(230, 232, 240)), FontSize = 12 },
            });
            return panel;
        }

        public static Color ColorFor(Tone t)
        {
            switch (t)
            {
                case Tone.Good: return Color.FromRgb(120, 220, 130);
                case Tone.Bad: return Color.FromRgb(255, 110, 100);
                case Tone.Warn: return Color.FromRgb(255, 190, 90);
                case Tone.Dim: return Color.FromRgb(150, 158, 175);
                case Tone.Heading: return Color.FromRgb(140, 200, 255);
                default: return Color.FromRgb(230, 232, 240);
            }
        }

    }
}
