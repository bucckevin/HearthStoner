using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Pages
{
    public static partial class PracticePage
    {
        /// <summary>Every turn HearthStoner saw you had lethal, newest games first.</summary>
        public static List<(GameRecord Game, TurnSnapshot Turn)> PuzzleList(AppData d) =>
            d.Games.SelectMany(g => g.Snapshots.Where(t => t.Puzzle != null).Select(t => (g, t))).ToList();

        /// <summary>A lethal puzzle drawn as a board; after the solution you say whether you found it.</summary>
        private static UIElement LethalQuestion(AppShell shell, string id, GameRecord g)
        {
            var st = shell.Practice;
            var t = g.Snapshots.FirstOrDefault(s => s.Turn == PracticeIds.TurnOf(id) && s.Puzzle != null);
            if (t == null) return Skip;
            var z = t.Puzzle;
            var missed = g.Review.Any(r => r.Kind == "MissedLethal" && r.Turn == t.Turn);
            var p = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
            p.Children.Add(Context($"Turn {(t.Turn + 1) / 2} vs {Opponent(g)}, {g.Start:MMM d}. Find {z.TheirHealth + z.TheirArmor} damage." + (missed ? " You missed this one in the game." : "")));
            p.Children.Add(PuzzleBoard(z));
            p.Children.Add(Note("Grey ring: Taunt. Yellow glow: Divine Shield. Green edge: can attack now."));
            var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            if (!st.Answered)
            {
                if (st.Choice < 0) actions.Children.Add(Theme.Button("Show the solution", () => { st.Choice = 0; shell.Redraw(); }));
                else
                {
                    p.Children.Add(ResultBox(true, "Solution:", z.Solution ?? ""));
                    actions.Children.Add(Theme.Button("I found it", () => Answer(shell, id, true)));
                    var no = Theme.Button("I missed it", () => Answer(shell, id, false), primary: false);
                    no.Margin = new Thickness(8, 0, 0, 0);
                    actions.Children.Add(no);
                }
                p.Children.Add(actions);
                return p;
            }
            p.Children.Add(ResultBox(st.WasRight, st.WasRight ? "Found it." : "Missed: it comes back tomorrow.", z.Solution ?? ""));
            p.Children.Add(NextButton(shell));
            return p;
        }

        // ------------------------------------------------------------------ boards

        /// <summary>A minion as the recorder saved it: "Name 3/2 (Taunt, Shield, ready)".</summary>
        private static readonly System.Text.RegularExpressions.Regex MinionText =
            new System.Text.RegularExpressions.Regex(@"^(?<name>.*) (?<atk>-?\d+)/(?<hp>-?\d+)(?: \((?<tags>[^)]*)\))?$");

        /// <summary>
        /// The puzzle as a little board: their hero's health, their minions, a divider, your minions, then your weapon,
        /// hand and mana. Shrinks to fit the column.
        /// </summary>
        private static UIElement PuzzleBoard(LethalPuzzle z)
        {
            var info = new TextBlock { FontSize = 13, Foreground = Theme.Vellum, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            if (z.Weapon != null) info.Inlines.Add(new System.Windows.Documents.Run($"Weapon: {z.Weapon}    "));
            info.Inlines.Add(new System.Windows.Documents.Run($"Hand: {(z.Hand.Count > 0 ? string.Join(", ", z.Hand) : "empty")}    "));
            info.Inlines.Add(new System.Windows.Documents.Run($"{z.Mana} mana") { Foreground = Theme.Mana, FontWeight = FontWeights.SemiBold });
            var label = z.TheirArmor > 0 ? $"Opponent: {z.TheirHealth} health + {z.TheirArmor} armor" : "Opponent health";
            return Felt(z.TheirHealth + z.TheirArmor, label, z.TheirMinions.Select(ParseMinion), z.YourMinions.Select(ParseMinion), info);
        }

        /// <summary>
        /// A board in the app's own colours (the same night blue as every other page, lit a little in the middle):
        /// their hero's health, their minions, a divider, your minions, then a line under it.
        /// </summary>
        private static UIElement Felt(int theirHealth, string healthLabel, IEnumerable<UIElement> theirs, IEnumerable<UIElement> yours, UIElement footer)
        {
            var board = new StackPanel { Width = 640 };
            var hero = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) };
            hero.Children.Add(new Border
            {
                Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Background = Theme.Raised, BorderBrush = Theme.B(Theme.LossC, 0.6), BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = theirHealth.ToString(), FontFamily = Theme.Display, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Theme.Loss, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var hl = Theme.Text(healthLabel, 13, Theme.Mist);
            hl.VerticalAlignment = VerticalAlignment.Center;
            hl.Margin = new Thickness(10, 0, 0, 0);
            hero.Children.Add(hl);
            board.Children.Add(hero);
            board.Children.Add(MinionRow(theirs.ToList(), "Their board is empty"));
            board.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 20, 0, 20) });
            board.Children.Add(MinionRow(yours.ToList(), "Your board is empty"));
            if (footer != null) board.Children.Add(footer);
            return new Border
            {
                Background = new RadialGradientBrush(Theme.Rgb(0x2A, 0x2F, 0x55), Theme.SlateC) { RadiusX = 0.7, RadiusY = 0.7 },
                BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 6),
                Child = new Viewbox { Child = board, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center },
            };
        }

        private static UIElement MinionRow(List<UIElement> minions, string empty)
        {
            if (minions.Count == 0)
            {
                var t = Theme.Text(empty, 13, Theme.Mist);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                return t;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var m in minions) row.Children.Add(m);
            return row;
        }

        private static UIElement ParseMinion(string text)
        {
            var match = MinionText.Match(text ?? "");
            if (!match.Success) return MinionToken(text, "", "", new List<string>(), text);
            var tags = match.Groups["tags"].Value.Split(',').Select(s => s.Trim()).ToList();
            return MinionToken(match.Groups["name"].Value, match.Groups["atk"].Value, match.Groups["hp"].Value, tags, text);
        }

        /// <summary>A minion from a replay frame.</summary>
        private static UIElement ReplayMinion(AppData d, Replay.Minion m, bool yours)
        {
            var tags = new List<string>();
            if (m.Taunt) tags.Add("Taunt");
            if (m.DivineShield) tags.Add("Shield");
            if (m.Windfury) tags.Add("Windfury");
            if (m.Frozen) tags.Add("Frozen");
            if (yours && !m.Exhausted && !m.Frozen && m.Attack > 0) tags.Add("ready");
            var name = d.Cards.NameOf(m.CardId);
            return MinionToken(name, m.Attack.ToString(), m.Health.ToString(), tags, $"{name} {m.Attack}/{m.Health}", m.CardId);
        }

        /// <summary>
        /// One minion: an arch with its card art (or its name when there's no art, as in lethal puzzles), attack
        /// (gold) and health (red) gems; Taunt, Shield and ready marked.
        /// </summary>
        private static UIElement MinionToken(string name, string atk, string hp, List<string> tags, string tip, string cardId = null)
        {
            var art = cardId != null ? ArtCache.Get(cardId) : null;
            var body = new Border
            {
                Width = 74, Height = 90, CornerRadius = new CornerRadius(37, 37, 10, 10), Padding = new Thickness(6, 4, 6, 16),
                Background = art != null
                    ? new ImageBrush(art) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.2, 0.08, 0.6, 0.8) }
                    : Theme.B(tags.Contains("Frozen") ? Theme.Rgb(0x2A, 0x4A, 0x70) : Theme.Rgb(0x2E, 0x35, 0x66)),
                BorderBrush = Theme.B(tags.Contains("ready") ? Theme.WinC : Theme.Rgb(0x6B, 0x5A, 0x3A)), BorderThickness = new Thickness(tags.Contains("ready") ? 2.5 : 2),
                Child = art != null ? null : new TextBlock { Text = name, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Vellum, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            if (tags.Contains("ready")) body.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.WinC, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.7 };
            if (art != null && tags.Contains("Frozen")) body.Opacity = 0.75;
            if (tags.Contains("Shield")) body.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xF0, 0xE0, 0x8A), BlurRadius = 14, ShadowDepth = 0, Opacity = 1 };
            var g = new Grid { Width = 84, Height = 100, Margin = new Thickness(3, 0, 3, 0), ToolTip = tip };
            if (tags.Contains("Taunt"))
                g.Children.Add(new Border { Width = 82, Height = 98, CornerRadius = new CornerRadius(41, 41, 12, 12), BorderBrush = Theme.B(Theme.Rgb(0x9A, 0x9E, 0xB8)), BorderThickness = new Thickness(3) });
            g.Children.Add(body);
            if (atk != "") g.Children.Add(StatGem(atk, Theme.Rgb(0xC8, 0xA0, 0x30), Theme.NightC, HorizontalAlignment.Left));
            if (hp != "") g.Children.Add(StatGem(hp, Theme.Rgb(0xC0, 0x40, 0x3A), Colors.White, HorizontalAlignment.Right));
            if (tags.Contains("Windfury"))
                g.Children.Add(new Border
                {
                    VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(3), Background = Theme.Raised, Padding = new Thickness(4, 0, 4, 1),
                    Child = new TextBlock { Text = "Windfury", FontSize = 9.5, Foreground = Theme.Vellum },
                });
            return g;
        }

        private static Border StatGem(string value, Color fill, Color text, HorizontalAlignment side) => new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Theme.B(fill), HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Theme.B(text), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }
}
