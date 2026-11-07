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
    /// <summary>
    /// The replay's board, drawn to read like Hearthstone's: a table with the two heroes in arched frames, minions in
    /// ovals with attack and health gems and keyword icons, fanned hands, mana crystals, hero powers, weapons, a play
    /// history down the left and the turn button on the right. Stepping forward one action animates it: attacks
    /// lunge and return, damage and healing pop up, new minions grow in, dead ones fade, the others slide over, and
    /// a played card shows large for a moment. Card pictures come from HearthstoneJSON; the table, frames, gems and
    /// icons come from your own Hearthstone install (see GameArt), with drawn stand-ins for any piece that's missing.
    /// </summary>
    public static class ReplayBoard
    {
        // The game's 4:3 view, measured from a 1916x1440 screenshot of a real game and scaled by 0.4175. Boards rendered
        // with the game's camera fill it exactly; the fallback table art (1024x512) is drawn 1.13x from (-178, -3) to match.
        // Heroes, minion rows, hands and mana sit at the game's spots and sizes.
        public const double W = 800, H = 600, CX = 400;
        private const double TableX = -178, TableY = -3, TableK = 1.13;
        private const double TheirHeroY = 110, YourHeroY = 456, TheirRowY = 226, YourRowY = 329;   // from the game's board layout
        /// <summary>
        /// The heroes' frames as a real game shows them over the board's arches (measured side by side with a screenshot):
        /// theirs 102x112 around (400, 108); yours 94x106 around (400, 454), hanging over the arch's lower trim.
        /// </summary>
        private const double TheirHeroW = 94, TheirHeroH = 107, YourHeroW = 94, YourHeroH = 108;
        private const double Spacing = 77, MinionW = 62, MinionH = 82;
        /// <summary>Scale of the minion frame art (410x504).</summary>
        private const double MinionK = 0.16;
        /// <summary>
        /// The board rendered from the game's own 3D model with the game's camera (Art/GameArtBuilder): it covers
        /// the whole 4:3 canvas exactly. Null when none has been rendered (then the table picture is used).
        /// </summary>
        private static BitmapSource _board;

        /// <summary>False: no animations (screenshots of whole pages).</summary>
        public static bool Animate = true;
        /// <summary>Draws the animation frozen at this point (0..1) instead of running it; for screenshots.</summary>
        public static double? FixedProgress;

        private static readonly Color Wood1 = Theme.Rgb(0x4A, 0x31, 0x20), Wood2 = Theme.Rgb(0x26, 0x18, 0x10);
        private static readonly Color Sand1 = Theme.Rgb(0x8E, 0x76, 0x52), Sand2 = Theme.Rgb(0x55, 0x43, 0x2C);
        private static readonly Color Stone = Theme.Rgb(0x6E, 0x60, 0x4C), Trim = Theme.Rgb(0xC9, 0xA2, 0x5E);
        private static readonly Color YouC = Theme.Rgb(0x5A, 0xA8, 0xFF), ThemC = Theme.Rgb(0xE0, 0x55, 0x55);
        private static readonly Color Ready = Theme.Rgb(0x5C, 0xFF, 0x7A);
        private static readonly FontFamily Symbols = new FontFamily("Segoe UI Symbol");

        public static FrameworkElement Build(Replay r, int index, Replay.Frame prev, bool showHidden, AppData d)
        {
            var f = r.Frames[index];
            // The board you picked in Settings, else any rendered board, else the table picture.
            _board = GameArt.Board(d.Services?.Settings?.ReplayBoard) ?? GameArt.Boards().Select(b => GameArt.Board(b)).FirstOrDefault(b => b != null);
            var c = new Canvas { Width = W, Height = H, ClipToBounds = true };
            Table(c, f);
            History(c, r, index, d);

            var anims = new List<Action<double>>();
            var elements = new Dictionary<int, FrameworkElement>();   // entity -> its element (minions and heroes)
            var centers = new Dictionary<int, Point>();
            var prevCenters = new Dictionary<int, Point>();
            if (prev != null)
            {
                Place(prev.Them, TheirHeroY, TheirRowY, prevCenters);
                Place(prev.You, YourHeroY, YourRowY, prevCenters);
            }
            Place(f.Them, TheirHeroY, TheirRowY, centers);
            Place(f.You, YourHeroY, YourRowY, centers);

            Side(c, f, f.Them, false, d, elements, centers);
            Side(c, f, f.You, true, d, elements, centers);
            OpponentHand(c, f, r, showHidden, d);
            YourHand(c, f, d);
            TurnButton(c, f);

            if (prev != null) Animations(c, r, f, prev, d, elements, centers, prevCenters, anims);
            Run(c, anims, f.PlayedCardId != null ? 900 : 650);

            return new Viewbox { Child = c, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
        }

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

        // ------------------------------------------------------------------ a side: hero, hero power, weapon, minions, mana

        private static void Side(Canvas c, Replay.Frame f, Replay.Side s, bool you, AppData d, Dictionary<int, FrameworkElement> elements, Dictionary<int, Point> centers)
        {
            var active = !f.Mulligan && f.YourTurn == you;
            var heroY = you ? YourHeroY : TheirHeroY;
            var rowY = you ? YourRowY : TheirRowY;

            foreach (var m in s.Board)
            {
                var el = Minion(m, active, d);
                var p = centers[m.EntityId];
                c.Children.Add(At(el, p.X - el.Width / 2, p.Y - el.Height / 2));
                elements[m.EntityId] = el;
            }

            var hero = Hero(s, active, d, you);
            c.Children.Add(At(hero, CX - hero.Width / 2, heroY - hero.Height / 2));
            if (s.HeroEntityId != 0) elements[s.HeroEntityId] = hero;

            // Hero power to the right, weapon to the left (the game's spots: about 94 either side of the hero).
            if (s.HeroPowerCardId != null)
            {
                var hp = HeroPower(s, d);
                c.Children.Add(At(hp, CX + 94 - hp.Width / 2, heroY + 2 - hp.Height / 2));
            }
            if (s.WeaponCardId != null)
            {
                var wpn = new Grid { Width = 56, Height = 56, ToolTip = $"{d.Cards.NameOf(s.WeaponCardId)} {s.WeaponAttack}/{s.WeaponDurability}" };
                wpn.Children.Add(Round(s.WeaponCardId, 48, Theme.Rgb(0x9A, 0xA4, 0xB8), d));
                wpn.Children.Add(StatGem(true, s.WeaponAttack.ToString(), 24, Brushes.White, HorizontalAlignment.Left));
                wpn.Children.Add(Gem(s.WeaponDurability.ToString(), 20, Theme.Rgb(0x8A, 0x8F, 0x9A), Brushes.White, HorizontalAlignment.Right, shield: true));
                c.Children.Add(At(wpn, (you ? 293 : 304) - 28, heroY + (you ? 4 : 2) - 28));   // the game's weapon spots
            }

            // Name and deck: right of the hero power.
            var info = new StackPanel { Width = 130 };
            info.Children.Add(Shadowed(Text(s.Name ?? (you ? "You" : "Opponent"), 13, Brushes.White, FontWeights.SemiBold)));
            info.Children.Add(Shadowed(Text($"{s.DeckCount} card{(s.DeckCount == 1 ? "" : "s")} in deck", 10.5, Theme.B(Theme.Rgb(0xE6, 0xD8, 0xC0)))));
            c.Children.Add(At(info, CX + 130, heroY - 16));
            Mana(c, s, you);

            // Secrets, auras and the like: in an arc over the portrait, as the game shows them. Secrets are the game's
            // gem in their class colour, other lasting effects (auras) the game's yellow bolt in a stone ring.
            var n = s.Secrets.Count;
            if (n == 0) return;
            // On the top of the portrait's arch, as the game draws them, so they stay clear of the minion row.
            // The game's five secret slots around the portrait (centre, then the pairs either side, each lower).
            const double size = 30;
            var slots = you ? new[] { (0.0, -53.0), (-29.0, -35.0), (30.0, -35.0), (-48.0, -3.0), (49.0, -3.0) }
                            : new[] { (0.0, -48.0), (-29.0, -30.0), (29.0, -30.0), (-48.0, 1.0), (48.0, 1.0) };
            var first = n % 2 == 1 ? 0 : 1;
            for (var i = 0; i < n; i++)
            {
                var id = s.Secrets[i];
                var (sx, sy) = slots[Math.Min(first + i, slots.Length - 1)];
                var x = CX + sx;
                var y = heroY + sy;
                FrameworkElement icon;
                var art = string.IsNullOrEmpty(id) ? null : ArtCache.Get(id);
                var card = string.IsNullOrEmpty(id) ? null : d.Cards.ById(id);
                var gemArt = string.IsNullOrEmpty(id) || card?.Has("SECRET") == true
                    ? GameArt.Get("secret-" + ((card?.CardClass ?? d.Cards.ById(s.HeroCardId)?.CardClass) switch { "HUNTER" => "hunter", "PALADIN" => "paladin", "ROGUE" => "rogue", _ => "mage" }))
                    : GameArt.Get("aura");
                if (gemArt != null)
                    icon = new Image { Source = gemArt, Width = size, Height = size, Stretch = Stretch.Fill, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 0.8 } };
                // A card you can see keeps its gold ring even before its picture has downloaded (it's not a hidden secret).
                else if (!string.IsNullOrEmpty(id))
                    icon = new Ellipse
                    {
                        Width = size, Height = size, Stroke = Theme.Gold, StrokeThickness = 2.5,
                        Fill = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.2, 0.08, 0.6, 0.6) } : Theme.B(Theme.Rgb(0x4A, 0x3A, 0x22)),
                        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.8 },
                    };
                else
                {
                    var gem = new Grid { Width = size, Height = size };
                    gem.Children.Add(new Path
                    {
                        Data = Geometry.Parse("M 9,0 L 18,5 L 18,15 L 9,20 L 0,15 L 0,5 Z"), Stretch = Stretch.Fill, Width = size * 0.8, Height = size * 0.9,
                        Fill = new RadialGradientBrush(Theme.Rgb(0xD8, 0xA0, 0xFF), Theme.Rgb(0x7A, 0x30, 0xC8)), Stroke = Theme.B(Trim), StrokeThickness = 1.5,
                        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.8 },
                    });
                    var q = Outlined("?", 14, Brushes.White);
                    q.HorizontalAlignment = HorizontalAlignment.Center; q.VerticalAlignment = VerticalAlignment.Center;
                    gem.Children.Add(q);
                    icon = gem;
                }
                icon.ToolTip = string.IsNullOrEmpty(id) ? "A secret" : d.Cards.NameOf(id);
                c.Children.Add(At(icon, x - size / 2, y - size / 2));
            }
        }

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

    /// <summary>
    /// The board art pulled from your own Hearthstone install by Art/GameArtBuilder, in
    /// %LocalAppData%\HearthStoner\gameart\board. Each piece is optional: the replay board draws its own when one is missing.
    /// </summary>
    public static class GameArt
    {
        private static readonly Dictionary<string, BitmapSource> Cache = new Dictionary<string, BitmapSource>();

        public static string Dir => AppPaths.Data("gameart", "board");
        /// <summary>Whole boards rendered from the game's 3D models by Art/GameArtBuilder: &lt;root&gt;.jpg, 4:3.</summary>
        public static string BoardsDir => AppPaths.Data("gameart", "boards");

        public static BitmapSource Get(string name) => Load(System.IO.Path.Combine(Dir, name + ".png"), 0);

        /// <summary>Makes the art in the background if it's missing or outdated (<paramref name="force"/>: always); see ArtJob.</summary>
        public static void StartJob(string installDir, Func<bool> inGame = null, bool force = false)
        {
            if (ArtJob.Build == null)
            {
                ArtJob.Build = GameArtBuilder.Run;
                ArtJob.Version = GameArtBuilder.GameVersion;
                ArtJob.Finished += () => { lock (Cache) Cache.Clear(); };   // new art replaces the loaded images
            }
            if (inGame != null) ArtJob.InGame = inGame;
            ArtJob.Start(installDir, force);
        }

        /// <summary>A rendered board (null if it hasn't been rendered); a small copy when <paramref name="width"/> is set.</summary>
        public static BitmapSource Board(string root, int width = 0) =>
            string.IsNullOrEmpty(root) ? null : Load(System.IO.Path.Combine(BoardsDir, root + ".jpg"), width);

        /// <summary>The boards that have been rendered, by root name.</summary>
        public static List<string> Boards() =>
            System.IO.Directory.Exists(BoardsDir)
                ? System.IO.Directory.GetFiles(BoardsDir, "*.jpg").Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).OrderBy(BoardName).ToList()
                : new List<string>();

        /// <summary>A readable name for a board's root (the set it came with), else the root itself.</summary>
        public static string BoardName(string root) => Names.TryGetValue(root ?? "", out var n) ? n : root;
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STW"] = "Stormwind", ["STWOG"] = "Stormwind (Old Gods)", ["STW2"] = "Stormwind 2", ["ORG"] = "Orgrimmar", ["MOP"] = "Pandaria",
            ["STR"] = "Stranglethorn", ["ICC"] = "Icecrown", ["BAR"] = "Barrens", ["BRM"] = "Blackrock Mountain", ["GVG"] = "Goblins vs Gnomes",
            ["NAX"] = "Naxxramas", ["TGT"] = "Grand Tournament", ["LOE1"] = "League of Explorers", ["LOE2"] = "League of Explorers 2",
            ["KAR"] = "Karazhan", ["MSG"] = "Gadgetzan", ["UNG"] = "Un'Goro", ["LOOT"] = "Kobolds & Catacombs", ["GIL"] = "Gilneas",
            ["BOT"] = "Boomsday", ["TRL"] = "Rastakhan's Rumble", ["DAL"] = "Dalaran", ["ULD"] = "Uldum", ["ULD2"] = "Uldum 2", ["ULD3"] = "Uldum 3",
            ["DRG"] = "Dragons", ["BT"] = "Outland", ["SCH"] = "Scholomance", ["DMF"] = "Darkmoon Faire", ["BOH"] = "Book of Heroes",
            ["TTN"] = "Titans", ["MOTLK"] = "Lich King", ["VSC"] = "Sunken City", ["BotB"] = "Festival of Legends", ["WW"] = "Badlands",
            ["TOY"] = "Whizbang's Workshop", ["CATA"] = "Cataclysm", ["EDR"] = "Emerald Dream", ["TIME"] = "Timeways", ["TLC"] = "Lost City",
            ["ALT"] = "Alterac Valley", ["RAG"] = "Firelands", ["REV"] = "Revendreth",
        };

        private static BitmapSource Load(string file, int width)
        {
            var key = file + "|" + width;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var b)) return b;
                BitmapSource img = null;
                try
                {
                    if (System.IO.File.Exists(file))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(file);
                        if (width > 0) bi.DecodePixelWidth = width;
                        bi.EndInit();
                        bi.Freeze();
                        img = bi;
                    }
                }
                catch (Exception swallowed) { Diag.Ignored(swallowed, "ReplayBoard"); }
                Cache[key] = img;
                return img;
            }
        }
    }
}
