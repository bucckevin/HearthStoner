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

        private static FrameworkElement Hero(Replay.Side s, bool active, AppData d, bool you)
        {
            var frameArt = GameArt.Get("hero-frame");
            if (frameArt != null) return HeroInFrame(s, active, d, frameArt, you);
            const double w = 98, h = 116;
            var g = new Grid { Width = w + 16, Height = h + 10, ToolTip = d.Cards.NameOf(s.HeroCardId) };
            var arch = Geometry.Parse($"M 0,{h} L 0,{w / 2} A {w / 2},{w / 2} 0 0 1 {w},{w / 2} L {w},{h} Z");
            var art = ArtCache.Get(s.HeroCardId);
            if (art == null && ArtCache.HasFailed(s.HeroCardId)) art = ArtCache.Get(ReplayPage.BaseHero(s.HeroCardId));
            var canAttack = active && s.HeroAttack > 0 && !s.HeroExhausted;
            var portrait = new Path
            {
                Data = arch, Width = w, Height = h, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Fill = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.18, 0.04, 0.64, 0.66) } : Theme.B(Stone),
                Stroke = Theme.B(active ? Theme.GoldC : Stone), StrokeThickness = 5,
                Effect = canAttack ? new DropShadowEffect { Color = Ready, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 }
                    : active ? new DropShadowEffect { Color = Theme.GoldC, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 }
                    : new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 3, Opacity = 0.7 },
            };
            g.Children.Add(portrait);
            g.Children.Add(new Path { Data = arch, Width = w - 8, Height = h - 8, Stretch = Stretch.Fill, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Stroke = Theme.B(Trim, 0.8), StrokeThickness = 1.2 });

            var health = Gem(s.Health.ToString(), 32, Theme.Rgb(0xC6, 0x2B, 0x2B), s.Health < 10 ? Theme.B(Theme.Rgb(0xFF, 0xC0, 0xC0)) : Brushes.White, HorizontalAlignment.Right, drop: true);
            g.Children.Add(health);
            if (s.Armor > 0)
            {
                var armor = Gem(s.Armor.ToString(), 26, Theme.Rgb(0x8E, 0x98, 0xA8), Brushes.White, HorizontalAlignment.Right, shield: true);
                armor.Margin = new Thickness(0, 0, 0, 30);
                g.Children.Add(armor);
            }
            if (s.HeroAttack > 0) g.Children.Add(Gem(s.HeroAttack.ToString(), 30, Theme.Rgb(0xF2, 0xC2, 0x3A), Brushes.White, HorizontalAlignment.Left));
            return g;
        }


        /// <summary>The hero portrait in the game's arched stone frame (its opening measured from the art).</summary>
        private static FrameworkElement HeroInFrame(Replay.Side s, bool active, AppData d, ImageSource frameArt, bool you)
        {
            // The frame art is 396x457, its opening 51..340 x 66..403. Each hero's frame has the size the game shows it at.
            double fw = you ? YourHeroW : TheirHeroW, fh = you ? YourHeroH : TheirHeroH;
            double kx = fw / 396, ky = fh / 457;
            double pw = (340 - 51) * kx + 3, ph = (403 - 66) * ky + 3;
            var g = new Grid { Width = fw + 14, Height = fh + 8, ToolTip = d.Cards.NameOf(s.HeroCardId) };
            var art = ArtCache.Get(s.HeroCardId);
            if (art == null && ArtCache.HasFailed(s.HeroCardId)) art = ArtCache.Get(ReplayPage.BaseHero(s.HeroCardId));
            var arch = Geometry.Parse($"M 0,{ph} L 0,{pw / 2} A {pw / 2},{pw / 2} 0 0 1 {pw},{pw / 2} L {pw},{ph} Z");
            g.Children.Add(new Path
            {
                Data = arch, Width = pw, Height = ph, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4 + 66 * ky - 1.5, 0, 0),
                Fill = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.18, 0.04, 0.64, 0.66) } : Theme.B(Stone),
            });
            var canAttack = active && s.HeroAttack > 0 && !s.HeroExhausted;
            g.Children.Add(new Image
            {
                Source = frameArt, Width = fw, Height = fh, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0),
                Effect = canAttack ? new DropShadowEffect { Color = Ready, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 }
                    : active ? new DropShadowEffect { Color = Theme.GoldC, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.9 }
                    : new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 3, Opacity = 0.8 },
            });
            g.Children.Add(StatGem(false, s.Health.ToString(), 34, s.Health < 10 ? Theme.B(Theme.Rgb(0xFF, 0xC0, 0xC0)) : Brushes.White, HorizontalAlignment.Right));
            if (s.Armor > 0)
            {
                var armorArt = GameArt.Get("armor");
                FrameworkElement armor;
                if (armorArt != null)
                {
                    var ag = new Grid { Width = 24, Height = 29, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
                    ag.Children.Add(new Image { Source = armorArt, Stretch = Stretch.Uniform });
                    var n = Outlined(s.Armor.ToString(), 15, Brushes.White);
                    n.HorizontalAlignment = HorizontalAlignment.Center; n.VerticalAlignment = VerticalAlignment.Center; n.Margin = new Thickness(0, 0, 0, 3);
                    ag.Children.Add(n);
                    armor = ag;
                }
                else armor = Gem(s.Armor.ToString(), 22, Theme.Rgb(0x8E, 0x98, 0xA8), Brushes.White, HorizontalAlignment.Right, shield: true);
                armor.Margin = new Thickness(0, 0, 0, 30);
                g.Children.Add(armor);
            }
            if (s.HeroAttack > 0) g.Children.Add(StatGem(true, s.HeroAttack.ToString(), 30, Brushes.White, HorizontalAlignment.Left));
            return g;
        }


        /// <summary>
        /// A stat in the game's own gem, number on top: attack in the gold gem with its sword, health in the blood
        /// drop. Falls back to a drawn gem when the art isn't there.
        /// </summary>
        private static FrameworkElement StatGem(bool attack, string text, double h, Brush fill, HorizontalAlignment ha)
        {
            var art = GameArt.Get(attack ? "attack" : "health");
            if (art == null) return Gem(text, h, attack ? Theme.Rgb(0xF2, 0xC2, 0x3A) : Theme.Rgb(0xC6, 0x2B, 0x2B), fill, ha, drop: !attack);
            var w = h * art.PixelWidth / art.PixelHeight;
            // Where the number sits (measured on the art): the middle of the attack orb, the round part of the drop.
            var (fx, fy) = attack ? (0.615, 0.513) : (0.49, 0.70);
            var g = new Canvas { Width = w, Height = h, HorizontalAlignment = ha, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            g.Children.Add(new Image { Source = art, Width = w, Height = h, Stretch = Stretch.Fill, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1.5, Opacity = 0.7 } });
            var num = Outlined(text, h * (attack ? 0.6 : 0.52), fill);
            g.Children.Add(At(num, fx * w - num.Width / 2, fy * h - num.Height / 2));
            return g;
        }


        /// <summary>
        /// The hero power in the game's round frame, its cost on a mana gem above; once used this turn it shows the
        /// game's face-down coin, as in the game.
        /// </summary>
        private static FrameworkElement HeroPower(Replay.Side s, AppData d)
        {
            var frame = GameArt.Get("heropower-frame");
            var used = s.HeroPowerUsed ? GameArt.Get("heropower-used") : null;
            var tip = d.Cards.NameOf(s.HeroPowerCardId) + (s.HeroPowerUsed ? " (used this turn)" : "");
            if (frame == null)
            {
                var hp = Round(s.HeroPowerCardId, 46, Trim, d);
                if (s.HeroPowerUsed) hp.Opacity = 0.45;
                hp.ToolTip = tip;
                return hp;
            }
            const double fw = 56, fh = 56 * 238 / 244.0;              // the frame art is 244x238, its hole 40..194 x 56..206
            var g = new Grid { Width = fw, Height = fh + 8, ToolTip = tip };
            if (used != null)
                g.Children.Add(new Image { Source = used, Width = fw * 0.86, Height = fw * 0.86 * 202 / 214, Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2),
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.8 } });
            else
            {
                var art = ArtCache.Get(s.HeroPowerCardId);
                double hw = 154 * fw / 244, hh = 150 * fh / 238;
                g.Children.Add(new Ellipse
                {
                    Width = hw, Height = hh, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(40 * fw / 244, 8 + 56 * fh / 238, 0, 0),
                    Fill = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.2, 0.1, 0.6, 0.6) } : Theme.B(Stone),
                });
                g.Children.Add(new Image { Source = frame, Width = fw, Height = fh, Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Bottom,
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.8 } });
                var cost = d.Cards.ById(s.HeroPowerCardId)?.Cost;
                if (cost != null)
                {
                    var gem = ManaGem(cost.ToString(), 20);
                    gem.HorizontalAlignment = HorizontalAlignment.Center; gem.VerticalAlignment = VerticalAlignment.Top;
                    g.Children.Add(gem);
                }
            }
            return g;
        }


        /// <summary>The blue mana gem with a number, as on cards and hero powers.</summary>
        private static FrameworkElement ManaGem(string text, double size)
        {
            var g = new Grid { Width = size, Height = size * 1.08 };
            g.Children.Add(new Path
            {
                Data = Geometry.Parse("M 6,0 L 12,3.25 L 12,9.75 L 6,13 L 0,9.75 L 0,3.25 Z"), Stretch = Stretch.Fill,
                Fill = new RadialGradientBrush(Theme.Rgb(0x9A, 0xD8, 0xFF), Theme.Rgb(0x1E, 0x5C, 0xD0)) { GradientOrigin = new Point(0.35, 0.3) },
                Stroke = Theme.B(Theme.Rgb(0x0C, 0x1C, 0x40)), StrokeThickness = 1.2,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8 },
            });
            var n = Outlined(text, size * 0.62, Brushes.White);
            n.HorizontalAlignment = HorizontalAlignment.Center; n.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(n);
            return g;
        }


        // ------------------------------------------------------------------ small parts

        /// <summary>A round gem with an outlined number: attack (gold), health (red drop), armor (grey shield).</summary>
        private static FrameworkElement Gem(string text, double size, Color color, Brush numberFill, HorizontalAlignment ha, bool drop = false, bool shield = false)
        {
            var g = new Grid { Width = size, Height = size, HorizontalAlignment = ha, VerticalAlignment = VerticalAlignment.Bottom };
            Geometry shape = drop ? Geometry.Parse("M 50,0 C 62,22 96,46 96,66 A 46,46 0 0 1 4,66 C 4,46 38,22 50,0 Z")
                : shield ? Geometry.Parse("M 50,0 L 96,12 L 92,58 Q 82,90 50,100 Q 18,90 8,58 L 4,12 Z")
                : Geometry.Parse("M 50,2 A 48,48 0 1 1 49.9,2 Z");
            var light = Color.FromRgb((byte)Math.Min(255, color.R + 70), (byte)Math.Min(255, color.G + 70), (byte)Math.Min(255, color.B + 70));
            g.Children.Add(new Path
            {
                Data = shape, Stretch = Stretch.Fill, Width = size, Height = size,
                Fill = new RadialGradientBrush(light, color) { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.4), RadiusX = 0.7, RadiusY = 0.7 },
                Stroke = Theme.B(Theme.Rgb(0x1A, 0x10, 0x0A)), StrokeThickness = 1.5,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1.5, Opacity = 0.7 },
            });
            var num = Outlined(text, size * 0.62, numberFill);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.VerticalAlignment = VerticalAlignment.Center;
            if (drop) num.Margin = new Thickness(0, size * 0.18, 0, 0);
            g.Children.Add(num);
            return g;
        }
    }
}
