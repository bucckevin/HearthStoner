using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HearthStoner.Pages
{
    public static partial class PracticePage
    {
        /// <summary>A Discover from one of your games: pick an option, then see the data's favourite and what you took.</summary>
        private static UIElement DiscoverQuestion(AppShell shell, string id, GameRecord g)
        {
            var st = shell.Practice;
            var d = shell.Data;
            var n = PracticeIds.TurnOf(id);
            var pick = n >= 0 && n < g.Discovers.Count ? g.Discovers[n] : null;
            var best = PracticeDrills.BestOption(pick);
            if (best < 0) return Skip;
            var p = new StackPanel();
            var source = string.IsNullOrEmpty(pick.Source) ? "a card" : d.Cards.NameOf(pick.Source);
            p.Children.Add(Context($"Turn {(pick.Turn + 1) / 2} vs {Opponent(g)}, {g.Start:MMM d}. You played {source} and Discovered. Which would you take?"));
            var row = new WrapPanel();
            for (var i = 0; i < pick.Options.Count; i++)
            {
                var idx = i;
                var chosen = st.Choice == i;
                var outline = st.Answered ? (i == best ? Theme.Win : chosen ? Theme.Amber : null) : chosen ? Theme.Mana : null;
                var card = new StackPanel { Width = 150, Margin = new Thickness(0, 0, 18, 10) };
                card.Children.Add(CardPicture(d, pick.Options[i], 150, 1, outline));
                if (st.Answered)
                {
                    var wr = pick.WinRates.ElementAtOrDefault(i);
                    var label = (wr.HasValue ? $"{AppShell.Pct(wr.Value)} when picked" : "no data") + (i == best ? " · best" : "") + (pick.Options[i] == pick.Picked ? " · your game pick" : "");
                    var t = Theme.Text(label, 13, i == best ? Theme.Win : Theme.Mist, i == best ? FontWeights.SemiBold : FontWeights.Normal, wrap: true);
                    t.TextAlignment = TextAlignment.Center;
                    t.Margin = new Thickness(0, 6, 0, 0);
                    card.Children.Add(t);
                }
                var b = new Border { Child = card, Background = System.Windows.Media.Brushes.Transparent, Cursor = st.Answered ? null : System.Windows.Input.Cursors.Hand, ToolTip = d.Cards.NameOf(pick.Options[i]) };
                if (!st.Answered) b.MouseLeftButtonUp += (s, e) => Answer(shell, id, (st.Choice = idx) == best);
                row.Children.Add(b);
            }
            p.Children.Add(row);
            if (!st.Answered)
            {
                p.Children.Add(Note("Click a card to pick it. The answer is the option with the best win rate when picked, for your archetype."));
                return p;
            }
            var bestName = d.Cards.NameOf(pick.Options[best]);
            var gamePick = string.IsNullOrEmpty(pick.Picked) ? null : d.Cards.NameOf(pick.Picked);
            var body = gamePick == null ? "" : pick.Picked == pick.Options[best] ? $"In the game you took {gamePick} too." : $"In the game you took {gamePick}.";
            p.Children.Add(ResultBox(st.WasRight, st.WasRight ? $"Right: the data favours {bestName}." : $"The data favours {bestName}.", body));
            p.Children.Add(DiscoverRecord(d));
            p.Children.Add(NextButton(shell));
            return p;
        }

        /// <summary>How often you took the data's favourite in your games.</summary>
        private static UIElement DiscoverRecord(AppData d)
        {
            var picks = d.Games.SelectMany(x => x.Discovers).Where(x => PracticeDrills.BestOption(x) >= 0 && !string.IsNullOrEmpty(x.Picked)).ToList();
            var right = picks.Count(x => x.Picked == x.Options[PracticeDrills.BestOption(x)]);
            var t = Theme.Text($"In your games you took the data's favourite {right} of {picks.Count} times.", 13, Theme.Mist);
            t.Margin = new Thickness(0, 12, 0, 0);
            return t;
        }
    }
}
