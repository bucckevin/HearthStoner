using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HearthStoner.Game
{
    public sealed partial class GameTracker
    {

        private static readonly Regex PowerLine = new Regex(@"GameState\.DebugPrintPower\(\) -\s*(.*)$");

        private static readonly Regex GameLine = new Regex(@"GameState\.DebugPrintGame\(\) - PlayerID=(\d+), PlayerName=(.+)$");


        private static readonly Regex FullEntity = new Regex(@"^FULL_ENTITY - (?:Creating|Updating) (?:ID=(\d+)|(\[.*?\])) CardID=(\S*)");

        private static readonly Regex ShowEntity = new Regex(@"^SHOW_ENTITY - Updating Entity=(.+?) CardID=(\S+)");

        private static readonly Regex ChangeEntity = new Regex(@"^CHANGE_ENTITY - Updating Entity=(.+?) CardID=(\S+)");

        private static readonly Regex HideEntity = new Regex(@"^HIDE_ENTITY - Entity=(.+?) tag=(\w+) value=(\S+)");

        private static readonly Regex TagChange = new Regex(@"^TAG_CHANGE Entity=(.+?) tag=(\w+) value=(\S+)");

        private static readonly Regex BlockTag = new Regex(@"^tag=(\w+) value=(\S+)");

        private static readonly Regex PlayerEnt = new Regex(@"^Player EntityID=(\d+) PlayerID=(\d+)");

        private static readonly Regex GameEnt = new Regex(@"^GameEntity EntityID=(\d+)");

        private static readonly Regex BlockStart = new Regex(@"^BLOCK_START BlockType=(\w+) Entity=(.+?) EffectCardId=.*?(?: Target=(.+?)(?: SubOption=|$)|$)");

        private static readonly Regex BracketId = new Regex(@"\bid=(\d+)");


        private static readonly Regex ChoicesHead = new Regex(@"GameState\.DebugPrintEntityChoices\(\) - id=(\d+) Player=(.+?) TaskList=\S* ChoiceType=(\w+)");

        private static readonly Regex ChoicesSource = new Regex(@"GameState\.DebugPrintEntityChoices\(\) -\s+Source=(.+?)\s*$");

        private static readonly Regex ChoicesEntity = new Regex(@"GameState\.DebugPrintEntityChoices\(\) -\s+Entities\[\d+\]=(.+?)\s*$");

        private static readonly Regex ChosenHead = new Regex(@"GameState\.DebugPrintEntitiesChosen\(\) - id=(\d+) Player=(.+?) EntitiesCount=(\d+)");

        private static readonly Regex ChosenEntity = new Regex(@"GameState\.DebugPrintEntitiesChosen\(\) -\s+Entities\[\d+\]=(.+?)\s*$");

        private static readonly Regex OptionsHead = new Regex(@"GameState\.DebugPrintOptions\(\) - id=(\d+)");

        private static readonly Regex OptionLine = new Regex(@"GameState\.DebugPrintOptions\(\) -\s+option \d+ type=(\w+) mainEntity=(.*?) error=(\w+)");

        private static readonly Regex BracketCardId = new Regex(@"\bcardId=(\S*)");

        private static readonly Regex BracketPlayer = new Regex(@"\bplayer=(\d+)");


        // ================================================================== log parsing

        /// <summary>Time of day of the last line fed (from its "D 01:20:22.82..." prefix).</summary>
        public TimeSpan LastLineTime { get; private set; }


        public void Feed(string line)
        {
            if (line.Length > 18 && line[0] == 'D' && line[1] == ' ' && TimeSpan.TryParse(line.Substring(2, 8), out var lt)) LastLineTime = lt;
            if (line.Contains("DebugPrintGame() - BuildNumber="))
            {
                var i = line.IndexOf("BuildNumber=", StringComparison.Ordinal);
                BuildNumber = line.Substring(i + 12).Trim();
                return;
            }
            // The mode and format, printed right after CREATE_GAME (see GameModes).
            if (line.Contains("DebugPrintGame() - GameType=")) { GameType = line.Substring(line.IndexOf("GameType=", StringComparison.Ordinal) + 9).Trim(); return; }
            if (line.Contains("DebugPrintGame() - FormatType=")) { FormatType = line.Substring(line.IndexOf("FormatType=", StringComparison.Ordinal) + 11).Trim(); return; }
            var g = GameLine.Match(line);
            if (g.Success)
            {
                var pid = int.Parse(g.Groups[1].Value);
                var name = g.Groups[2].Value.Trim();
                PlayerNames[pid] = name;
                if (!IsPlaceholderName(name)) _nameToPlayerId[name] = pid;
                TryIdentifyFriendlyByName();
                return;
            }

            if (line.Contains("GameState.DebugPrintEntityChoices") || line.Contains("GameState.DebugPrintEntitiesChosen") ||
                line.Contains("GameState.DebugPrintOptions"))
            {
                FeedChoiceOrOption(line);
                return;
            }

            var m = PowerLine.Match(line);
            if (!m.Success) return;
            var p = m.Groups[1].Value.Trim();
            if (p.Length == 0) return;

            if (!BlockTag.IsMatch(p)) FlushShow();

            if (p == "CREATE_GAME") { StartGame(); return; }

            Match x;
            if ((x = BlockTag.Match(p)).Success)
            {
                // A brand-new entity's tags are its starting values; tags under SHOW_ENTITY/CHANGE_ENTITY are real changes.
                if (_current != null) SetTag(_current, x.Groups[1].Value, x.Groups[2].Value, initial: ReferenceEquals(_current, _newEntity));
                return;
            }
            if ((x = GameEnt.Match(p)).Success) { _gameEntityId = int.Parse(x.Groups[1].Value); _current = GetOrCreate(_gameEntityId); _newEntity = _current; return; }
            if ((x = PlayerEnt.Match(p)).Success)
            {
                var eid = int.Parse(x.Groups[1].Value);
                var pid = int.Parse(x.Groups[2].Value);
                _playerEntityToPlayerId[eid] = pid;
                _playerIdToEntity[pid] = eid;
                _current = GetOrCreate(eid);
                _newEntity = _current;
                return;
            }
            if ((x = FullEntity.Match(p)).Success)
            {
                var id = x.Groups[1].Success ? int.Parse(x.Groups[1].Value) : ResolveId(x.Groups[2].Value);
                var isNew = !Entities.ContainsKey(id);
                _current = GetOrCreate(id);
                if (x.Groups[3].Value.Length > 0) SetCardId(_current, x.Groups[3].Value);
                if (isNew) _newEntity = _current;
                return;
            }
            if ((x = ShowEntity.Match(p)).Success)
            {
                _current = GetOrCreate(ResolveId(x.Groups[1].Value));
                SetCardId(_current, x.Groups[2].Value);
                _pendingShow = _current;
                return;
            }
            if ((x = ChangeEntity.Match(p)).Success)
            {
                _current = GetOrCreate(ResolveId(x.Groups[1].Value));
                SetCardId(_current, x.Groups[2].Value);
                return;
            }
            if ((x = HideEntity.Match(p)).Success)
            {
                var e = GetOrCreate(ResolveId(x.Groups[1].Value));
                SetTag(e, x.Groups[2].Value, x.Groups[3].Value, initial: false);
                _current = null;
                return;
            }

            FinishNewEntity();

            if ((x = TagChange.Match(p)).Success)
            {
                _current = null;
                HandleTagChange(x.Groups[1].Value, x.Groups[2].Value, x.Groups[3].Value);
                return;
            }
            if ((x = BlockStart.Match(p)).Success)
            {
                _current = null;
                var target = x.Groups[3].Success ? ResolveId(x.Groups[3].Value) : 0;
                OnBlockStart(x.Groups[1].Value, ResolveId(x.Groups[2].Value), target);
                return;
            }
            if (p.StartsWith("BLOCK_END"))
            {
                _current = null;
                if (_blocks.Count > 0) _blocks.RemoveAt(_blocks.Count - 1);
                return;
            }
            _current = null;
        }


        // A FULL_ENTITY's tags arrive on the following lines; once they're done we know its zone/controller/creator.
        private Entity _newEntity;

        private void FinishNewEntity()
        {
            var e = _newEntity;
            _newEntity = null;
            if (e == null || !InGame) return;
            e.ZoneTurn = Turn;
            var zone = e.Zone;
            var created = e.Int("CREATOR") != 0 || e.Int("CREATOR_DBID") != 0;
            if (InSetup && zone == "HAND" && IsCardEntity(e) && (_cards.IsCoin(e.CardId) || string.IsNullOrEmpty(e.CardId) || created))
                e.SetupCoin = true;
            else if (InSetup && !created && (zone == "DECK" || zone == "HAND") && IsCardEntity(e) && !_cards.IsCoin(e.CardId))
            {
                e.StartedInDeck = true;
                e.StartController = e.Controller;
            }
            if (zone == "SECRET" && e.Controller == OpponentPlayerId && e.Flag("SECRET")) AddOpponentSecret(e);
            // The client is only told card ids for its own hand at game start.
            if (FriendlyPlayerId == 0 && InSetup && zone == "HAND" && !string.IsNullOrEmpty(e.CardId) && e.Controller != 0 && !created && !_cards.IsCoin(e.CardId))
                SetFriendly(e.Controller);
        }


        private void FlushShow()
        {
            FinishNewEntity();
            if (_pendingShow == null) return;
            var e = _pendingShow;
            _pendingShow = null;
            if (FriendlyPlayerId == 0 && e.Zone == "HAND" && e.CardType != "ENCHANTMENT" && e.Controller != 0)
                SetFriendly(e.Controller);
        }


        // ================================================================== choices and options

        private void FeedChoiceOrOption(string line)
        {
            Match x;
            if ((x = ChoicesHead.Match(line)).Success)
            {
                var name = x.Groups[2].Value.Trim();
                _readingChoice = new Choice
                {
                    Id = int.Parse(x.Groups[1].Value),
                    PlayerId = ResolvePlayerByName(name),
                    Type = x.Groups[3].Value,
                    Turn = Turn,
                };
                Choices.Add(_readingChoice);
                _readingChosen = null;
                return;
            }
            if (_readingChoice != null && (x = ChoicesSource.Match(line)).Success)
            {
                var src = x.Groups[1].Value;
                _readingChoice.SourceEntity = src == "GameEntity" ? _gameEntityId : ResolveId(src);
                _readingChoice.SourceCardId = BracketCardId.Match(src) is var cm && cm.Success ? cm.Groups[1].Value : EntityCardId(_readingChoice.SourceEntity);
                return;
            }
            if (_readingChoice != null && (x = ChoicesEntity.Match(line)).Success)
            {
                var ent = x.Groups[1].Value;
                var id = ResolveId(ent);
                var cid = BracketCardId.Match(ent);
                _readingChoice.Entities.Add(id);
                _readingChoice.CardIds.Add(cid.Success && cid.Groups[1].Value.Length > 0 ? cid.Groups[1].Value : EntityCardId(id));
                // The mulligan choice tells us which player each name belongs to.
                var pl = BracketPlayer.Match(ent);
                if (pl.Success && _readingChoice.PlayerId == 0) _readingChoice.PlayerId = int.Parse(pl.Groups[1].Value);
                return;
            }
            if ((x = ChosenHead.Match(line)).Success)
            {
                var id = int.Parse(x.Groups[1].Value);
                _readingChosen = Choices.LastOrDefault(c => c.Id == id);
                if (_readingChosen != null) { _readingChosen.Resolved = true; _readingChosen.Chosen.Clear(); }
                _readingChoice = null;
                Changed?.Invoke();
                return;
            }
            if (_readingChosen != null && (x = ChosenEntity.Match(line)).Success)
            {
                _readingChosen.Chosen.Add(ResolveId(x.Groups[1].Value));
                return;
            }
            if (OptionsHead.IsMatch(line))
            {
                _optionsFresh = true;
                _readingChoice = null;
                return;
            }
            if ((x = OptionLine.Match(line)).Success)
            {
                if (_optionsFresh) { Playable.Clear(); UsableNow.Clear(); _optionsFresh = false; }
                if (x.Groups[1].Value != "POWER" || x.Groups[3].Value != "NONE") return;
                var id = ResolveId(x.Groups[2].Value);
                if (id == 0 || !Entities.TryGetValue(id, out var e) || e.Controller != FriendlyPlayerId) return;
                UsableNow.Add(id);
                if (e.Zone == "HAND") Playable.Add(id);
                Changed?.Invoke();
            }
        }
    }
}
