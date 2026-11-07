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

        // ------------------------------------------------------------------ animation

        private static void Animations(Canvas c, Replay r, Replay.Frame f, Replay.Frame prev, AppData d,
            Dictionary<int, FrameworkElement> elements, Dictionary<int, Point> centers, Dictionary<int, Point> prevCenters, List<Action<double>> anims)
        {
            var prevMinions = prev.You.Board.Concat(prev.Them.Board).ToDictionary(m => m.EntityId);
            var curMinions = f.You.Board.Concat(f.Them.Board).ToDictionary(m => m.EntityId);

            // Dead minions: a copy where they were, fading and shrinking.
            foreach (var m in prevMinions.Values.Where(m => !curMinions.ContainsKey(m.EntityId)))
            {
                if (!prevCenters.TryGetValue(m.EntityId, out var at)) continue;
                var ghost = Minion(m, false, d);
                ghost.IsHitTestVisible = false;
                c.Children.Add(At(ghost, at.X - ghost.Width / 2, at.Y - ghost.Height / 2));
                elements[m.EntityId] = ghost;
                var (scale, _, _) = Transforms(ghost);
                anims.Add(t =>
                {
                    var k = Ease(t, 0.45, 0.95);
                    ghost.Opacity = 1 - k;
                    scale.ScaleX = scale.ScaleY = 1 - 0.35 * k;
                });
            }

            // Minions that moved slide over; new ones grow in.
            foreach (var m in curMinions.Values)
            {
                var el = elements[m.EntityId];
                var (scale, move, _) = Transforms(el);
                var now = centers[m.EntityId];
                if (prevCenters.TryGetValue(m.EntityId, out var was))
                {
                    if (Math.Abs(was.X - now.X) < 0.5 && Math.Abs(was.Y - now.Y) < 0.5) continue;
                    var dx = was.X - now.X; var dy = was.Y - now.Y;
                    anims.Add(t => { var k = Ease(t, 0.5, 0.9); move.X = dx * (1 - k); move.Y = dy * (1 - k); });
                }
                else
                    anims.Add(t => { var k = Ease(t, 0.12, 0.6); el.Opacity = k; scale.ScaleX = scale.ScaleY = 0.4 + 0.6 * k; });
            }

            // An attack: the attacker lunges most of the way to its target and comes back.
            if (f.AttackerId != 0 && elements.TryGetValue(f.AttackerId, out var attacker) && (prevCenters.TryGetValue(f.AttackerId, out var from) || centers.TryGetValue(f.AttackerId, out from)))
            {
                var to = prevCenters.TryGetValue(f.TargetId, out var tp) ? tp : centers.TryGetValue(f.TargetId, out tp) ? tp : (Point?)null;
                if (to != null)
                {
                    var (_, _, lunge) = Transforms(attacker);
                    var dx = (to.Value.X - from.X) * 0.82; var dy = (to.Value.Y - from.Y) * 0.82;
                    Panel.SetZIndex(attacker, 50);
                    anims.Add(t =>
                    {
                        var k = t < 0.28 ? Ease(t, 0, 0.28) : 1 - Ease(t, 0.3, 0.55);
                        lunge.X = dx * k; lunge.Y = dy * k;
                    });
                }
            }

            // Damage and healing: a splash on every character whose health changed.
            void Splash(Point at, int delta)
            {
                var heal = delta > 0;
                var g = new Grid { Width = 54, Height = 54, IsHitTestVisible = false };
                g.Children.Add(new Path
                {
                    Data = heal ? Geometry.Parse("M 27,2 A 25,25 0 1 1 26.9,2 Z")
                                : Geometry.Parse("M 27,0 L 33,16 L 52,12 L 40,27 L 54,40 L 35,39 L 30,54 L 22,40 L 4,46 L 14,30 L 0,18 L 19,17 Z"),
                    Fill = heal ? new RadialGradientBrush(Theme.Rgb(0x9C, 0xFF, 0x9C), Theme.Rgb(0x2E, 0xA0, 0x48)) : new RadialGradientBrush(Theme.Rgb(0xFF, 0x6A, 0x4A), Theme.Rgb(0xA0, 0x10, 0x10)),
                    Stroke = Theme.B(Theme.Rgb(0x30, 0x08, 0x08)), StrokeThickness = 1.5,
                });
                var num = Outlined((heal ? "+" : "-") + Math.Abs(delta), 22, Brushes.White);
                num.HorizontalAlignment = HorizontalAlignment.Center; num.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(num);
                c.Children.Add(At(g, at.X - 27, at.Y - 27));
                Panel.SetZIndex(g, 60);
                var (scale, _, _) = Transforms(g);
                anims.Add(t =>
                {
                    var appear = Ease(t, 0.3, 0.42);
                    g.Opacity = t < 0.3 ? 0 : t > 0.88 ? 1 - Ease(t, 0.88, 1) : 1;
                    scale.ScaleX = scale.ScaleY = 0.5 + 0.5 * appear;
                });
            }
            foreach (var m in curMinions.Values)
                if (prevMinions.TryGetValue(m.EntityId, out var pm) && pm.Health != m.Health) Splash(centers[m.EntityId], m.Health - pm.Health);
            foreach (var (now, was) in new[] { (f.You, prev.You), (f.Them, prev.Them) })
            {
                var delta = (now.Health + now.Armor) - (was.Health + was.Armor);
                if (delta != 0 && now.HeroEntityId != 0 && centers.TryGetValue(now.HeroEntityId, out var hp)) Splash(new Point(hp.X, hp.Y - 6), delta);
            }

            // A played card shows large for a moment, at the side of the board like the game.
            if (f.PlayedCardId != null)
            {
                var render = CardTiles.GetRender(f.PlayedCardId);
                FrameworkElement big = render != null
                    ? new Image { Source = render, Width = 160, Height = 222, Stretch = Stretch.Uniform }
                    : new Border { Width = 160, Height = 222, CornerRadius = new CornerRadius(10), Background = Theme.B(Theme.Rgb(0x3A, 0x2C, 0x20)), BorderBrush = Theme.B(Trim), BorderThickness = new Thickness(2),
                        Child = new TextBlock { Text = d.Cards.NameOf(f.PlayedCardId), FontSize = 16, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) } };
                big.IsHitTestVisible = false;
                big.Effect = new DropShadowEffect { Color = f.PlayedByYou ? YouC : ThemC, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9 };
                c.Children.Add(At(big, 75, 143));   // the game's BigCardPosition
                Panel.SetZIndex(big, 80);
                var (scale, move, _) = Transforms(big);
                anims.Add(t =>
                {
                    var appear = Ease(t, 0, 0.14);
                    big.Opacity = t > 0.8 ? 1 - Ease(t, 0.8, 1) : appear;
                    scale.ScaleX = scale.ScaleY = 0.6 + 0.4 * appear;
                    move.Y = (f.PlayedByYou ? 40 : -20) * (1 - appear);
                });
            }
        }


        /// <summary>Scale, slide and lunge transforms on an element (created once).</summary>
        private static (ScaleTransform Scale, TranslateTransform Move, TranslateTransform Lunge) Transforms(FrameworkElement el)
        {
            if (el.RenderTransform is TransformGroup tg && tg.Children.Count == 3 && tg.Children[0] is ScaleTransform s0)
                return (s0, (TranslateTransform)tg.Children[1], (TranslateTransform)tg.Children[2]);
            var s = new ScaleTransform(1, 1); var m = new TranslateTransform(); var l = new TranslateTransform();
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = new TransformGroup { Children = { s, m, l } };
            return (s, m, l);
        }


        private static void Run(Canvas c, List<Action<double>> anims, double ms)
        {
            if (anims.Count == 0) return;
            if (FixedProgress is double fixedT) { foreach (var a in anims) a(fixedT); return; }
            if (!Animate) { foreach (var a in anims) a(1); return; }
            foreach (var a in anims) a(0);
            Stopwatch clock = null;
            EventHandler tick = null;
            tick = (s, e) =>
            {
                clock ??= Stopwatch.StartNew();
                var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
                foreach (var a in anims) a(t);
                if (t >= 1) CompositionTarget.Rendering -= tick;
            };
            c.Loaded += (s, e) => CompositionTarget.Rendering += tick;
            c.Unloaded += (s, e) => CompositionTarget.Rendering -= tick;
        }


        /// <summary>0 before <paramref name="a"/>, 1 after <paramref name="b"/>, eased in between.</summary>
        private static double Ease(double t, double a, double b)
        {
            var x = Math.Max(0, Math.Min(1, (t - a) / (b - a)));
            return x * x * (3 - 2 * x);
        }
    }
}
