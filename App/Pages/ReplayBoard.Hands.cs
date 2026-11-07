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

        private static void Mana(Canvas c, Replay.Side s, bool you)
        {
            // Where the game has them: yours along the bottom right, theirs at the top right. "Mana/crystals" sits in a
            // dark pill, the crystals after it.
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (_board != null)
            {
                // The rendered board has the game's blue mana capsules (yours 540..590 x 540..570, theirs 525..570 x 25..47):
                // the number goes on the capsule, the crystals right after it.
                double cx = you ? 540 : 525, cy = you ? 540 : 25, cw = you ? 50 : 45, ch = you ? 30 : 22;
                var label = Shadowed(Text($"{s.Mana}/{s.Crystals}", you ? 15 : 13, Brushes.White, FontWeights.Bold));
                label.Width = cw; label.TextAlignment = TextAlignment.Center;
                c.Children.Add(At(label, cx, cy + (ch - (you ? 20 : 17)) / 2));
                // The game shows crystals for you only (ten slots from x 588, 17.6 apart); theirs is just the number.
                if (!you) return;
                Crystals(row, s, 16, 17, 1.6);
                c.Children.Add(At(row, 588, 537));
                return;
            }
            var y = you ? 548 : 26;
            var x = CX + 122;
            row.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(0, 0, 5, 0), MinWidth = 40,
                Background = new LinearGradientBrush(Theme.Rgb(0x23, 0x3A, 0x6A), Theme.Rgb(0x0E, 0x18, 0x34), 90),
                BorderBrush = Theme.B(Theme.Rgb(0x6A, 0x8C, 0xC8)), BorderThickness = new Thickness(1.2),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1, Opacity = 0.8 },
                Child = new TextBlock { Text = $"{s.Mana}/{s.Crystals}", FontSize = 13, FontWeight = FontWeights.Bold, FontFamily = Theme.Display, Foreground = Brushes.White, TextAlignment = TextAlignment.Center },
            });
            Crystals(row, s);
            c.Children.Add(At(row, x, y));
        }


        /// <summary>The mana crystals: full, spent, and locked by overload (the game's crystal art when it's there).</summary>
        private static void Crystals(StackPanel row, Replay.Side s, double cw = 14, double ch = 15, double gap = 1)
        {
            var shown = Math.Max(s.Crystals, Math.Min(s.Mana, 10));
            for (var i = 0; i < Math.Min(shown, 10); i++)
            {
                var locked = i >= s.Crystals - s.Overload && i < s.Crystals && s.Overload > 0;
                var full = i < s.Mana;
                var crystal = GameArt.Get(locked ? "mana-locked" : full ? "mana" : "mana-empty");
                if (crystal != null)
                {
                    row.Children.Add(new Image { Source = crystal, Width = cw, Height = ch, Stretch = Stretch.Fill, Margin = new Thickness(0, 1.5, gap, 0),
                        Effect = full ? new DropShadowEffect { Color = Theme.Rgb(0x6A, 0xB8, 0xFF), BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8 } : null });
                    continue;
                }
                row.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 6,0 L 12,3.25 L 12,9.75 L 6,13 L 0,9.75 L 0,3.25 Z"), Width = 13, Height = 14, Stretch = Stretch.Fill, Margin = new Thickness(0, 2, 1.5, 0),
                    Fill = locked ? new RadialGradientBrush(Theme.Rgb(0xFF, 0x8A, 0x7A), Theme.Rgb(0xA0, 0x24, 0x24)) { GradientOrigin = new Point(0.35, 0.3) }
                        : full ? new RadialGradientBrush(Theme.Rgb(0xB8, 0xE8, 0xFF), Theme.Rgb(0x22, 0x62, 0xE0)) { GradientOrigin = new Point(0.35, 0.3) }
                        : (Brush)Theme.B(Theme.Rgb(0x1E, 0x24, 0x36)),
                    Stroke = Theme.B(full || locked ? Theme.Rgb(0x0C, 0x1C, 0x40) : Theme.Rgb(0x4A, 0x56, 0x74)), StrokeThickness = 1,
                    Effect = full ? new DropShadowEffect { Color = Theme.Rgb(0x6A, 0xB8, 0xFF), BlurRadius = 5, ShadowDepth = 0, Opacity = 0.7 } : null,
                });
            }
        }


        // ------------------------------------------------------------------ hands

        private static void OpponentHand(Canvas c, Replay.Frame f, Replay r, bool showHidden, AppData d)
        {
            var hand = f.Them.Hand;
            var n = hand.Count;
            if (n == 0) return;
            const double w = 44, h = 62;
            var spacing = Math.Min(38, 340.0 / n);
            var step = Math.Min(5, 36.0 / n);
            for (var i = 0; i < n; i++)
            {
                var card = hand[i];
                var off = i - (n - 1) / 2.0;
                var id = card.CardId ?? (showHidden && r.FinalCardIds.TryGetValue(card.EntityId, out var later) ? later : null);
                var render = id != null ? CardTiles.GetRender(id) : null;
                // A card render has empty space around the card (its body is 12..236 x 30..344 of 256x388): crop to the
                // body so a revealed card is exactly the size of a card back.
                FrameworkElement el = render != null
                    ? new Border { Width = w, Height = h, Opacity = card.CardId == null ? 0.8 : 1, ToolTip = d.Cards.NameOf(id),
                        Background = new ImageBrush(render) { Viewbox = new Rect(12 / 256.0, 30 / 388.0, 224 / 256.0, 314 / 388.0), Stretch = Stretch.Fill } }
                    : CardBack(w, h);
                var cell = new Grid { Width = w, Height = h + 18 };
                el.VerticalAlignment = VerticalAlignment.Top;
                cell.Children.Add(el);
                if (card.Mark != null)
                {
                    var m = HandMarkView.Build(card.Mark, 0.56);
                    m.HorizontalAlignment = HorizontalAlignment.Center;
                    m.VerticalAlignment = VerticalAlignment.Bottom;
                    cell.Children.Add(m);
                }
                cell.RenderTransformOrigin = new Point(0.5, 0);
                cell.RenderTransform = new RotateTransform(-off * step);
                c.Children.Add(At(cell, CX - w / 2 + off * spacing, -28 + Math.Abs(off) * Math.Abs(off) * 0.9));
            }
        }


        private static void YourHand(Canvas c, Replay.Frame f, AppData d)
        {
            var hand = f.You.Hand;
            var n = hand.Count;
            if (n == 0) return;
            const double w = 71, h = 104;   // the game's hand card, centred on y 571
            var spacing = Math.Min(56, 300.0 / n);   // the game's hand is centred at x 380 and stays left of the mana counter
            var step = Math.Min(5, 32.0 / n);
            for (var i = 0; i < n; i++)
            {
                var card = hand[i];
                var off = i - (n - 1) / 2.0;
                var render = card.CardId != null ? CardTiles.GetRender(card.CardId) : null;
                FrameworkElement el = render != null
                    ? new Image { Source = render, Width = w, Height = h, Stretch = Stretch.Uniform }
                    : (FrameworkElement)new Border
                    {
                        Width = w, Height = h, CornerRadius = new CornerRadius(6), Background = Theme.B(Theme.Rgb(0x3A, 0x2C, 0x20)), BorderBrush = Theme.B(Trim), BorderThickness = new Thickness(1.5),
                        Child = new TextBlock { Text = d.Cards.NameOf(card.CardId), FontSize = 10, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) },
                    };
                el.ToolTip = d.Cards.NameOf(card.CardId);
                // Cards you could afford on your turn glow, like playable cards in the game.
                if (f.YourTurn && !f.Mulligan && card.Cost <= f.You.Mana)
                    el.Effect = new DropShadowEffect { Color = Ready, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.95 };
                el.RenderTransformOrigin = new Point(0.5, 1);
                el.RenderTransform = new RotateTransform(off * step);
                c.Children.Add(At(el, 380 - w / 2 + off * spacing, H - 81 + Math.Abs(off) * Math.Abs(off) * 1.4));
            }
        }


        /// <summary>The game's classic card back when it's been pulled from the game, else a drawn one.</summary>
        private static FrameworkElement CardBack(double w, double h) => GameArt.Get("card-back") is BitmapSource back
            ? new Image { Source = back, Width = w, Height = h, Stretch = Stretch.Fill, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 0.7 } }
            : (FrameworkElement)new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(5), BorderBrush = Theme.B(Theme.GoldC, 0.8), BorderThickness = new Thickness(1.5),
            Background = new LinearGradientBrush(Theme.Rgb(0x7B, 0x46, 0x28), Theme.Rgb(0x3A, 0x22, 0x16), 90),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 0.6 },
            Child = new Path { Data = Geometry.Parse("M 6,0 L 12,3.25 L 12,9.75 L 6,13 L 0,9.75 L 0,3.25 Z"), Fill = Theme.B(Theme.GoldC, 0.6), Width = 14, Height = 15, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }
}
