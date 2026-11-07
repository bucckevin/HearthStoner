using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HearthStoner
{
    public sealed partial class GameTracker
    {

        private void SetFriendly(int playerId)
        {
            if (FriendlyPlayerId != 0) return;
            FriendlyPlayerId = playerId;
            Emit(GameEventKind.Info, true, null, $"You are player {FriendlyPlayerId} ({NameOf(FriendlyPlayerId)})");
        }


        private void TryIdentifyFriendlyByName()
        {
            if (FriendlyPlayerId != 0 || string.IsNullOrEmpty(FriendlyNameHint)) return;
            var hint = FriendlyNameHint.Split('#')[0];
            foreach (var kv in PlayerNames)
                if (kv.Value.Split('#')[0].Equals(hint, StringComparison.OrdinalIgnoreCase))
                    SetFriendly(kv.Key);
        }


        /// <summary>A player's name without the BattleTag number ("Musume#1455" -> "Musume"), or null while it's
        /// still hidden (the opponent shows as "UNKNOWN HUMAN PLAYER" until the game reveals them).</summary>
        public string DisplayName(int playerId) =>
            PlayerNames.TryGetValue(playerId, out var n) && !IsPlaceholderName(n) ? n.Split('#')[0] : null;


        private static bool IsPlaceholderName(string name) =>
            string.IsNullOrEmpty(name) || name.StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase);


        /// <summary>Player entities are referred to by name ("Musume#1455") in TAG_CHANGE lines.</summary>
        private int ResolvePlayerByName(string name)
        {
            if (_nameToPlayerId.TryGetValue(name, out var pid)) return pid;
            var guess = GuessPlayerIdForName(name);
            if (!string.IsNullOrEmpty(FriendlyNameHint) && name.Split('#')[0].Equals(FriendlyNameHint.Split('#')[0], StringComparison.OrdinalIgnoreCase))
            {
                if (FriendlyPlayerId == 0 && guess != 0) SetFriendly(guess);
                if (FriendlyPlayerId != 0) { _nameToPlayerId[name] = FriendlyPlayerId; return FriendlyPlayerId; }
            }
            if (guess != 0) _nameToPlayerId[name] = guess;
            return guess;
        }


        /// <summary>Names that couldn't be matched to a player (for the self-test).</summary>
        public int UnresolvedNameLookups { get; private set; }

        /// <summary>A few of those names, to say which ones in the self-test.</summary>
        public List<string> UnresolvedNames { get; } = new List<string>();


        private int GuessPlayerIdForName(string name)
        {
            // Exact match with the names from DebugPrintGame.
            foreach (var kv in PlayerNames)
                if (string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase)) return kv.Key;

            // The one player we only know by a placeholder ("UNKNOWN HUMAN PLAYER") or not at all. The opponent is
            // logged under a placeholder until their name is revealed, then under their real BattleTag.
            var real = new HashSet<int>(_nameToPlayerId.Where(kv => !IsPlaceholderName(kv.Key)).Select(kv => kv.Value));
            var unnamed = _playerIdToEntity.Keys.Where(id => !real.Contains(id)).ToList();
            if (unnamed.Count == 1)
            {
                if (!IsPlaceholderName(name)) PlayerNames[unnamed[0]] = name;
                return unnamed[0];
            }
            // Not you: then it's the opponent. (Against the AI the opponent is listed as "The Innkeeper" but logged
            // under its hero's name, e.g. "Mal'Ganis".)
            var you = !string.IsNullOrEmpty(FriendlyNameHint) ? FriendlyNameHint : PlayerNames.TryGetValue(FriendlyPlayerId, out var yn) ? yn : null;
            if (FriendlyPlayerId != 0 && OpponentPlayerId != 0 && !IsPlaceholderName(name) && !string.IsNullOrEmpty(you) &&
                !name.Split('#')[0].Equals(you.Split('#')[0], StringComparison.OrdinalIgnoreCase))
                return OpponentPlayerId;
            UnresolvedNameLookups++;
            if (UnresolvedNames.Count < 3 && !UnresolvedNames.Contains(name)) UnresolvedNames.Add(name);
            return 0;
        }
    }
}
