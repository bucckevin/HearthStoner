using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Pages
{
    /// <summary>
    /// Practice, from your own games. The home shows today's review (questions you missed, spaced out over days by
    /// <see cref="PracticeStore"/>) and a tile per drill; a drill or the review shows one question at a time. Each
    /// drill lives in its own part of this class: mulligan, lethal, Discover, and the three drills that need a replay
    /// (what would you play, guess their hand, secret guessing).
    /// </summary>
    public static partial class PracticePage
    {
        private static PracticeStore _store;
        /// <summary>Your answers and review schedule (practice.json), loaded the first time it's needed.</summary>
        private static PracticeStore Store => _store ??= PracticeStore.Load();

        private static readonly Dictionary<string, (string Title, string Blurb)> DrillInfo = new Dictionary<string, (string, string)>
        {
            [PracticeIds.Play] = ("What would you play?", "A turn from one of your replays: pick your plays, then see how they use your mana and what you did."),
            [PracticeIds.Hand] = ("Guess their hand", "Name cards your opponent was holding, from how long they held them and their likely deck."),
            [PracticeIds.Secret] = ("Secret guessing", "They played a secret: which one? From their class and what happened before your turn."),
            [PracticeIds.Mulligan] = ("Mulligan by matchup", "Your real opening hands, checked against the win-rate data. Pick a matchup to drill it."),
            [PracticeIds.Discover] = ("Discover drill", "Three Discover options from your games: pick one, then see the data and what you chose."),
            [PracticeIds.Lethal] = ("Lethal puzzles", "Boards where HearthStoner saw you had lethal. Find the damage, then check the solution."),
        };

        public static UIElement Build(AppShell shell)
        {
            var st = shell.Practice;
            if (st.Drill == null) return AppShell.Page("Practice", "Drills built from your own games.", Home(shell));
            var title = st.Drill == PracticeState.Review ? "Today's review" : DrillInfo[st.Drill].Title;
            return AppShell.Page(title, null, DrillView(shell), Theme.Button("Back to Practice", () => { st.Drill = null; shell.Redraw(); }, primary: false));
        }

        // ------------------------------------------------------------------ home

        private static UIElement Home(AppShell shell)
        {
            var d = shell.Data;
            var today = DateTime.Today;
            var due = Store.Due(today).Where(id => GameFor(d, id) != null).ToList();
            var p = new StackPanel { MaxWidth = shell.Size == PageWidth.Wide ? 1600 : 1200, HorizontalAlignment = HorizontalAlignment.Left };
            p.Children.Add(ReviewBox(shell, due, Store.StreakDays(today)));

            var columns = shell.Size == PageWidth.Compact ? 2 : 3;
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns, Margin = new Thickness(0, 22, 0, 0) };
            foreach (var drill in PracticeIds.Drills) tiles.Children.Add(Tile(shell, drill, due.Count(id => PracticeIds.DrillOf(id) == drill)));
            p.Children.Add(tiles);
            var note = Theme.Text("A question you miss comes back the next day; right answers push it back 3, then 7 days, and three right in a row retire it. New games add new questions by themselves.", 12.5, Theme.Dusk, wrap: true);
            note.Margin = new Thickness(0, 16, 0, 0);
            p.Children.Add(note);
            return p;
        }

        private static UIElement ReviewBox(AppShell shell, List<string> due, int streak)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 28, 0) };
            left.Children.Add(AppShell.ColumnLabel("Today's review"));
            var count = new TextBlock { FontFamily = Theme.Display, FontWeight = FontWeights.Bold, FontSize = 44, Foreground = Theme.Vellum };
            count.Inlines.Add(new System.Windows.Documents.Run(due.Count.ToString()));
            count.Inlines.Add(new System.Windows.Documents.Run(due.Count == 1 ? " question" : " questions") { FontSize = 20, Foreground = Theme.Mist });
            left.Children.Add(count);
            g.Children.Add(left);
            var text = Theme.Text(due.Count > 0
                ? "Questions you missed, from every drill. Get one right three times in a row and it's retired."
                : "Nothing due. Answer some questions in the drills below: the ones you miss come back here.", 13.5, Theme.Mist, wrap: true);
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            var start = Theme.Button("Start review", () => Open(shell, PracticeState.Review, due));
            start.IsEnabled = due.Count > 0;
            start.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(start);
            if (streak > 0)
            {
                var s = Theme.Text($"Streak: {streak} day{(streak == 1 ? "" : "s")}", 12.5, Theme.Mist);
                s.HorizontalAlignment = HorizontalAlignment.Right;
                s.Margin = new Thickness(0, 6, 0, 0);
                right.Children.Add(s);
            }
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            return new Border
            {
                Child = g, CornerRadius = new CornerRadius(14), Padding = new Thickness(24, 18, 24, 18), BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromArgb(40, 90, 160, 255), Color.FromArgb(10, 90, 160, 255), 0),
            };
        }

        private static UIElement Tile(AppShell shell, string drill, int due)
        {
            var d = shell.Data;
            var ids = Questions(d, drill);
            var (title, blurb) = DrillInfo[drill];
            var p = new StackPanel();
            p.Children.Add(Theme.Text(title, 21, Theme.Vellum, FontWeights.SemiBold, display: true));
            var b = Theme.Text(blurb, 13, Theme.Mist, wrap: true);
            b.Margin = new Thickness(0, 6, 0, 10);
            p.Children.Add(b);
            var what = drill == PracticeIds.Secret ? $"{ids.Count} game{(ids.Count == 1 ? "" : "s")} to search" : $"{ids.Count} question{(ids.Count == 1 ? "" : "s")}";
            // An empty drill says why it's empty instead of "0 due · 0 questions".
            var empty = ids.Count == 0;
            p.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 4), HorizontalAlignment = HorizontalAlignment.Left,
                Background = empty ? Theme.Raised : Theme.B(Theme.Rgb(0x2E, 0x35, 0x66)),
                Child = new TextBlock { Text = empty ? "None yet · fills up as you play" : $"{due} due · {what}", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = empty ? Theme.Mist : Theme.Mana },
            });
            var tile = new Border
            {
                Child = p, Background = Theme.Slate, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18), Margin = new Thickness(0, 0, 16, 16), Cursor = ids.Count > 0 ? System.Windows.Input.Cursors.Hand : null, Opacity = ids.Count > 0 ? 1 : 0.55,
                ToolTip = ids.Count > 0 ? "Open this drill" : "No questions yet: play more games with HearthStoner running",
            };
            if (ids.Count > 0) tile.MouseLeftButtonUp += (s, e) => Open(shell, drill, Order(ids));
            return tile;
        }

        /// <summary>Due questions first, then ones you haven't answered, then the rest (newest games first within each).</summary>
        private static List<string> Order(List<string> ids)
        {
            var today = DateTime.Today;
            int Rank(string id) => !Store.Cards.TryGetValue(id, out var c) ? 1 : !c.Retired && c.Due <= today ? 0 : 2;
            return ids.Select((id, i) => (id, i)).OrderBy(x => Rank(x.id)).ThenBy(x => x.i).Select(x => x.id).ToList();
        }

        /// <summary>
        /// For the screenshot command: shows a drill (null: the home) with its questions in order, or its first
        /// question answered, without writing practice.json. Returns the games of its first questions, so their
        /// replays can be built before drawing.
        /// </summary>
        public static List<GameRecord> ShowForScreenshot(AppShell shell, string drill, bool answered)
        {
            var st = shell.Practice;
            if (drill == null) { st.Drill = null; return new List<GameRecord>(); }
            var ids = Questions(shell.Data, drill);
            if (st.Drill != drill) Open(shell, drill, ids);
            st.ResetAnswer();
            if (answered) { st.Answered = true; st.Choice = 0; }
            return ids.Take(8).Select(id => GameFor(shell.Data, id)).Where(g => g != null).Distinct().ToList();
        }

        private static void Open(AppShell shell, string drill, List<string> queue)
        {
            var st = shell.Practice;
            st.Drill = drill;
            st.Queue = queue;
            st.Index = 0;
            st.ResetAnswer();
            shell.Redraw();
        }

        // ------------------------------------------------------------------ questions

        /// <summary>Every question of a drill from your games, newest games first.</summary>
        private static List<string> Questions(AppData d, string drill)
        {
            switch (drill)
            {
                case PracticeIds.Play: return d.Games.SelectMany(PracticeDrills.PlayQuestions).ToList();
                case PracticeIds.Hand: return d.Games.SelectMany(PracticeDrills.HandQuestions).ToList();
                case PracticeIds.Secret: return d.Games.Where(PracticeDrills.MayHaveSecrets).Select(g => PracticeIds.Make(PracticeIds.Secret, g)).ToList();
                case PracticeIds.Mulligan: return QuizHands(d).Select(g => PracticeIds.Make(PracticeIds.Mulligan, g)).ToList();
                case PracticeIds.Discover: return d.Games.SelectMany(PracticeDrills.DiscoverQuestions).ToList();
                case PracticeIds.Lethal: return PuzzleList(d).Select(x => PracticeIds.Make(PracticeIds.Lethal, x.Game, x.Turn.Turn)).ToList();
                default: return new List<string>();
            }
        }

        private static GameRecord GameFor(AppData d, string id)
        {
            var stem = PracticeIds.GameOf(id);
            return stem == null ? null : d.Games.FirstOrDefault(g => g.FileStem == stem);
        }

        // ------------------------------------------------------------------ one question

        /// <summary>
        /// The shown question of the drill or review. A question that turns out to have nothing to ask (no card was
        /// revealed later, the turn isn't in the replay) is skipped; one waiting for its replay shows a note.
        /// </summary>
        private static UIElement DrillView(AppShell shell)
        {
            var st = shell.Practice;
            var p = new StackPanel { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
            if (st.Drill == PracticeIds.Mulligan) p.Children.Add(MatchupChips(shell));
            for (var guard = 0; st.Index < st.Queue.Count && guard < st.Queue.Count + 50; guard++)
            {
                var id = st.Queue[st.Index];
                var g = GameFor(shell.Data, id);
                var q = g == null ? Skip : Question(shell, id, g);
                if (q == Again) continue;
                if (q == Skip) { st.Index++; st.ResetAnswer(); continue; }
                p.Children.Add(Progress(shell));
                p.Children.Add(q);
                return p;
            }
            p.Children.Add(Done(shell));
            return p;
        }

        /// <summary>Returned by a question that has nothing to ask: the page moves on to the next one.</summary>
        private static readonly UIElement Skip = new Border();

        private static UIElement Question(AppShell shell, string id, GameRecord g)
        {
            switch (PracticeIds.DrillOf(id))
            {
                case PracticeIds.Play: return PlayQuestion(shell, id, g);
                case PracticeIds.Hand: return HandQuestion(shell, id, g);
                case PracticeIds.Secret: return SecretQuestion(shell, id, g);
                case PracticeIds.Mulligan: return MulliganQuestion(shell, id, g);
                case PracticeIds.Discover: return DiscoverQuestion(shell, id, g);
                case PracticeIds.Lethal: return LethalQuestion(shell, id, g);
                default: return Skip;
            }
        }

        private static UIElement Progress(AppShell shell)
        {
            var st = shell.Practice;
            var text = $"Question {st.Index + 1} of {st.Queue.Count}" + (st.Total > 0 ? $"   ·   this session {st.Right} of {st.Total} right" : "");
            var t = Theme.Text(text, 13, Theme.Mist);
            t.Margin = new Thickness(0, 0, 0, 12);
            return t;
        }

        private static UIElement Done(AppShell shell)
        {
            var st = shell.Practice;
            var p = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var nothing = st.Queue.Count == 0;
            p.Children.Add(Theme.Heading(nothing ? "Nothing to ask here yet" : "All done"));
            var why = st.Drill == PracticeIds.Secret && nothing
                ? "None of the secrets in these games was revealed later, so there's no answer to check. Secrets that trigger in your next games show up here."
                : "Play more games with HearthStoner running and new questions show up here.";
            var t = Theme.Text(st.Total > 0 ? $"{st.Right} of {st.Total} right this session. Missed questions come back in tomorrow's review." : why, 14, Theme.Mist, wrap: true);
            p.Children.Add(t);
            var back = Theme.Button("Back to Practice", () => { st.Drill = null; shell.Redraw(); });
            back.Margin = new Thickness(0, 16, 0, 0);
            p.Children.Add(back);
            return p;
        }

        /// <summary>Records the answer to the shown question (score and review schedule) and redraws.</summary>
        private static void Answer(AppShell shell, string id, bool right)
        {
            var st = shell.Practice;
            if (st.Answered) return;
            st.Answered = true;
            st.WasRight = right;
            st.Total++;
            if (right) st.Right++;
            Store.Record(id, right, DateTime.Today);
            Store.Save();
            shell.Redraw();
        }

        /// <summary>The "Next question" button under an answered question.</summary>
        private static Button NextButton(AppShell shell)
        {
            var st = shell.Practice;
            var b = Theme.Button(st.Index + 1 < st.Queue.Count ? "Next question" : "Finish", () => { st.Index++; st.ResetAnswer(); shell.Redraw(); });
            b.Margin = new Thickness(0, 14, 0, 0);
            return b;
        }

        /// <summary>A green (right) or amber (missed) box with the answer explained.</summary>
        private static Border ResultBox(bool right, string headline, string body)
        {
            var c = right ? Theme.WinC : Theme.AmberC;
            var t = new TextBlock { FontSize = 14, Foreground = Theme.Vellum, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
            t.Inlines.Add(new System.Windows.Documents.Run(headline) { Foreground = Theme.B(c), FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(body)) t.Inlines.Add(new System.Windows.Documents.Run(" " + body));
            return new Border
            {
                Child = t, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 14, 0, 0),
                Background = Theme.B(c, 0.10), BorderBrush = Theme.B(c, 0.5), BorderThickness = new Thickness(1),
            };
        }

        /// <summary>The line under a question's title: game, turn and opponent.</summary>
        private static TextBlock Context(string text)
        {
            var t = Theme.Text(text, 14, Theme.Mist, wrap: true);
            t.Margin = new Thickness(0, 0, 0, 14);
            return t;
        }

        private static string Opponent(GameRecord g) => g.OpponentArchetype ?? MetaStats.ClassName(g.OpponentClass);

        /// <summary>A card picture (or a named box while it downloads), optionally faded or outlined.</summary>
        private static FrameworkElement CardPicture(AppData d, string cardId, double width, double opacity = 1, Brush outline = null)
        {
            var img = CardTiles.GetRender(cardId);
            FrameworkElement pic = img != null
                ? new Image { Source = img, Width = width, Opacity = opacity }
                : new Border { Width = width, Height = width * 1.38, CornerRadius = new CornerRadius(8), Background = Theme.Raised, Opacity = opacity, Padding = new Thickness(8), Child = Theme.Text(d.Cards.NameOf(cardId), 13, wrap: true) };
            if (outline == null) return pic;
            return new Border { Child = pic, BorderBrush = outline, BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(10) };
        }

        /// <summary>A clickable choice: a rounded button showing a card name, highlighted when picked or marked after the answer.</summary>
        private static Border ChoiceChip(string text, bool picked, bool? correct, Action click)
        {
            var c = correct == true ? Theme.WinC : correct == false ? Theme.LossC : picked ? Theme.ManaC : Theme.LineC;
            var b = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(0, 0, 8, 8),
                Background = picked || correct.HasValue ? Theme.B(c, 0.16) : Theme.Raised, BorderBrush = Theme.B(c), BorderThickness = new Thickness(1.5),
                Cursor = click != null ? System.Windows.Input.Cursors.Hand : null,
                Child = new TextBlock { Text = text, FontSize = 14, Foreground = Theme.Vellum, FontWeight = picked ? FontWeights.SemiBold : FontWeights.Normal },
            };
            if (click != null) b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        /// <summary>Shown while a question's replay is being built from the game's log.</summary>
        private static UIElement LoadingReplay() =>
            Theme.Text("Building this game's replay from its log... it shows up here in a moment.", 14, Theme.Mist, wrap: true);
    }
}
