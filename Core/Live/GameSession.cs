#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HackF5.UnitySpy.HearthstoneLib;

namespace HearthStoner.Live
{
    /// <summary>
    /// The live pipeline while Hearthstone runs: follows Power.log, rebuilds the game (<see cref="GameTracker"/>),
    /// records it (<see cref="GameRecorder"/>), keeps the advice current (<see cref="Advisor"/>), finds your deck list,
    /// reads the match info from memory, and saves each finished game with its log. It prints nothing: the console,
    /// overlay and app listen to its events.
    /// </summary>
    public sealed class GameSession : IDisposable
    {
        private const int MatchInfoTries = 30;
        private static readonly TimeSpan MatchInfoPause = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SettleBeforeSave = TimeSpan.FromMilliseconds(1500);

        private readonly Func<string, string, MetaStats> _loadMeta;
        private readonly string? _rankOverride;
        private readonly string? _periodOverride;
        private readonly Action<string> _log;
        private readonly Action<string> _warn;
        private readonly DeckLoader _deckLoader;
        private bool _buildChecked;

        /// <summary>The Hearthstone install folder.</summary>
        public string InstallDir { get; }
        /// <summary>Your settings.</summary>
        public Settings Settings { get; }
        /// <summary>The card database.</summary>
        public CardDb Cards { get; }
        /// <summary>Memory access, shared with whoever else reads the game.</summary>
        public ILiveMemory Live { get; }
        /// <summary>The game as rebuilt from Power.log. Lock <see cref="GameTracker.Sync"/> to read it from other threads.</summary>
        public GameTracker Tracker { get; }
        /// <summary>The advice for the current game.</summary>
        public Advisor Advisor { get; }
        /// <summary>Records the current game.</summary>
        public GameRecorder Recorder { get; }
        /// <summary>Follows the newest Power.log.</summary>
        public PowerLogTailer Tailer { get; }

        /// <summary>A game event, raised on the log thread with the tracker lock held, after the recorder saw it.</summary>
        public event Action<GameEvent>? EventSeen;
        /// <summary>The match info (mode, both players' ranks) once it shows up in memory, raised on a worker thread.</summary>
        public event Action<IMatchInfo>? MatchFound;
        /// <summary>A finished game was saved: the record and the file it went to. Raised on a worker thread.</summary>
        public event Action<GameRecord, string>? GameSaved;

        /// <summary>Wires up the pipeline. Nothing runs until <see cref="Start"/> and <see cref="Run"/>.</summary>
        /// <param name="installDir">The Hearthstone install folder; must exist.</param>
        /// <param name="settings">Your settings; saved when your BattleTag changes.</param>
        /// <param name="cards">The card database.</param>
        /// <param name="meta">The meta stats to start with.</param>
        /// <param name="live">Memory access; disposed with the session.</param>
        /// <param name="loadMeta">Loads meta stats for (rank bracket, period); used when the "auto" bracket follows your rank.</param>
        /// <param name="rankOverride">The --rank option, or null to follow the settings.</param>
        /// <param name="periodOverride">The --period option, or null to follow the settings.</param>
        /// <param name="say">Messages for you (your deck was found ...).</param>
        /// <param name="log">Diagnostics.</param>
        /// <param name="warn">Warnings that need your attention (a new Hearthstone build).</param>
        public GameSession(string installDir, Settings settings, CardDb cards, MetaStats meta, ILiveMemory live,
            Func<string, string, MetaStats> loadMeta, string? rankOverride, string? periodOverride,
            Action<string> say, Action<string> log, Action<string> warn)
        {
            if (string.IsNullOrEmpty(installDir)) throw new ArgumentException("The Hearthstone install folder is required.", nameof(installDir));
            InstallDir = installDir;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Cards = cards ?? throw new ArgumentNullException(nameof(cards));
            Live = live ?? throw new ArgumentNullException(nameof(live));
            _loadMeta = loadMeta ?? throw new ArgumentNullException(nameof(loadMeta));
            _rankOverride = rankOverride;
            _periodOverride = periodOverride;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _warn = warn ?? throw new ArgumentNullException(nameof(warn));
            if (say == null) throw new ArgumentNullException(nameof(say));

            Tracker = new GameTracker(cards, OnEvent);
            Advisor = new Advisor(Tracker, cards, meta, settings);
            Recorder = new GameRecorder(Tracker, Advisor, cards);
            Tracker.Changed += Recorder.OnChanged;
            Advisor.LastGame = GameRecord.LoadAll().LastOrDefault();

            // Your deck list: from Decks.log (what you queued with), falling back to memory. Retried in the
            // background until it works, which also covers starting HearthStoner in the middle of a game.
            Tailer = new PowerLogTailer(installDir, Tracker);
            _deckLoader = new DeckLoader(Tracker, Advisor, cards, live, () => Tailer.CurrentPath, say, log);
        }

        /// <summary>
        /// Starts the deck finder and reads your BattleTag (the most reliable way to tell which player you are).
        /// </summary>
        /// <returns>Your BattleTag, or null when Hearthstone isn't running or not logged in yet.</returns>
        public string? Start()
        {
            new Thread(_deckLoader.Loop) { IsBackground = true, Name = "deck loader" }.Start();
            var tag = Live.Use(m => m.GetAccountInfo()?.BattleTag);
            if (string.IsNullOrEmpty(tag)) return null;
            Tracker.FriendlyNameHint = tag;
            if (Settings.LastBattleTag != tag)
            {
                Settings.LastBattleTag = tag;
                Settings.Save();
            }
            return tag;
        }

        /// <summary>Follows Power.log until <paramref name="cancel"/> fires. Blocks.</summary>
        /// <param name="cancel">Stops following.</param>
        /// <param name="status">Status lines from the log follower (waiting for Hearthstone, switched log ...).</param>
        public void Run(CancellationToken cancel, Action<string> status) => Tailer.Run(cancel, status);

        /// <summary>Drops the memory attachment.</summary>
        public void Dispose() => Live.Dispose();

        // Runs on the log thread while the tracker lock is held, so reading tracker state here is safe.
        private void OnEvent(GameEvent ev)
        {
            Recorder?.OnEvent(ev);
            EventSeen?.Invoke(ev);
            switch (ev.Kind)
            {
                case GameEventKind.GameStart:
                    Task.Run(ReadMatchInfo);
                    _deckLoader?.Reset();
                    break;
                case GameEventKind.Turn:
                    CheckBuildOnce();
                    break;
                case GameEventKind.GameEnd:
                    var record = Recorder?.Current;
                    if (record == null) break;
                    Advisor.LastGame = record;
                    Task.Run(() => SaveGame(record));
                    break;
            }
        }

        private void CheckBuildOnce()
        {
            if (_buildChecked || Tracker.BuildNumber == null) return;
            _buildChecked = true;
            var msg = UpdateCheck.CheckHearthstoneBuild(Tracker.BuildNumber);
            if (msg != null) _warn(msg);
        }

        /// <summary>Waits for the match info to show up in memory, announces it and stores both ranks with the record.</summary>
        private void ReadMatchInfo()
        {
            var match = WaitForMatchInfo();
            if (match == null) return;
            MatchFound?.Invoke(match);
            var me = Ranks.Of(match.LocalPlayer, match.FormatType);
            var them = Ranks.Of(match.OpposingPlayer, match.FormatType);
            lock (Tracker.Sync)
                Recorder?.SetMatchInfo(match.GameType.ToString(), match.FormatType.ToString(), me == null ? null : Ranks.Name(me),
                    me?.StarLevel ?? 0, me?.LegendRank ?? 0, match.OpposingPlayer?.Name, me?.EarnedStars ?? 0, me?.StarsPerWin ?? 0, me?.SeasonId ?? 0,
                    them?.StarLevel ?? 0, them?.LegendRank ?? 0);
        }

        private IMatchInfo? WaitForMatchInfo()
        {
            for (var i = 0; i < MatchInfoTries; i++)
            {
                var m = Live.Use(x => x.GetMatchInfo());
                if (m?.OpposingPlayer != null && !string.IsNullOrEmpty(m.OpposingPlayer.Name)) return m;
                Thread.Sleep(MatchInfoPause);
            }
            return null;
        }

        /// <summary>Keeps the game's part of Power.log with its record, saves it, and follows your rank with the "auto" meta bracket.</summary>
        private void SaveGame(GameRecord record)
        {
            Thread.Sleep(SettleBeforeSave);   // let the match info and result settle
            try
            {
                AttachLog(record);
                string path;
                lock (Tracker.Sync) path = record.Save();
                GameSaved?.Invoke(record, path);
                FollowRank();
            }
            catch (Exception ex) { _log("[record] Couldn't save the game: " + ex.Message); }
        }

        private void AttachLog(GameRecord record)
        {
            try
            {
                var logPath = Tailer.CurrentPath;
                var slice = logPath == null ? null : GameLog.LastGame(logPath);
                if (slice != null && slice.Complete) GameLog.Attach(record, slice);
            }
            catch (Exception ex) { _log("[record] Couldn't keep the game log: " + ex.Message); }
        }

        private void FollowRank()
        {
            if (_rankOverride != null || !string.Equals(Settings.MetaRank, "auto", StringComparison.OrdinalIgnoreCase)) return;
            var bracket = Ranks.MetaBracket(Settings);
            if (bracket != Advisor.Meta?.Rank) Advisor.Meta = _loadMeta(bracket, _periodOverride ?? Settings.MetaPeriod);
        }
    }
}
