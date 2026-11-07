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

namespace HearthStoner.Pages
{
    public static partial class ReplayBoard
    {

        // ------------------------------------------------------------------ the table

        private static void Table(Canvas c, Replay.Frame f)
        {
            // The whole table (frame, corners, hero slots, trays) when you've put its picture in the game-art folder.
            var table = GameArt.Get("table");
            if (_board != null || table != null)
            {
                c.Children.Add(At(new Rectangle { Width = W, Height = H, Fill = Theme.B(Theme.Rgb(0x2A, 0x1A, 0x1E)) }, 0, 0));
                if (_board != null) c.Children.Add(At(new Image { Source = _board, Width = W, Height = H, Stretch = Stretch.Fill }, 0, 0));
                else c.Children.Add(At(new Image { Source = table, Width = 1024 * TableK, Height = 512 * TableK, Stretch = Stretch.Fill }, TableX, TableY));
                // The game's lighting: a warm glow over the middle of the field, darker towards the edges.
                c.Children.Add(At(new Rectangle
                {
                    Width = W, Height = H, IsHitTestVisible = false,
                    Fill = new RadialGradientBrush
                    {
                        GradientOrigin = new Point(0.5, 0.45), Center = new Point(0.5, 0.45), RadiusX = 0.62, RadiusY = 0.66,
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(46, 255, 226, 170), 0), new GradientStop(Color.FromArgb(0, 255, 226, 170), 0.45),
                            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.62), new GradientStop(Color.FromArgb(130, 0, 0, 0), 1),
                        },
                    },
                }, 0, 0));
                return;
            }
            var mid = (TheirRowY + YourRowY) / 2;
            c.Children.Add(At(new Rectangle
            {
                Width = W, Height = H, RadiusX = 18, RadiusY = 18, Stroke = Theme.B(Trim, 0.55), StrokeThickness = 2,
                Fill = new LinearGradientBrush(Wood1, Wood2, 90),
            }, 0, 0));
            // Else the game's playing field (from Art/GameArtBuilder), else a drawn one.
            var boardArt = GameArt.Get("board");
            if (boardArt != null)
            {
                c.Children.Add(At(new Image
                {
                    Source = boardArt, Width = 660, Height = 330, Stretch = Stretch.Fill,
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.8 },
                }, CX - 330, mid - 165));
                return;
            }
            c.Children.Add(At(new Rectangle
            {
                Width = 600, Height = 250, RadiusX = 26, RadiusY = 26, Stroke = Theme.B(Trim, 0.6), StrokeThickness = 1.5,
                Fill = new RadialGradientBrush(Sand1, Sand2) { RadiusX = 0.65, RadiusY = 0.8 },
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 },
            }, CX - 300, mid - 125));
            foreach (var y in new[] { TheirHeroY, YourHeroY })
                c.Children.Add(At(new Ellipse
                {
                    Width = 300, Height = 92, Fill = new RadialGradientBrush(Color.FromArgb(140, 0x8E, 0x76, 0x52), Color.FromArgb(0, 0x55, 0x43, 0x2C)),
                }, CX - 150, y - 26));
            c.Children.Add(At(new Rectangle
            {
                Width = 560, Height = 2,
                Fill = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Color.FromArgb(0, 0xC9, 0xA2, 0x5E), 0), new GradientStop(Color.FromArgb(150, 0xC9, 0xA2, 0x5E), 0.5), new GradientStop(Color.FromArgb(0, 0xC9, 0xA2, 0x5E), 1) }, 0),
            }, CX - 280, mid - 1));
        }


        /// <summary>
        /// The play history down the table's left tray, newest on top: a tile of the card's art for each card played
        /// (with the game's lightning bolt) and each attack (with its sword), edged blue for you and red for them.
        /// </summary>
        private static void History(Canvas c, Replay r, int index, AppData d)
        {
            // In the board's tray: the rendered board's tray is 22..58 x 160..410, the table picture's a little further out.
            // The game's column: the first tile centred at (46, 188), each 33.6 lower and drifting left to x 38, six fit.
            const double tw = 30, th = 30, step = 33.6;
            double x0 = _board != null ? 31 : 7, y0 = _board != null ? 173 : 146;
            var max = _board != null ? 6 : 8;
            var k = 0;
            for (var i = index; i >= 0 && k < max; i--)
            {
                var fr = r.Frames[i];
                string id; bool you, attack = false;
                if (fr.PlayedCardId != null) { id = fr.PlayedCardId; you = fr.PlayedByYou; }
                else if (fr.AttackerId != 0 && Attacker(r, i, out id, out you)) attack = true;
                else continue;
                var art = ArtCache.Get(id);
                var tile = new Grid
                {
                    Width = tw, Height = th, Opacity = k == 0 ? 1 : 0.9,
                    ToolTip = $"{(you ? "You" : "Opponent")} {(attack ? "attacked with" : "played")} {d.Cards.NameOf(id)}, turn {fr.Round}",
                };
                var artBrush = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.22, 0.12, 0.56, 0.48) } : Theme.B(Stone);
                var frame = GameArt.Get("history-tile");
                if (frame != null)
                {
                    // The game's tile: the art in its middle (64..202 x 52..204 of the 256 frame), the frame over it,
                    // with a faint blue or red glow for whose it was.
                    tile.Children.Add(new Border { Background = artBrush, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(tw * 62 / 256, th * 50 / 256, 0, 0), Width = tw * 142 / 256, Height = th * 156 / 256 });
                    tile.Children.Add(new Image { Source = frame, Stretch = Stretch.Fill,
                        Effect = new DropShadowEffect { Color = you ? YouC : ThemC, BlurRadius = 7, ShadowDepth = 0, Opacity = 0.9 } });
                }
                else
                    tile.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.6), BorderBrush = Theme.B(you ? YouC : ThemC), Background = artBrush,
                        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 0.8 },
                    });
                var icon = GameArt.Get(attack ? "history-attack" : "history-play");
                if (icon != null)
                    tile.Children.Add(new Image { Source = icon, Width = 14, Height = 14, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -4, -3) });
                c.Children.Add(At(tile, x0 - (_board != null ? 1.6 * k : 0), y0 + k++ * step));
            }
        }


        private static void TurnButton(Canvas c, Replay.Frame f)
        {
            var yours = f.YourTurn && !f.Mulligan;
            var text = f.Mulligan ? "MULLIGAN" : yours ? "YOUR TURN" : "ENEMY TURN";
            // The game's button: yellow while you still have something to do, green when you don't, grey on theirs.
            var canAct = yours && (f.You.Board.Any(m => !m.Exhausted && m.Attack > 0 && !m.Frozen && !m.Dormant) || f.You.Hand.Any(h => h.Cost <= f.You.Mana)
                                   || (f.You.HeroAttack > 0 && !f.You.HeroExhausted));
            if (_board != null)
            {
                // The game's own button, rendered from its mesh at its spot on the board (yellow/green 687,252 87x46,
                // grey - its flipped underside - 685,252 86x46), the label in the game's text box on its top face.
                var name = yours ? (canAct ? "endturn-btn-yellow" : "endturn-btn-green") : "endturn-btn-grey";
                var btn = GameArt.Get(name);
                if (btn != null)
                    c.Children.Add(At(new Image
                    {
                        Source = btn, Width = yours ? 87 : 86, Height = 46, Stretch = Stretch.Fill, IsHitTestVisible = false,
                        Effect = yours ? new DropShadowEffect { Color = canAct ? Theme.Rgb(0xFF, 0xD0, 0x40) : Ready, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.75 } : null,
                    }, yours ? 687 : 685, 252));
                else
                    c.Children.Add(new Path
                    {
                        // The top face's outline from the game's mesh.
                        Data = Geometry.Parse("M 693.5,262.6 L 694.9,260.5 705.1,256.8 725,253.6 741,253.6 761.1,256.8 771.5,260.5 772.9,262.6 773.4,285.1 772,287.2 761.8,291 741.8,294.2 725.8,294.2 705.7,291 695.3,287.2 693.9,285.1 Z"),
                        Fill = Theme.B(yours ? (canAct ? Theme.Rgb(0xE8, 0xC0, 0x30) : Theme.Rgb(0x5C, 0xC8, 0x3A)) : Theme.Rgb(0x8A, 0x80, 0x72)),
                    });
                var label = Text(yours ? "YOUR TURN" : f.Mulligan ? "MULLIGAN" : "ENEMY TURN", 11.5, yours ? Theme.B(Theme.Rgb(0x3A, 0x26, 0x08)) : Theme.B(Theme.Rgb(0xF4, 0xEC, 0xDC)), FontWeights.Bold);
                if (!yours) Shadowed(label);
                label.Width = 80; label.TextAlignment = TextAlignment.Center;
                c.Children.Add(At(label, 693, yours ? 265 : 267));
                return;
            }
            if (GameArt.Get("table") != null)
            {
                // The table has its own end-turn slot: a six-sided plate, measured from the picture (691..775 x 252..280
                // here). Light it up in exactly that shape, filled with the game's own button surface when it's there.
                var plate = Geometry.Parse("M 691,266 L 700.6,252.4 L 766.1,252.4 L 774.6,266 L 766.1,279.5 L 700.6,279.5 Z");
                if (yours)
                {
                    var surface = GameArt.Get(canAct ? "endturn-yellow" : "endturn-green");
                    c.Children.Add(new Path
                    {
                        Data = plate,
                        Fill = surface != null ? new ImageBrush(surface) { Stretch = Stretch.Fill, Viewbox = new Rect(0.1, 0.12, 0.8, 0.5) }
                            : canAct ? new LinearGradientBrush(Theme.Rgb(0xFF, 0xE6, 0x5A), Theme.Rgb(0xD8, 0x9A, 0x0E), 90)
                            : new LinearGradientBrush(Theme.Rgb(0x8C, 0xF0, 0x5A), Theme.Rgb(0x2E, 0x9A, 0x1E), 90),
                        Stroke = Theme.B(canAct ? Theme.Rgb(0xFF, 0xF4, 0xB0) : Theme.Rgb(0xC8, 0xFF, 0xB0)), StrokeThickness = 1.2,
                        Effect = new DropShadowEffect { Color = canAct ? Theme.Rgb(0xFF, 0xD0, 0x40) : Ready, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 },
                    });
                }
                var label = Text(yours ? "YOUR TURN" : f.Mulligan ? "MULLIGAN" : "ENEMY TURN", 11, yours ? Theme.B(Theme.Rgb(0x3A, 0x26, 0x08)) : Theme.B(Theme.Rgb(0xEC, 0xE4, 0xD4)), FontWeights.Bold);
                if (!yours) Shadowed(label);
                label.Width = 84; label.TextAlignment = TextAlignment.Center;
                c.Children.Add(At(label, 691, 257));
                return;
            }
            var art = GameArt.Get(yours ? (canAct ? "endturn-yellow" : "endturn-green") : "endturn-grey");
            if (art != null)
            {
                var g = new Grid { Width = 118, Height = 50 };
                g.Children.Add(new Image { Source = art, Stretch = Stretch.Fill, Effect = new DropShadowEffect { Color = yours ? Ready : Colors.Black, BlurRadius = yours ? 12 : 8, ShadowDepth = yours ? 0 : 2, Opacity = 0.7 } });
                var label = Text(text, 13, yours ? Theme.B(Theme.Rgb(0x3A, 0x26, 0x08)) : Brushes.White, FontWeights.Bold);
                if (!yours) Shadowed(label);
                label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 0, 3);
                g.Children.Add(label);
                c.Children.Add(At(g, 733 - 59, 266 - 25));   // the game's end-turn spot
                return;
            }
            var b = new Border
            {
                Width = 96, Height = 32, CornerRadius = new CornerRadius(16), BorderBrush = Theme.B(Trim), BorderThickness = new Thickness(2),
                Background = yours ? new LinearGradientBrush(Theme.Rgb(0x9C, 0xE0, 0x4A), Theme.Rgb(0x4E, 0x8E, 0x22), 90) : new LinearGradientBrush(Theme.Rgb(0x8A, 0x80, 0x72), Theme.Rgb(0x4A, 0x44, 0x3C), 90),
                Effect = new DropShadowEffect { Color = yours ? Ready : Colors.Black, BlurRadius = yours ? 14 : 8, ShadowDepth = yours ? 0 : 2, Opacity = 0.8 },
                Child = Shadowed(Text(text, 12.5, Brushes.White, FontWeights.Bold)),
            };
            ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)b.Child).VerticalAlignment = VerticalAlignment.Center;
            c.Children.Add(At(b, 733 - 48, 266 - 16));
        }
    }
}
