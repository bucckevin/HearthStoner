using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HearthStoner.Pages
{
    public static partial class PracticePage
    {
        /// <summary>Opening hands that have data for at least two cards, newest first.</summary>
        public static List<GameRecord> QuizHands(AppData d) =>
            d.Games.Where(g => g.Mulligan.Count(m => m.Advice == "KEEP" || m.Advice == "TOSS") >= 2).ToList();

        /// <summary>The opponent-class chips above the mulligan drill: "Any" and each class you have hands against.</summary>
        private static UIElement MatchupChips(AppShell shell)
        {
            var st = shell.Practice;
            var d = shell.Data;
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            var hands = QuizHands(d);
            void Chip(string label, string cls)
            {
                var on = st.MatchupClass == cls;
                var c = ChoiceChip(label, on, null, () =>
                {
                    st.MatchupClass = cls;
                    var ids = hands.Where(g => cls == null || g.OpponentClass == cls).Select(g => PracticeIds.Make(PracticeIds.Mulligan, g)).ToList();
                    Open(shell, PracticeIds.Mulligan, Order(ids));
                });
                c.CornerRadius = new CornerRadius(14);
                wrap.Children.Add(c);
            }
            Chip($"Any · {hands.Count}", null);
            foreach (var grp in hands.Where(g => !string.IsNullOrEmpty(g.OpponentClass)).GroupBy(g => g.OpponentClass).OrderByDescending(x => x.Count()))
                Chip($"vs {MetaStats.ClassName(grp.Key)} · {grp.Count()}", grp.Key);
            return wrap;
        }

        /// <summary>One opening hand: toss cards by clicking them, then check against the data.</summary>
        private static UIElement MulliganQuestion(AppShell shell, string id, GameRecord g)
        {
            var st = shell.Practice;
            var d = shell.Data;
            var p = new StackPanel();
            p.Children.Add(Context($"{g.MyArchetype ?? MetaStats.ClassName(g.MyClass)} vs {Opponent(g)}, {(g.WentFirst ? "going first" : "on the coin")}, {g.Start:MMM d}. Click a card to toss it, then check."));
            var row = new WrapPanel();
            for (var i = 0; i < g.Mulligan.Count; i++)
            {
                var m = g.Mulligan[i];
                var idx = i;
                var keep = !st.Keep.TryGetValue(i, out var k) || k;
                var card = new StackPanel { Width = 130, Margin = new Thickness(0, 0, 14, 10) };
                card.Children.Add(CardPicture(d, m.CardId, 130, keep ? 1 : 0.35));
                card.Children.Add(KeepTossButton(keep));
                if (st.Answered && (m.Advice == "KEEP" || m.Advice == "TOSS"))
                {
                    var right = keep == (m.Advice == "KEEP");
                    var verdict = Theme.Text($"{(right ? "Right" : "Data: " + m.Advice.ToLowerInvariant())} ({(m.Delta >= 0 ? "+" : "")}{m.Delta * 100:F1}%)", 13, right ? Theme.Win : Theme.Loss, FontWeights.SemiBold);
                    verdict.HorizontalAlignment = HorizontalAlignment.Center;
                    verdict.Margin = new Thickness(0, 6, 0, 0);
                    card.Children.Add(verdict);
                    var you = Theme.Text($"you {(m.Kept ? "kept" : "tossed")} it", 12.5, Theme.Mist);
                    you.HorizontalAlignment = HorizontalAlignment.Center;
                    card.Children.Add(you);
                }
                var b = new Border { Child = card, Cursor = st.Answered ? null : System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent, ToolTip = d.Cards.NameOf(m.CardId) };
                if (!st.Answered) b.MouseLeftButtonUp += (s, e) => { st.Keep[idx] = !keep; shell.Redraw(); };
                row.Children.Add(b);
            }
            p.Children.Add(row);
            if (!st.Answered)
            {
                var check = Theme.Button("Check", () =>
                {
                    var judged = g.Mulligan.Select((m, i) => (m, i)).Where(x => x.m.Advice == "KEEP" || x.m.Advice == "TOSS").ToList();
                    var all = judged.All(x => (!st.Keep.TryGetValue(x.i, out var kk) || kk) == (x.m.Advice == "KEEP"));
                    Answer(shell, id, all);
                });
                check.Margin = new Thickness(0, 4, 0, 0);
                p.Children.Add(check);
                p.Children.Add(Note("Percentages after checking: how much more (or less) often players win when they keep the card in this matchup."));
                return p;
            }
            p.Children.Add(ResultBox(st.WasRight, st.WasRight ? "Every card right." : "Not quite.", st.WasRight ? null : "The cards in red are the ones the data disagrees with."));
            p.Children.Add(MatchupTable(d, g));
            p.Children.Add(NextButton(shell));
            return p;
        }

        /// <summary>This matchup at a glance: the cards that matter most, what they're worth kept, and how often you keep them.</summary>
        private static UIElement MatchupTable(AppData d, GameRecord g)
        {
            var rows = PracticeDrills.MatchupKeeps(d.Games, g.OpponentClass).Take(6).ToList();
            var p = new StackPanel { Margin = new Thickness(0, 18, 0, 0), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
            if (rows.Count == 0) return p;
            var head = AppShell.ColumnLabel($"Vs {MetaStats.ClassName(g.OpponentClass)}: your cards at a glance");
            head.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(head);
            foreach (var r in rows)
            {
                var row = new Grid { Height = 32 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                var n = Theme.Text(d.Cards.NameOf(r.CardId), 14);
                var w = Theme.Text($"{(r.Delta >= 0 ? "+" : "")}{r.Delta * 100:F1}% kept", 14, r.Delta >= 0 ? Theme.Win : Theme.Loss, FontWeights.SemiBold);
                w.TextAlignment = TextAlignment.Right;
                var k = Theme.Text($"you keep {AppShell.Pct(r.KeepRate)} ({r.Seen})", 13, Theme.Mist);
                k.TextAlignment = TextAlignment.Right;
                var col = 0;
                foreach (var t in new[] { n, w, k }) { t.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(t, col++); row.Children.Add(t); }
                p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
            }
            return p;
        }

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

        /// <summary>A small grey explanation under a question.</summary>
        private static TextBlock Note(string text)
        {
            var t = Theme.Text(text, 12.5, Theme.Dusk, wrap: true);
            t.Margin = new Thickness(0, 12, 0, 0);
            return t;
        }
    }
}
