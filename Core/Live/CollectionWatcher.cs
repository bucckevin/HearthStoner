using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HackF5.UnitySpy.HearthstoneLib;

namespace HearthStoner
{
    /// <summary>
    /// Keeps collection.json and quests.json current for as long as the app runs. At the menu it checks the card count
    /// (a cheap read) each minute and reads the whole collection only when something may have changed: the count
    /// moved, you left pack opening or the collection screen (crafting, disenchanting), or a game just ended
    /// (rewards). Quests are saved whenever they changed. Never reads during a game.
    /// </summary>
    public sealed class CollectionWatcher
    {
        private static readonly TimeSpan BusyScreenPause = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MenuPause = TimeSpan.FromSeconds(60);

        private readonly ILiveMemory _live;
        private readonly Func<bool> _inGame;
        private readonly Func<OwnedCollection> _refreshCollection;
        private readonly Action _saved;
        private readonly Action<string> _say;
        private readonly Action<string> _verboseLog;
        private readonly Dictionary<int, QuestEntry> _questDefinitions = new Dictionary<int, QuestEntry>();
        private int _lastCount = -1;
        private bool _wasInGame;
        private SceneModeEnum? _lastScene;
        private string _questKey;

        /// <summary>Creates the watcher; call <see cref="Loop"/> on a background thread to run it.</summary>
        /// <param name="live">Memory access.</param>
        /// <param name="inGame">True while a game is on (nothing is read then).</param>
        /// <param name="refreshCollection">Reads and saves the whole collection; returns null on failure.</param>
        /// <param name="saved">Called after the collection (not the first time) or the quests were saved.</param>
        /// <param name="say">Status lines ("[collection] saved ..."); must not be null.</param>
        /// <param name="verboseLog">Failed quest reads; null to keep quiet.</param>
        public CollectionWatcher(ILiveMemory live, Func<bool> inGame, Func<OwnedCollection> refreshCollection, Action saved,
            Action<string> say, Action<string> verboseLog)
        {
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _inGame = inGame ?? throw new ArgumentNullException(nameof(inGame));
            _refreshCollection = refreshCollection ?? throw new ArgumentNullException(nameof(refreshCollection));
            _saved = saved ?? (() => { });
            _say = say ?? throw new ArgumentNullException(nameof(say));
            _verboseLog = verboseLog;
        }

        /// <summary>Runs forever; meant for a background thread.</summary>
        public void Loop()
        {
            while (true)
            {
                try { Check(); }
                catch (Exception ex) { _say("[collection] " + ex.Message); }
                // Look more often while you're on the screens where the collection changes.
                Thread.Sleep(OnBusyScreen(_lastScene) ? BusyScreenPause : MenuPause);
            }
        }

        private void Check()
        {
            var inGame = _inGame();
            var gameEnded = _wasInGame && !inGame;
            _wasInGame = inGame;
            if (inGame) return;
            CheckCollection(gameEnded);
            SaveQuests();
        }

        private void CheckCollection(bool gameEnded)
        {
            var now = _live.Use(m => m.IsCollectionReady() ? (Scene: m.GetSceneMode(), Count: m.CollectionSize()) : default);
            if (now.Count <= 0) { _lastScene = null; return; }

            var leftShop = OnBusyScreen(_lastScene) && now.Scene != _lastScene;
            if ((now.Count != _lastCount || leftShop || gameEnded) && _refreshCollection() is OwnedCollection c)
            {
                _say($"[collection] saved ({c.Owned.Count} cards)");
                if (_lastCount >= 0) _saved();
                _lastCount = now.Count;
            }
            _lastScene = now.Scene;
        }

        private void SaveQuests()
        {
            var log = _live.Use(m =>
            {
                // Caught here: an exception inside Use would drop the attachment.
                try { return QuestReader.Read(m, _questDefinitions, _verboseLog); }
                catch (Exception ex) { _verboseLog?.Invoke("[quests] " + ex.Message); return null; }
            });
            if (log == null) return;
            var key = string.Join(",", log.Quests.Select(q => $"{q.Id}:{q.Progress}:{q.Status}")) + "|" + log.Track?.TotalXp + "|" + log.SeasonEnd;
            if (key == _questKey) return;
            log.Save();
            _questKey = key;
            _say($"[quests] saved ({log.Quests.Count} quests)");
            _saved();
        }

        private static bool OnBusyScreen(SceneModeEnum? scene) =>
            scene is SceneModeEnum.PACKOPENING or SceneModeEnum.COLLECTIONMANAGER;
    }
}
