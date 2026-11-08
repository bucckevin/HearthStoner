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
            // Very wide screens: all six drills in one row, and your progress in its own column on the right.
            var extra = shell.Size == PageWidth.Wide && shell.PageAreaWidth >= PageLayout.ExtraWideFrom;
            var p = extra ? new StackPanel() : new StackPanel { MaxWidth = shell.Size == PageWidth.Wide ? 1600 : 1200, HorizontalAlignment = HorizontalAlignment.Left };
            p.Children.Add(ReviewBox(shell, due, Store.StreakDays(today)));

            var columns = extra ? PracticeIds.Drills.Length : shell.Size == PageWidth.Compact ? 2 : 3;
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns, Margin = new Thickness(0, 22, 0, 0) };
            foreach (var drill in PracticeIds.Drills) tiles.Children.Add(Tile(shell, drill, due.Count(id => PracticeIds.DrillOf(id) == drill)));
            p.Children.Add(tiles);
            var note = Theme.Text("A question you miss comes back the next day; right answers push it back 3, then 7 days, and three right in a row retire it. New games add new questions by themselves.", 12.5, Theme.Dusk, wrap: true);
            note.Margin = new Thickness(0, 16, 0, 0);
            if (extra)
            {
                p.Children.Add(note);
                return PageLayout.Columns((p, 0), (ProgressColumn(today), ProgressColumnWidth));
            }
            p.Children.Add(Progress(shell.Size, today));
            var coming = ComingUpLine(today);
            if (coming != null) p.Children.Add(coming);
            p.Children.Add(note);
            return p;
        }

        /// <summary>The progress column's width on a very wide screen.</summary>
        private const double ProgressColumnWidth = 440;

        /// <summary>Weeks in the practice calendar.</summary>
        private const int CalendarWeeks = 5;

        /// <summary>Days ahead in "Coming up".</summary>
        private const int ComingDays = 7;

        /// <summary>The right-hand column on a very wide screen: the practice calendar, accuracy by drill and what comes due next.</summary>
        private static UIElement ProgressColumn(DateTime today)
        {
            var p = new StackPanel();
            p.Children.Add(Theme.Heading("Your progress"));
            p.Children.Add(Calendar(today));
            var acc = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            if (Store.Cards.Count == 0) acc.Children.Add(Theme.Text("Answer a few questions and your accuracy shows up here.", 13.5, Theme.Mist, wrap: true));
            else acc.Children.Add(AccuracyTable());
            p.Children.Add(acc);
            p.Children.Add(ComingUp(today));
            return p;
        }

        /// <summary>
        /// The last <see cref="CalendarWeeks"/> weeks, one square a day (Monday on top), brighter the more you answered;
        /// today outlined, each day's tally on hover.
        /// </summary>
        private static UIElement Calendar(DateTime today)
        {
            var p = new StackPanel();
            p.Children.Add(AppShell.ColumnLabel($"Last {CalendarWeeks} weeks · days you practised"));
            // Start on the Monday CalendarWeeks-1 weeks before this week's.
            var monday = today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var start = monday.AddDays(-7 * (CalendarWeeks - 1));
            var days = Store.LastDays(today, (int)(today.Date - start).TotalDays + 1);
            var most = Math.Max(1, days.Max(x => x.Answered));
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            for (var w = 0; w < CalendarWeeks; w++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            for (var r = 0; r < 7; r++) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            foreach (var (row, label) in new[] { (0, "M"), (2, "W"), (4, "F"), (6, "S") })
            {
                var t = Theme.Text(label, 11.5, Theme.Dusk);
                t.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(t, row);
                g.Children.Add(t);
            }
            foreach (var day in days)
            {
                var offset = (int)(day.Day - start).TotalDays;
                var isToday = day.Day == today.Date;
                var cell = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(4),
                    Background = day.Answered == 0 ? Theme.Raised : Theme.B(Theme.ManaC, 0.35 + 0.65 * day.Answered / most),
                    BorderBrush = isToday ? Theme.Vellum : null, BorderThickness = new Thickness(isToday ? 1.5 : 0),
                    ToolTip = day.Answered == 0 ? $"{day.Day:ddd MMM d}: none" : $"{day.Day:ddd MMM d}: {day.Answered} answered, {day.Right} right",
                };
                Grid.SetColumn(cell, 1 + offset / 7);
                Grid.SetRow(cell, offset % 7);
                g.Children.Add(cell);
            }
            p.Children.Add(g);
            return p;
        }

        /// <summary>How many questions come due on each of the next days, as a short list.</summary>
        private static UIElement ComingUp(DateTime today)
        {
            var p = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            p.Children.Add(AppShell.ColumnLabel("Coming up"));
            var next = Store.Upcoming(today, ComingDays);
            if (next.All(x => x.Due == 0))
            {
                var none = Theme.Text("Nothing scheduled: questions you answer come back over the next days.", 13, Theme.Mist, wrap: true);
                none.Margin = new Thickness(0, 6, 0, 0);
                p.Children.Add(none);
                return p;
            }
            foreach (var (day, count) in next)
            {
                var row = new Grid { Height = 28 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = Theme.Text(day == today.Date.AddDays(1) ? "Tomorrow" : day.ToString("ddd MMM d"), 13.5, count > 0 ? Theme.Vellum : Theme.Dusk);
                var n = Theme.Text(count == 0 ? "–" : $"{count} question{(count == 1 ? "" : "s")}", 13.5, count > 0 ? Theme.Mana : Theme.Dusk);
                Grid.SetColumn(n, 1);
                foreach (var t in new[] { name, n }) { t.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(t); }
                p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
            }
            return p;
        }

        /// <summary>"Coming up: 3 questions tomorrow, 5 more this week." under the progress (narrower windows); null when nothing is scheduled.</summary>
        private static UIElement ComingUpLine(DateTime today)
        {
            var next = Store.Upcoming(today, ComingDays);
            var tomorrow = next[0].Due;
            var rest = next.Skip(1).Sum(x => x.Due);
            if (tomorrow + rest == 0) return null;
            var parts = new List<string>();
            if (tomorrow > 0) parts.Add($"{tomorrow} question{(tomorrow == 1 ? "" : "s")} tomorrow");
            if (rest > 0) parts.Add($"{rest}{(tomorrow > 0 ? " more" : " question" + (rest == 1 ? "" : "s"))} in the {ComingDays - 1} days after");
            var t = Theme.Text("Coming up: " + string.Join(", ", parts) + ".", 13, Theme.Mist, wrap: true);
            t.Margin = new Thickness(0, 12, 0, 0);
            return t;
        }

        /// <summary>Days shown in the progress strip.</summary>
        private const int ProgressDays = 14;

        /// <summary>
        /// Your progress: questions answered on each of the last two weeks' days, and how often you're right in each
        /// drill. Side by side, or one under the other in a small window; a short line until your first answer.
        /// </summary>
        private static UIElement Progress(PageWidth size, DateTime today)
        {
            var p = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            p.Children.Add(Theme.Heading("Your progress"));
            if (Store.Cards.Count == 0)
            {
                p.Children.Add(Theme.Text("Answer a few questions and your accuracy and daily progress show up here.", 13.5, Theme.Mist, wrap: true));
                return p;
            }
            var strip = DayStrip(Store.LastDays(today, ProgressDays));
            var accuracy = AccuracyTable();
            p.Children.Add(size == PageWidth.Compact
                ? PageLayout.Stack(18, strip, accuracy)
                : PageLayout.Columns((strip, 0), (accuracy, 0)));
            return p;
        }

        /// <summary>One bar per day (height = questions answered), today outlined, with each day's tally on hover.</summary>
        private static UIElement DayStrip(List<PracticeDay> days)
        {
            const double barArea = 80;
            var p = new StackPanel();
            p.Children.Add(AppShell.ColumnLabel($"Last {days.Count} days · questions answered"));
            var bars = new Grid { Height = barArea, Margin = new Thickness(0, 8, 0, 0) };
            var most = Math.Max(1, days.Max(x => x.Answered));
            for (var i = 0; i < days.Count; i++)
            {
                var day = days[i];
                bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var isToday = i == days.Count - 1;
                var bar = new Border
                {
                    Height = day.Answered == 0 ? 2 : Math.Max(4, barArea * day.Answered / most), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(3, 3, 0, 0), Background = day.Answered == 0 ? Theme.Raised : Theme.Mana,
                    BorderBrush = isToday ? Theme.Vellum : null, BorderThickness = new Thickness(isToday ? 1.5 : 0),
                    ToolTip = day.Answered == 0 ? $"{day.Day:ddd MMM d}: none" : $"{day.Day:ddd MMM d}: {day.Answered} answered, {day.Right} right",
                };
                Grid.SetColumn(bar, i);
                bars.Children.Add(bar);
            }
            p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = bars });
            var dates = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var end = Theme.Text("today", 12.5, Theme.Mist);
            DockPanel.SetDock(end, Dock.Right);
            dates.Children.Add(end);
            dates.Children.Add(Theme.Text(days[0].Day.ToString("MMM d"), 12.5, Theme.Mist));
            p.Children.Add(dates);
            return p;
        }

        /// <summary>Each drill's share of right answers, with how many answers that's from, and the questions you've retired.</summary>
        private static UIElement AccuracyTable()
        {
            var p = new StackPanel();
            p.Children.Add(AppShell.ColumnLabel("Accuracy by drill"));
            var retired = 0;
            foreach (var drill in PracticeIds.Drills)
            {
                var a = Store.Accuracy(drill);
                retired += a.Retired;
                var row = new Grid { Height = 30 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                var name = Theme.Text(DrillInfo[drill].Title, 13.5);
                var brush = a.Answered == 0 ? Theme.Dusk : Theme.RateBrush(a.Rate);
                var bar = AppShell.StatBar(a.Rate, brush); bar.Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(bar, 1);
                var pct = Theme.Text(a.Answered == 0 ? "–" : AppShell.Pct(a.Rate), 13.5, brush, FontWeights.SemiBold); pct.TextAlignment = TextAlignment.Right; Grid.SetColumn(pct, 2);
                var n = Theme.Text(a.Answered == 0 ? "none yet" : $"of {a.Answered}", 12.5, Theme.Mist); n.TextAlignment = TextAlignment.Right; Grid.SetColumn(n, 3);
                foreach (var t in new FrameworkElement[] { name, bar, pct, n }) { t.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(t); }
                p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
            }
            var learned = Theme.Text(retired == 0 ? "No questions retired yet: three right in a row retires one." : $"{retired} question{(retired == 1 ? "" : "s")} retired (learned).", 12.5, Theme.Mist, wrap: true);
            learned.Margin = new Thickness(0, 6, 0, 0);
            p.Children.Add(learned);
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
