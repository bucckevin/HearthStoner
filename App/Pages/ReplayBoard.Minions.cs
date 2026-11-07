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

        /// <summary>The card that attacked in frame <paramref name="i"/> (a minion, or the hero), and whose it was.</summary>
        private static bool Attacker(Replay r, int i, out string cardId, out bool you)
        {
            var who = r.Frames[i].AttackerId;
            foreach (var fr in new[] { r.Frames[i], i > 0 ? r.Frames[i - 1] : null })
            {
                if (fr == null) continue;
                foreach (var (s, mine) in new[] { (fr.You, true), (fr.Them, false) })
                {
                    var m = s.Board.FirstOrDefault(x => x.EntityId == who);
                    cardId = m?.CardId ?? (s.HeroEntityId == who ? s.HeroCardId : null);
                    you = mine;
                    if (cardId != null) return true;
                }
            }
            cardId = null; you = false;
            return false;
        }


        // ------------------------------------------------------------------ positions

        private static double MinionX(int i, int n) => CX + (i - (n - 1) / 2.0) * Spacing;


        private static void Place(Replay.Side s, double heroY, double rowY, Dictionary<int, Point> into)
        {
            if (s.HeroEntityId != 0) into[s.HeroEntityId] = new Point(CX, heroY);
            for (var i = 0; i < s.Board.Count; i++) into[s.Board[i].EntityId] = new Point(MinionX(i, s.Board.Count), rowY);
        }


        private static FrameworkElement Minion(Replay.Minion m, bool active, AppData d)
        {
            var frameArt = GameArt.Get("minion-frame");
            if (frameArt != null) return MinionInFrame(m, active, d, frameArt);
            var info = d.Cards.ById(m.CardId);
            var g = new Grid { Width = MinionW + 22, Height = MinionH + 14, ToolTip = d.Cards.NameOf(m.CardId) };
            if (m.Taunt)
                g.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 46,0 L 92,14 L 88,70 Q 80,100 46,112 Q 12,100 4,70 L 0,14 Z"), Width = MinionW + 18, Height = MinionH + 14, Stretch = Stretch.Fill,
                    Fill = new LinearGradientBrush(Theme.Rgb(0x9C, 0x92, 0x84), Theme.Rgb(0x5A, 0x52, 0x48), 90), Stroke = Theme.B(Theme.Rgb(0x3A, 0x34, 0x2C)), StrokeThickness = 2,
                });
            var art = ArtCache.Get(m.CardId);
            var legendary = info?.Rarity == "LEGENDARY";
            var canAttack = active && !m.Exhausted && m.Attack > 0 && !m.Frozen && !m.Dormant;
            var face = new Ellipse
            {
                Width = MinionW, Height = MinionH,
                Fill = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.14, 0.04, 0.72, 0.7) } : Theme.B(Stone),
                Stroke = Theme.B(legendary ? Theme.GoldC : Stone), StrokeThickness = legendary ? 4.5 : 3.5,
                Effect = canAttack ? new DropShadowEffect { Color = Ready, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 }
                    : new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 3, Opacity = 0.75 },
            };
            g.Children.Add(face);
            if (legendary)
                g.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 0,10 Q 8,0 16,6 L 20,0 L 24,6 Q 32,0 40,10 L 34,12 L 20,8 L 6,12 Z"), Fill = Theme.Gold, Stroke = Theme.B(Theme.Rgb(0x6B, 0x4A, 0x14)), StrokeThickness = 1,
                    Width = 40, Height = 12, Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0),
                });
            if (art == null)
                g.Children.Add(new TextBlock { Text = d.Cards.NameOf(m.CardId), FontSize = 10, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14) });
            if (m.DivineShield)
                g.Children.Add(new Ellipse { Width = MinionW + 10, Height = MinionH + 10, Stroke = Theme.B(Theme.Rgb(0xFF, 0xE0, 0x7A)), StrokeThickness = 3, Fill = Theme.B(Theme.Rgb(0xFF, 0xE0, 0x7A), 0.22),
                    Effect = new DropShadowEffect { Color = Theme.Rgb(0xFF, 0xE0, 0x7A), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 } });
            if (m.Frozen)
                g.Children.Add(new Ellipse { Width = MinionW, Height = MinionH, Fill = Theme.B(Theme.Rgb(0x8F, 0xD8, 0xFF), 0.38), Stroke = Theme.B(Theme.Rgb(0xC8, 0xF0, 0xFF)), StrokeThickness = 2 });
            if (m.Stealth)
                g.Children.Add(new Ellipse { Width = MinionW, Height = MinionH, Fill = Theme.B(Colors.Black, 0.35), Stroke = Theme.B(Colors.White, 0.5), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 2 } });
            if (m.Dormant)
            {
                g.Children.Add(new Ellipse { Width = MinionW, Height = MinionH, Fill = Theme.B(Theme.Rgb(0x20, 0x20, 0x30), 0.55) });
                g.Children.Add(new TextBlock { Text = "Zzz", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Theme.B(Theme.Rgb(0xC8, 0xC8, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }

            // Numbers: green above the card's own value, red when damaged.
            var atkFill = info?.Attack != null && m.Attack > info.Attack ? Theme.B(Theme.Rgb(0x8C, 0xFF, 0x8C)) : Brushes.White;
            var hpFill = m.Health < m.MaxHealth ? Theme.B(Theme.Rgb(0xFF, 0x8A, 0x8A)) : info?.Health != null && m.MaxHealth > info.Health ? Theme.B(Theme.Rgb(0x8C, 0xFF, 0x8C)) : Brushes.White;
            g.Children.Add(Gem(m.Attack.ToString(), 28, Theme.Rgb(0xF2, 0xC2, 0x3A), atkFill, HorizontalAlignment.Left));
            g.Children.Add(Gem(m.Health.ToString(), 28, Theme.Rgb(0xC6, 0x2B, 0x2B), hpFill, HorizontalAlignment.Right, drop: true));

            var icon = KeywordIcon(m);
            if (icon != null) g.Children.Add(icon);
            return g;
        }


        /// <summary>A minion in the game's stone oval frame, with its effects drawn from the game's art when available.</summary>
        private static FrameworkElement MinionInFrame(Replay.Minion m, bool active, AppData d, ImageSource frameArt)
        {
            const double k = MinionK;                                // the frame art is 410x504, its opening 66..348 x 49..471
            double fw = 410 * k, fh = 504 * k;
            double pw = (348 - 66) * k + 3, ph = (471 - 49) * k + 3;
            var info = d.Cards.ById(m.CardId);
            var g = new Grid { Width = fw + 12, Height = fh + 8, ToolTip = d.Cards.NameOf(m.CardId) };
            if (m.Taunt)
            {
                // The game's stone shield behind the frame. Its art is 186x243 with the oval opening at 20..168 x 10..212:
                // sized so the opening matches the frame's outer oval (400 wide in the frame art), and lined up on it.
                var tauntArt = GameArt.Get("taunt");
                double ts = 400 * k / 148, sw = 186 * ts, sh = 243 * ts, sl = g.Width / 2 - 94 * ts, st = g.Height / 2 - 111 * ts;
                g.Children.Add(tauntArt != null
                    ? new Image { Source = tauntArt, Stretch = Stretch.Fill, IsHitTestVisible = false, Width = sw, Height = sh,
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(sl, st, g.Width - sl - sw, g.Height - st - sh),
                        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.7 } }
                    : (UIElement)new Path
                    {
                        Data = Geometry.Parse("M 46,0 L 92,14 L 88,70 Q 80,100 46,112 Q 12,100 4,70 L 0,14 Z"), Width = fw + 10, Height = fh + 8, Stretch = Stretch.Fill,
                        Fill = new LinearGradientBrush(Theme.Rgb(0xA8, 0x9E, 0x8E), Theme.Rgb(0x5A, 0x52, 0x48), 90), Stroke = Theme.B(Theme.Rgb(0x3A, 0x34, 0x2C)), StrokeThickness = 2,
                    });
            }
            var art = ArtCache.Get(m.CardId);
            g.Children.Add(new Ellipse
            {
                Width = pw, Height = ph, Margin = new Thickness(0, 2 * (260 - 252) * k, 0, 0),
                Fill = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.14, 0.04, 0.72, 0.7) } : Theme.B(Stone),
            });
            if (art == null)
                g.Children.Add(new TextBlock { Text = d.Cards.NameOf(m.CardId), FontSize = 10, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) });
            if (m.Stealth)
                g.Children.Add(new Ellipse { Width = pw, Height = ph, Fill = Theme.B(Colors.Black, 0.4) });
            if (m.Dormant)
            {
                g.Children.Add(new Ellipse { Width = pw, Height = ph, Fill = Theme.B(Theme.Rgb(0x20, 0x20, 0x30), 0.55) });
                g.Children.Add(new TextBlock { Text = "Zzz", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Theme.B(Theme.Rgb(0xC8, 0xC8, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            var canAttack = active && !m.Exhausted && m.Attack > 0 && !m.Frozen && !m.Dormant;
            g.Children.Add(new Image
            {
                Source = frameArt, Width = fw, Height = fh, Stretch = Stretch.Fill,
                Effect = canAttack ? new DropShadowEffect { Color = Ready, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 }
                    : new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 3, Opacity = 0.8 },
            });
            if (info?.Rarity == "LEGENDARY")
            {
                // The game's dragon over the top of the frame: head at the upper left, wing and tail down the right side
                // (placed as measured on a legendary in a real game: 1.10 x 0.59 of the frame, from 12% in, 9.5% above).
                var dragon = GameArt.Get("legendary");
                if (dragon != null)
                {
                    double dw = fw * 1.10, dh = fh * 0.59, dl = g.Width / 2 - fw / 2 + fw * 0.12, dt = g.Height / 2 - fh / 2 - fh * 0.095;
                    g.Children.Add(new Image { Source = dragon, Stretch = Stretch.Fill, Width = dw, Height = dh, IsHitTestVisible = false,
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(dl, dt, g.Width - dl - dw, g.Height - dt - dh),
                        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 0.7 } });
                }
                else
                    g.Children.Add(new Path
                    {
                        Data = Geometry.Parse("M 0,10 Q 8,0 16,6 L 20,0 L 24,6 Q 32,0 40,10 L 34,12 L 20,8 L 6,12 Z"), Fill = Theme.Gold, Stroke = Theme.B(Theme.Rgb(0x6B, 0x4A, 0x14)), StrokeThickness = 1,
                        Width = 38, Height = 11, Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -3, 0, 0),
                    });
            }
            var shieldArt = GameArt.Get("divine-shield");
            if (m.DivineShield)
                g.Children.Add(shieldArt != null
                    // The bubble fills 351x467 of its 512x512 picture (centred): size it so the bubble sits just outside the frame's
                    // outer edge (400x496 of the frame art), centred on the frame.
                    ? Overlay(new Image { Source = shieldArt, Stretch = Stretch.Fill, Opacity = 0.9, IsHitTestVisible = false }, 400 * k * 1.06 / 351 * 512, 496 * k * 1.06 / 467 * 512, g)
                    : new Ellipse { Width = fw + 4, Height = fh + 4, Stroke = Theme.B(Theme.Rgb(0xFF, 0xE0, 0x7A)), StrokeThickness = 3, Fill = Theme.B(Theme.Rgb(0xFF, 0xE0, 0x7A), 0.22) });
            var frostArt = GameArt.Get("frozen");
            if (m.Frozen)
                g.Children.Add(frostArt != null
                    // The frost ring fills 476x511 of its picture: fit it to the frame's outer edge.
                    ? Overlay(new Image { Source = frostArt, Stretch = Stretch.Fill, Opacity = 0.9, IsHitTestVisible = false }, 400 * k * 1.04 / 476 * 512, 496 * k * 1.04 / 511 * 512, g)
                    : new Ellipse { Width = pw, Height = ph, Fill = Theme.B(Theme.Rgb(0x8F, 0xD8, 0xFF), 0.38) });

            var atkFill = info?.Attack != null && m.Attack > info.Attack ? Theme.B(Theme.Rgb(0x8C, 0xFF, 0x8C)) : Brushes.White;
            var hpFill = m.Health < m.MaxHealth ? Theme.B(Theme.Rgb(0xFF, 0x8A, 0x8A)) : info?.Health != null && m.MaxHealth > info.Health ? Theme.B(Theme.Rgb(0x8C, 0xFF, 0x8C)) : Brushes.White;
            g.Children.Add(StatGem(true, m.Attack.ToString(), 29, atkFill, HorizontalAlignment.Left));
            g.Children.Add(StatGem(false, m.Health.ToString(), 31, hpFill, HorizontalAlignment.Right));
            var icon = KeywordIcon(m);
            if (icon != null) g.Children.Add(icon);
            return g;
        }


        /// <summary>
        /// An effect drawn over a minion, centred on it at an exact size even when larger than the minion's box: the
        /// negative margins give it room, so it isn't clipped to the box (which is what made the bubbles look off).
        /// </summary>
        private static UIElement Overlay(FrameworkElement el, double w, double h, FrameworkElement box)
        {
            el.Width = w; el.Height = h;
            el.HorizontalAlignment = HorizontalAlignment.Center; el.VerticalAlignment = VerticalAlignment.Center;
            double dx = Math.Max(0, (w - box.Width) / 2), dy = Math.Max(0, (h - box.Height) / 2);
            el.Margin = new Thickness(-dx, -dy, -dx, -dy);
            return el;
        }


        /// <summary>One keyword icon for the bottom of a minion, as the game shows (deathrattle, trigger, ...).</summary>
        private static FrameworkElement KeywordIcon(Replay.Minion m)
        {
            var icon = m.Deathrattle ? ("☠", "Deathrattle") : m.Trigger ? ("⚡", "Triggers on an event") : m.Poisonous ? ("☣", "Poisonous") : m.Lifesteal ? ("♥", "Lifesteal")
                : m.Reborn ? ("✚", "Reborn") : m.Windfury ? ("≋", "Windfury") : (null, null);
            if (icon.Item1 == null) return null;
            // The game's own icon (skull, bolt, poison bottle, heart); the game has none for reborn and windfury.
            var art = GameArt.Get(m.Deathrattle ? "deathrattle" : m.Trigger ? "trigger" : m.Poisonous ? "poisonous" : m.Lifesteal ? "lifesteal" : "-");
            if (art != null)
                return new Image
                {
                    Source = art, Height = 20, Width = 20 * art.PixelWidth / art.PixelHeight, Stretch = Stretch.Fill, ToolTip = icon.Item2,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1),
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8 },
                };
            {
                var b = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Theme.B(Theme.Rgb(0x22, 0x1A, 0x14)), BorderBrush = Theme.B(Trim), BorderThickness = new Thickness(1.3),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 0), ToolTip = icon.Item2,
                    Child = new TextBlock { Text = icon.Item1, FontFamily = Symbols, FontSize = 10, Foreground = Theme.B(Theme.Rgb(0xF0, 0xE0, 0xC0)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                return b;
            }
        }
    }
}
