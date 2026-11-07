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
    public static partial class ReplayBoard
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
    }
}
