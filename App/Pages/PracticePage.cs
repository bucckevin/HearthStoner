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
                var label = Theme.Text(keep ? "Keep" : "Toss", 13.5, keep ? Theme.Vellum : Theme.Dusk, FontWeights.SemiBold);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                card.Children.Add(label);
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
            var head = Theme.Text($"Turn {(t.Turn + 1) / 2} vs {g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass)}, {g.Start:MMM d}" + (missed ? ". You missed this one." : ""), 13.5, missed ? Theme.Gold : Theme.Dusk, wrap: true);
            head.Margin = new Thickness(0, -4, 0, 12);
            p.Children.Add(head);

            void Line(string label, string value)
            {
                var g2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g2.Children.Add(Theme.Text(label, 13.5, Theme.Dusk));
                var v = Theme.Text(value, 14, Theme.Vellum, wrap: true);
                Grid.SetColumn(v, 1);
                g2.Children.Add(v);
                p.Children.Add(g2);
            }
            Line("Opponent", $"{z.TheirHealth} health{(z.TheirArmor > 0 ? $" + {z.TheirArmor} armor" : "")}");
            Line("Their board", z.TheirMinions.Count > 0 ? string.Join(", ", z.TheirMinions) : "empty");
            Line("Your board", (z.YourMinions.Count > 0 ? string.Join(", ", z.YourMinions) : "empty") + (z.Weapon != null ? $"; weapon {z.Weapon}" : ""));
            Line($"Your hand", $"{z.Mana} mana: {(z.Hand.Count > 0 ? string.Join(", ", z.Hand) : "empty")}");

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
            var count = Theme.Text($"Puzzle {shell.Practice.Puzzle + 1} of {list.Count}{(list.Count(x => x.Game.Review.Any(r => r.Kind == "MissedLethal" && r.Turn == x.Turn.Turn)) is int m && m > 0 ? $", {m} of them missed in the game" : "")}.", 12, Theme.Dusk);
            count.Margin = new Thickness(0, 12, 0, 0);
            p.Children.Add(count);
            return p;
        }
    }
}
