using System.Collections.Generic;
using System.Windows.Threading;

namespace HearthStoner
{
    /// <summary>
    /// What the Practice page remembers between redraws: which quiz hand and puzzle you're on, your keep/toss picks
    /// and the running score. Owned by the window (<see cref="AppShell.Practice"/>), so a second window or the
    /// screenshot mode starts fresh.
    /// </summary>
    public sealed class PracticeState
    {
        /// <summary>Index of the mulligan quiz hand shown.</summary>
        public int QuizHand;
        /// <summary>Your picks for the shown hand: card index to keep (true) or toss (false). Missing means keep.</summary>
        public readonly Dictionary<int, bool> Keep = new Dictionary<int, bool>();
        /// <summary>True once the shown hand was checked against the data.</summary>
        public bool Checked;
        /// <summary>Quiz score so far: cards judged right, out of cards judged.</summary>
        public int Right, Total;
        /// <summary>Index of the lethal puzzle shown.</summary>
        public int Puzzle;
        /// <summary>True when the shown puzzle's solution is revealed.</summary>
        public bool SolutionShown;
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
