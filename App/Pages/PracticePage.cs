using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Pages
{
    /// <summary>
    /// Practice, from your own games: lethal puzzles (every turn HearthStoner saw you had lethal; the ones you missed are
    /// marked) and a mulligan quiz (your real opening hands, checked against the win-rate data).
    /// </summary>
    public static class PracticePage
    {

        public static UIElement Build(AppShell shell)
        {
            var d = shell.Data;
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.Children.Add(Quiz(shell));
            var lethal = Puzzles(shell);
            Grid.SetColumn(lethal, 2);
            cols.Children.Add(lethal);
            return AppShell.Page("Practice", "Drills built from your own games.", cols);
        }

        // ------------------------------------------------------------------ mulligan quiz

        /// <summary>Opening hands that have data for at least two cards, newest first.</summary>
        public static List<GameRecord> QuizHands(AppData d) =>
            d.Games.Where(g => g.Mulligan.Count(m => m.Advice == "KEEP" || m.Advice == "TOSS") >= 2).ToList();

        private static UIElement Quiz(AppShell shell)
        {
            var d = shell.Data;
            var p = new StackPanel();
            p.Children.Add(Theme.Heading("Mulligan quiz"));
            var hands = QuizHands(d);
            if (hands.Count == 0)
            {
                p.Children.Add(Theme.Text("No opening hands with data yet. Play a few games with HearthStoner running.", 13.5, Theme.Dusk, wrap: true));
                return p;
            }
            if (shell.Practice.QuizHand >= hands.Count) shell.Practice.QuizHand = 0;
            var g = hands[shell.Practice.QuizHand];
            var sub = Theme.Text($"{g.MyArchetype ?? MetaStats.ClassName(g.MyClass)} vs {g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass)}, " +
                                 $"{(g.WentFirst ? "going first" : "on the coin")}. Click a card to toss it, then check.", 13.5, Theme.Dusk, wrap: true);
            sub.Margin = new Thickness(0, -4, 0, 12);
            p.Children.Add(sub);

            var row = new WrapPanel();
            for (var i = 0; i < g.Mulligan.Count; i++)
            {
                var m = g.Mulligan[i];
                var idx = i;
                var keep = !shell.Practice.Keep.TryGetValue(i, out var k) || k;
                var card = new StackPanel { Width = 116, Margin = new Thickness(0, 0, 10, 10) };
                var img = CardTiles.GetRender(m.CardId);
                var pic = img != null
                    ? (UIElement)new Image { Source = img, Width = 116, Opacity = keep ? 1 : 0.35 }
                    : new Border { Height = 160, CornerRadius = new CornerRadius(8), Background = Theme.Slate, Child = Theme.Text(d.Cards.NameOf(m.CardId), 13, wrap: true) };
                card.Children.Add(pic);
                card.Children.Add(KeepTossButton(keep));
                if (shell.Practice.Checked && (m.Advice == "KEEP" || m.Advice == "TOSS"))
                {
                    var right = keep == (m.Advice == "KEEP");
                    var verdict = Theme.Text($"{(right ? "Right" : "Data: " + m.Advice.ToLowerInvariant())} ({(m.Delta >= 0 ? "+" : "")}{m.Delta * 100:F1}%)", 12.5, right ? Theme.Win : Theme.Loss, FontWeights.SemiBold);
                    verdict.HorizontalAlignment = HorizontalAlignment.Center;
                    card.Children.Add(verdict);
                    var you = Theme.Text($"you {(m.Kept ? "kept" : "tossed")} it", 12, Theme.Dusk);
                    you.HorizontalAlignment = HorizontalAlignment.Center;
                    card.Children.Add(you);
                }
                var b = new Border { Child = card, Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, ToolTip = d.Cards.NameOf(m.CardId) };
                if (!shell.Practice.Checked) b.MouseLeftButtonUp += (s, e) => { shell.Practice.Keep[idx] = !keep; shell.Redraw(); };
                row.Children.Add(b);
            }
            p.Children.Add(row);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            if (!shell.Practice.Checked)
                actions.Children.Add(Theme.Button("Check", () =>
                {
                    foreach (var (m, i) in g.Mulligan.Select((m, i) => (m, i)).Where(x => x.m.Advice == "KEEP" || x.m.Advice == "TOSS"))
                    {
                        var keep = !shell.Practice.Keep.TryGetValue(i, out var k) || k;
                        shell.Practice.Total++;
                        if (keep == (m.Advice == "KEEP")) shell.Practice.Right++;
                    }
                    shell.Practice.Checked = true;
                    shell.Redraw();
                }));
            else
                actions.Children.Add(Theme.Button(hands.Count > 1 ? "Next hand" : "Try again", () => { shell.Practice.QuizHand = (shell.Practice.QuizHand + 1) % hands.Count; shell.Practice.Keep.Clear(); shell.Practice.Checked = false; shell.Redraw(); }));
            if (shell.Practice.Total > 0)
            {
                var score = Theme.Text($"Score: {shell.Practice.Right} of {shell.Practice.Total} ({AppShell.Pct((double)shell.Practice.Right / shell.Practice.Total)})", 14, Theme.Vellum, FontWeights.SemiBold);
                score.VerticalAlignment = VerticalAlignment.Center;
                score.Margin = new Thickness(16, 0, 0, 0);
                actions.Children.Add(score);
            }
            p.Children.Add(actions);
            var note = Theme.Text($"Hand {shell.Practice.QuizHand + 1} of {hands.Count}. Percentages: how much more (or less) often players win when they keep the card in this matchup.", 12, Theme.Dusk, wrap: true);
            note.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(note);
            return p;
        }

        // ------------------------------------------------------------------ the puzzle drawn as a board

        /// <summary>A minion as the recorder saved it: "Name 3/2 (Taunt, Shield, ready)".</summary>
        private static readonly System.Text.RegularExpressions.Regex MinionText =
            new System.Text.RegularExpressions.Regex(@"^(?<name>.*) (?<atk>-?\d+)/(?<hp>-?\d+)(?: \((?<tags>[^)]*)\))?$");

        /// <summary>
        /// The puzzle as a little board: their hero's health, their minions, a divider, your minions, then your weapon,
        /// hand and mana. Shrinks to fit the column.
        /// </summary>
        private static UIElement PuzzleBoard(LethalPuzzle z)
        {
            var felt = Theme.Rgb(0x2A, 0x24, 0x18);
            var edge = Theme.Rgb(0x5A, 0x4A, 0x2A);
            var board = new StackPanel { Width = 600 };
            var hero = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) };
            hero.Children.Add(new Border
            {
                Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Background = Theme.B(Theme.Rgb(0x3A, 0x30, 0x20)), BorderBrush = Theme.B(Theme.Rgb(0x8A, 0x70, 0x40)), BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = (z.TheirHealth + z.TheirArmor).ToString(), FontFamily = Theme.Display, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Theme.Loss, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var hl = Theme.Text(z.TheirArmor > 0 ? $"Opponent: {z.TheirHealth} health + {z.TheirArmor} armor" : "Opponent health", 13, Theme.Mist);
            hl.VerticalAlignment = VerticalAlignment.Center;
            hl.Margin = new Thickness(10, 0, 0, 0);
            hero.Children.Add(hl);
            board.Children.Add(hero);
            board.Children.Add(MinionRow(z.TheirMinions, "Their board is empty"));
            board.Children.Add(new Border { Height = 1, Background = Theme.B(edge), Margin = new Thickness(0, 20, 0, 20) });
            board.Children.Add(MinionRow(z.YourMinions, "Your board is empty"));
            var info = new TextBlock { FontSize = 13, Foreground = Theme.Vellum, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            if (z.Weapon != null) info.Inlines.Add(new System.Windows.Documents.Run($"Weapon: {z.Weapon}    "));
            info.Inlines.Add(new System.Windows.Documents.Run($"Hand: {(z.Hand.Count > 0 ? string.Join(", ", z.Hand) : "empty")}    "));
            info.Inlines.Add(new System.Windows.Documents.Run($"{z.Mana} mana") { Foreground = Theme.Mana, FontWeight = FontWeights.SemiBold });
            board.Children.Add(info);
            return new Border
            {
                Background = Theme.B(felt), BorderBrush = Theme.B(edge), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14),
                Child = new Viewbox { Child = board, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center },
            };
        }

        private static UIElement MinionRow(List<string> minions, string empty)
        {
            if (minions.Count == 0)
            {
                var t = Theme.Text(empty, 13, Theme.Mist);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                return t;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var m in minions) row.Children.Add(MinionToken(m));
            return row;
        }

        /// <summary>One minion: an arch with its name, attack (gold) and health (red) gems; Taunt, Shield and ready marked.</summary>
        private static UIElement MinionToken(string text)
        {
            var match = MinionText.Match(text ?? "");
            var name = match.Success ? match.Groups["name"].Value : text;
            var tags = match.Success ? match.Groups["tags"].Value.Split(',').Select(s => s.Trim()).ToList() : new List<string>();
            var body = new Border
            {
                Width = 74, Height = 90, CornerRadius = new CornerRadius(37, 37, 10, 10), Padding = new Thickness(6, 4, 6, 16),
                Background = Theme.B(tags.Contains("Frozen") ? Theme.Rgb(0x2A, 0x4A, 0x70) : Theme.Rgb(0x2E, 0x35, 0x66)),
                BorderBrush = Theme.B(tags.Contains("ready") ? Theme.WinC : Theme.Rgb(0x4A, 0x52, 0x90)), BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = name, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Vellum, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            if (tags.Contains("Shield")) body.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xF0, 0xE0, 0x8A), BlurRadius = 14, ShadowDepth = 0, Opacity = 1 };
            var g = new Grid { Width = 84, Height = 100, Margin = new Thickness(3, 0, 3, 0), ToolTip = text };
            if (tags.Contains("Taunt"))
                g.Children.Add(new Border { Width = 82, Height = 98, CornerRadius = new CornerRadius(41, 41, 12, 12), BorderBrush = Theme.B(Theme.Rgb(0x9A, 0x9E, 0xB8)), BorderThickness = new Thickness(3) });
            g.Children.Add(body);
            if (match.Success)
            {
                g.Children.Add(StatGem(match.Groups["atk"].Value, Theme.Rgb(0xC8, 0xA0, 0x30), Theme.NightC, HorizontalAlignment.Left));
                g.Children.Add(StatGem(match.Groups["hp"].Value, Theme.Rgb(0xC0, 0x40, 0x3A), Colors.White, HorizontalAlignment.Right));
            }
            if (tags.Contains("Windfury"))
                g.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(3), Background = Theme.Raised, Padding = new Thickness(4, 0, 4, 1),
                    Child = new TextBlock { Text = "Windfury", FontSize = 9.5, Foreground = Theme.Vellum } });
            return g;
        }

        private static Border StatGem(string value, Color fill, Color text, HorizontalAlignment side) => new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Theme.B(fill), HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Theme.B(text), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };

        /// <summary>The card's Keep / Toss state as a button: green with a tick, or red with a cross (the whole card toggles it).</summary>
        private static Border KeepTossButton(bool keep)
        {
            var c = keep ? Theme.WinC : Theme.LossC;
            return new Border
            {
                Height = 32, Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(7),
                Background = Theme.B(c, 0.14), BorderBrush = Theme.B(c), BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text = keep ? "✓  Keep" : "✕  Toss", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Theme.B(c),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        // ------------------------------------------------------------------ lethal puzzles

        public static List<(GameRecord Game, TurnSnapshot Turn)> PuzzleList(AppData d) =>
            d.Games.SelectMany(g => g.Snapshots.Where(t => t.Puzzle != null).Select(t => (g, t))).ToList();

        private static UIElement Puzzles(AppShell shell)
        {
            var d = shell.Data;
            var p = new StackPanel();
            p.Children.Add(Theme.Heading("Lethal puzzles"));
            var list = PuzzleList(d);
            if (list.Count == 0)
            {
                p.Children.Add(Theme.Text("None yet. Whenever HearthStoner sees you have lethal, it saves the board here to solve again.", 13.5, Theme.Dusk, wrap: true));
                return p;
            }
            if (shell.Practice.Puzzle >= list.Count) shell.Practice.Puzzle = 0;
            var (g, t) = list[shell.Practice.Puzzle];
            var z = t.Puzzle;
            var missed = g.Review.Any(r => r.Kind == "MissedLethal" && r.Turn == t.Turn);
            var head = Theme.Text($"Turn {(t.Turn + 1) / 2} vs {g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass)}, {g.Start:MMM d}. " +
                $"Find {z.TheirHealth + z.TheirArmor} damage." + (missed ? " You missed this one." : ""), 13.5, missed ? Theme.Amber : Theme.Dusk, wrap: true);
            head.Margin = new Thickness(0, -4, 0, 12);
            p.Children.Add(head);
            p.Children.Add(PuzzleBoard(z));

            if (shell.Practice.SolutionShown)
            {
                var sol = new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 6, 0, 12),
                    Background = Theme.B(Theme.WinC, 0.10), BorderBrush = Theme.B(Theme.WinC, 0.5), BorderThickness = new Thickness(1),
                    Child = Theme.Text(z.Solution ?? "", 14, Theme.Vellum, wrap: true),
                };
                p.Children.Add(sol);
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (!shell.Practice.SolutionShown) actions.Children.Add(Theme.Button("Show the solution", () => { shell.Practice.SolutionShown = true; shell.Redraw(); }));
            var replayable = ReplayPage.Replayable(d);
            var ri = replayable.IndexOf(g);
            if (ri >= 0)
            {
                var watch = Theme.Button("Show the board in the replay", () =>
                {
                    shell.ReplayGame = ri;
                    var rp = ReplayPage.Get(g, d);
                    shell.ReplayFrame = rp != null && rp.TurnStarts.TryGetValue(t.Turn, out var f) ? f : 0;
                    shell.Show("Replays");
                }, primary: false);
                watch.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(watch);
            }
            if (list.Count > 1)
            {
                var next = Theme.Button("Next puzzle", () => { shell.Practice.Puzzle = (shell.Practice.Puzzle + 1) % list.Count; shell.Practice.SolutionShown = false; shell.Redraw(); }, primary: false);
                next.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(next);
            }
            p.Children.Add(actions);
            var key = Theme.Text("Grey ring: Taunt. Yellow glow: Divine Shield. Green edge: can attack now.", 12, Theme.Dusk, wrap: true);
            key.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(key);
            var count = Theme.Text($"Puzzle {shell.Practice.Puzzle + 1} of {list.Count}{(list.Count(x => x.Game.Review.Any(r => r.Kind == "MissedLethal" && r.Turn == x.Turn.Turn)) is int m && m > 0 ? $", {m} of them missed in the game" : "")}.", 12, Theme.Dusk);
            count.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(count);
            return p;
        }
    }
}
