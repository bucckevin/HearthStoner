using System.Collections.Generic;
using System.Windows.Threading;

namespace HearthStoner.Pages
{
    /// <summary>
    /// What the Practice page remembers between redraws: the drill (or the daily review) you're in, its questions,
    /// your answer so far to the shown one, and the session score. Owned by the window (<see cref="AppShell.Practice"/>),
    /// so a second window or the screenshot mode starts fresh.
    /// </summary>
    public sealed class PracticeState
    {
        /// <summary>The drill shown (a <see cref="PracticeIds"/> name), <see cref="Review"/>, or null for the Practice home.</summary>
        public string Drill;
        public const string Review = "review";
        /// <summary>The question ids of this drill or review, and which one is shown.</summary>
        public List<string> Queue = new List<string>();
        public int Index;
        /// <summary>The mulligan drill's opponent class filter (null: any).</summary>
        public string MatchupClass;

        /// <summary>True once the shown question is answered (checked, revealed, locked in).</summary>
        public bool Answered;
        public bool WasRight;
        /// <summary>Mulligan: card index to keep (true) or toss (false); missing means keep.</summary>
        public readonly Dictionary<int, bool> Keep = new Dictionary<int, bool>();
        /// <summary>What would you play: hand positions in the order you picked them.</summary>
        public readonly List<int> PickOrder = new List<int>();
        /// <summary>Guess their hand: hand slot to the chosen option's index.</summary>
        public readonly Dictionary<int, int> Guesses = new Dictionary<int, int>();
        /// <summary>Secret and Discover drills: the option you picked (-1: none yet).</summary>
        public int Choice = -1;

        /// <summary>Session score: questions right, out of questions answered.</summary>
        public int Right, Total;

        /// <summary>Clears the answer to the shown question (moving to another one).</summary>
        public void ResetAnswer()
        {
            Answered = false;
            WasRight = false;
            Keep.Clear();
            PickOrder.Clear();
            Guesses.Clear();
            Choice = -1;
        }
    }

    /// <summary>
    /// What the Replays page remembers between redraws: the auto-play timer and the last frame drawn (so a one-step
    /// move animates). Owned by the window (<see cref="AppShell.ReplayView"/>).
    /// </summary>
    public sealed class ReplayViewState
    {
        /// <summary>Steps through the replay while playing; null until Play was first pressed.</summary>
        public DispatcherTimer Player;
        /// <summary>The game (its log file) and frame drawn last; -1 before the first draw.</summary>
        public string ShownGame;
        public int ShownFrame = -1;

        /// <summary>True while auto-play runs.</summary>
        public bool Playing => Player != null && Player.IsEnabled;
    }
}
