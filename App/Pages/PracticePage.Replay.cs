using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Pages
{
    /// <summary>The drills that need a game's replay: what would you play, guess their hand, secret guessing.</summary>
    public static partial class PracticePage
    {
        // ------------------------------------------------------------------ what would you play?

        /// <summary>
        /// The start of one of your turns: the boards and your hand. You pick cards in order; the answer is right when
        /// it uses as much of your mana as any set of playable cards can (the "This turn" helper's ranking). Then it
        /// shows what you played in the game and how your win chance moved over that turn.
        /// </summary>
        private static UIElement PlayQuestion(AppShell shell, string id, GameRecord g)
        {
            var st = shell.Practice;
            var d = shell.Data;
            var r = ReplayPage.TryGet(g, shell);
            if (r == null) return LoadingReplay();
            var turn = PracticeIds.TurnOf(id);
            var fi = PracticeDrills.TurnFrame(r, turn);
            if (fi < 0) return Skip;
            var f = r.Frames[fi];
            var mana = f.You.Mana;
            var hand = f.You.Hand.Select((h, i) => (Card: h, Slot: i)).Where(x => x.Card.CardId != null).ToList();
            // The Coin gives a mana instead of costing one: it widens what fits, and isn't counted as mana used.
            bool IsCoin(Replay.HandCard h) => d.Cards.NameOf(h.CardId) == "The Coin";
            var coins = hand.Count(x => IsCoin(x.Card));
            var playable = hand.Where(x => !IsCoin(x.Card) && x.Card.Cost <= mana + coins).ToList();
            if (playable.Count < 2) return Skip;   // no real choice
            var best = PracticeDrills.BestManaUse(playable.Select(x => x.Card.Cost).ToList(), mana + coins);
            var picked = st.PickOrder.Select(s => hand.First(x => x.Slot == s).Card).ToList();
            var used = picked.Where(h => !IsCoin(h)).Sum(h => h.Cost);
            var limit = mana + picked.Count(IsCoin);

            var p = new StackPanel();
            p.Children.Add(Context($"Turn {f.Round} vs {Opponent(g)}, {g.Start:MMM d}. You have {mana} mana. Click the cards you'd play, in order (hero power not counted)."));
            var footer = Theme.Text($"Your health: {f.You.Health}{(f.You.Armor > 0 ? $" + {f.You.Armor} armor" : "")}", 13, Theme.Vellum);
            footer.HorizontalAlignment = HorizontalAlignment.Center;
            footer.Margin = new Thickness(0, 16, 0, 0);
            p.Children.Add(Felt(f.Them.Health + f.Them.Armor, "Opponent health", f.Them.Board.Select(m => ReplayMinion(d, m, false)), f.You.Board.Select(m => ReplayMinion(d, m, true)), footer));

            var label = AppShell.ColumnLabel($"Your hand · {used} of {limit} mana picked" + (coins > 0 ? " (The Coin adds 1)" : ""));
            label.Margin = new Thickness(0, 14, 0, 8);
            p.Children.Add(label);
            var row = new WrapPanel();
            foreach (var (card, slot) in hand)
            {
                var order = st.PickOrder.IndexOf(slot);
                var canPlay = IsCoin(card) || card.Cost <= mana + coins;
                var cell = new Grid { Margin = new Thickness(0, 0, 12, 10) };
                cell.Children.Add(CardPicture(d, card.CardId, 112, canPlay ? 1 : 0.35, order >= 0 ? Theme.Mana : null));
                if (order >= 0)
                    cell.Children.Add(new Border
                    {
                        Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Theme.Mana, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -6, 0),
                        Child = new TextBlock { Text = (order + 1).ToString(), FontWeight = FontWeights.Bold, Foreground = Theme.B(Theme.Rgb(0x0B, 0x14, 0x30)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    });
                var b = new Border { Child = cell, Background = Brushes.Transparent, ToolTip = $"{d.Cards.NameOf(card.CardId)} ({card.Cost})", Cursor = canPlay && !st.Answered ? System.Windows.Input.Cursors.Hand : null };
                if (canPlay && !st.Answered)
                    b.MouseLeftButtonUp += (s, e) =>
                    {
                        if (!st.PickOrder.Remove(slot)) st.PickOrder.Add(slot);
                        shell.Redraw();
                    };
                row.Children.Add(b);
            }
            p.Children.Add(row);

            if (!st.Answered)
            {
                var lockIn = Theme.Button("Lock in", () => Answer(shell, id, used <= limit && used == best));
                lockIn.IsEnabled = st.PickOrder.Count > 0 && used <= limit;
                p.Children.Add(lockIn);
                if (used > limit) p.Children.Add(Note($"That's {used} mana: more than the {limit} you have."));
                return p;
            }
            var chance = ReplayPage.Analyze(g, r, d).Chance;
            var after = r.TurnStarts.TryGetValue(turn + 1, out var next) ? next : r.Frames.Count - 1;
            var played = PracticeDrills.PlayedInTurn(r, turn).Select(d.Cards.NameOf).ToList();
            var body = (played.Count > 0 ? $"In the game you played {string.Join(", ", played)}" : "In the game you played no cards") +
                       $"; your win chance went from {AppShell.Pct(chance.At(fi))} to {AppShell.Pct(chance.At(after))} over that turn.";
            p.Children.Add(ResultBox(st.WasRight, st.WasRight ? $"You use all {best} mana you could." : $"You'd use {used} of {mana + coins} mana; the most you could use was {best}.", body));
            var ideas = r.AdviceAt(fi).Where(s => s.Title.StartsWith("This turn", StringComparison.Ordinal)).SelectMany(s => s.Lines).Where(l => l.Tone != Tone.Dim).Select(l => l.Text).ToList();
            if (ideas.Count > 0) p.Children.Add(Note("HearthStoner's mana options that turn: " + string.Join("   ·   ", ideas)));
            p.Children.Add(Note("This scores mana use, not card strength: sometimes holding a card is the better play."));
            p.Children.Add(NextButton(shell));
            return p;
        }

        // ------------------------------------------------------------------ guess their hand

        /// <summary>
        /// Their hand at the start of one of your turns, face down, with how long they've held each card. You guess
        /// the cards the game revealed later, from their likely deck and the cards they showed.
        /// </summary>
        private static UIElement HandQuestion(AppShell shell, string id, GameRecord g)
        {
            var st = shell.Practice;
            var d = shell.Data;
            var r = ReplayPage.TryGet(g, shell);
            if (r == null) return LoadingReplay();
            var turn = PracticeIds.TurnOf(id);
            if (!r.TurnStarts.TryGetValue(turn, out var fi)) return Skip;
            var f = r.Frames[fi];
            var likely = r.AdviceAt(fi).Where(s => s.Left).SelectMany(s => s.Lines).Where(l => l.CardId != null).Select(l => l.CardId);
            var guesses = PracticeDrills.GuessHand(r, fi, likely.Concat(g.OpponentCardsSeen ?? new List<string>()), PracticeIds.Seed(id));
            if (guesses.Count == 0) return Skip;

            var p = new StackPanel();
            p.Children.Add(Context($"Turn {f.Round} vs {Opponent(g)}, {g.Start:MMM d}. They hold {f.Them.Hand.Count} cards. Guess the {(guesses.Count == 1 ? "marked one" : $"{guesses.Count} marked ones")}."));
            var backs = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            for (var i = 0; i < f.Them.Hand.Count; i++)
            {
                var h = f.Them.Hand[i];
                var asked = guesses.Any(x => x.Slot == i);
                var held = h.Mark == null ? 0 : Math.Max(0, f.Round - Math.Max(1, h.Mark.Turn));
                var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                cell.Children.Add(new Border
                {
                    Width = 56, Height = 78, CornerRadius = new CornerRadius(6), Background = Theme.B(Theme.Rgb(0x3A, 0x2A, 0x5A)),
                    BorderBrush = asked ? Theme.Gold : Theme.B(Theme.Rgb(0x6A, 0x5A, 0x9A)), BorderThickness = new Thickness(asked ? 2.5 : 2),
                    Child = new TextBlock { Text = h.CardId != null ? "✓" : (i + 1).ToString(), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = asked ? Theme.Gold : Theme.Mist, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    ToolTip = h.CardId != null ? d.Cards.NameOf(h.CardId) + " (you could see this one)" : null,
                });
                var t = Theme.Text(held == 0 ? "new" : $"held {held}", 12, Theme.Mist);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Margin = new Thickness(0, 4, 0, 0);
                cell.Children.Add(t);
                backs.Children.Add(cell);
            }
            p.Children.Add(backs);
            foreach (var q in guesses)
            {
                var head = AppShell.ColumnLabel($"Card {q.Slot + 1} · {(q.Held == 0 ? "drawn this turn" : $"held {q.Held} turn{(q.Held == 1 ? "" : "s")}")}");
                head.Margin = new Thickness(0, 4, 0, 6);
                p.Children.Add(head);
                var chips = new WrapPanel();
                for (var c = 0; c < q.Choices.Count; c++)
                {
                    var ci = c;
                    var picked = st.Guesses.TryGetValue(q.Slot, out var gi) && gi == c;
                    bool? mark = !st.Answered ? (bool?)null : q.Choices[c] == q.Answer ? true : picked ? false : (bool?)null;
                    chips.Children.Add(ChoiceChip(d.Cards.NameOf(q.Choices[c]), picked, mark, st.Answered ? null : (Action)(() => { st.Guesses[q.Slot] = ci; shell.Redraw(); })));
                }
                p.Children.Add(chips);
            }
            int Correct() => guesses.Count(q => st.Guesses.TryGetValue(q.Slot, out var gi) && q.Choices[gi] == q.Answer);
            if (!st.Answered)
            {
                var reveal = Theme.Button("Reveal", () => Answer(shell, id, Correct() == guesses.Count));
                reveal.IsEnabled = guesses.All(q => st.Guesses.ContainsKey(q.Slot));
                reveal.Margin = new Thickness(0, 6, 0, 0);
                p.Children.Add(reveal);
                p.Children.Add(Note("The choices come from the deck HearthStoner predicted for them and the cards they showed this game."));
                return p;
            }
            var right = Correct();
            p.Children.Add(ResultBox(st.WasRight, $"{right} of {guesses.Count} right.", st.WasRight ? null : "The green ones are what they were really holding."));
            p.Children.Add(NextButton(shell));
            return p;
        }

        // ------------------------------------------------------------------ secret guessing

        /// <summary>
        /// A secret they played: what happened between playing it and your next turn, and their class's secrets to
        /// choose from. A game's first visit finds its revealed secrets in the replay and queues one question each.
        /// </summary>
        private static UIElement SecretQuestion(AppShell shell, string id, GameRecord g)
        {
            var st = shell.Practice;
            var d = shell.Data;
            var r = ReplayPage.TryGet(g, shell);
            if (r == null) return LoadingReplay();
            var gameId = PracticeIds.Make(PracticeIds.Secret, g);
            var secrets = PracticeDrills.Secrets(r, g.OpponentClass, PracticeIds.Seed(gameId));
            var n = PracticeIds.TurnOf(id);
            if (n < 0)
            {
                // The game, not a question yet: replace it with one question per secret it has.
                st.Queue.RemoveAt(st.Index);
                st.Queue.InsertRange(st.Index, secrets.Select((s, i) => PracticeIds.Make(PracticeIds.Secret, g, i)));
                return Again;
            }
            if (n >= secrets.Count) return Skip;
            var q = secrets[n];
            var played = r.Frames[q.PlayedFrame];
            var ask = r.Frames[q.Frame];

            var p = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            p.Children.Add(Context($"Turn {played.Round} vs {Opponent(g)} ({MetaStats.ClassName(g.OpponentClass)}), {g.Start:MMM d}. They played a secret. It's your turn {ask.Round}: which secret is it?"));
            var events = r.Frames.Where(x => x.Index > q.PlayedFrame && x.Index <= q.Frame && !string.IsNullOrEmpty(x.Action)).Select(x => x.Action).Take(10).ToList();
            var head = AppShell.ColumnLabel("What happened since they played it");
            head.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(head);
            if (events.Count == 0) p.Children.Add(Theme.Text("Nothing yet: your turn comes right after.", 13.5, Theme.Mist));
            foreach (var e in events)
            {
                var t = Theme.Text("•  " + e, 13.5, Theme.Vellum, wrap: true);
                t.Margin = new Thickness(0, 0, 0, 3);
                p.Children.Add(t);
            }
            var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            for (var c = 0; c < q.Choices.Count; c++)
            {
                var ci = c;
                bool? mark = !st.Answered ? (bool?)null : q.Choices[c] == q.Answer ? true : st.Choice == c ? false : (bool?)null;
                chips.Children.Add(ChoiceChip(d.Cards.NameOf(q.Choices[c]), st.Choice == c, mark, st.Answered ? null : (Action)(() => Answer(shell, id, q.Choices[st.Choice = ci] == q.Answer))));
            }
            p.Children.Add(chips);
            if (!st.Answered)
            {
                p.Children.Add(Note("Things that would have triggered a secret and didn't rule it out."));
                return p;
            }
            var rule = Knowledge.Secrets.ById(q.Answer);
            p.Children.Add(ResultBox(st.WasRight, $"It was {d.Cards.NameOf(q.Answer)}.", rule?.Advice));
            p.Children.Add(NextButton(shell));
            return p;
        }

        /// <summary>Returned when the question list changed under the shown position: the page looks at the same position again.</summary>
        private static readonly UIElement Again = new Border();
    }
}
