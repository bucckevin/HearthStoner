using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HearthStoner.Game
{
    /// <summary>State at the end of one turn.</summary>
    public sealed class TurnSnapshot
    {
        public int Turn;                 // game turn (1, 2, 3...); yours and theirs alternate
        public bool Yours;
        public int MyHp, MyArmor, TheirHp, TheirArmor;
        public int MyMinions, MyBoardAttack, MyBoardHealth;
        public int TheirMinions, TheirBoardAttack, TheirBoardHealth;
        public int MyHand, TheirHand, MyDeck, TheirDeck;
        public int ManaAvailable;        // crystals you had this turn
        public int ManaUnspent;          // left over at the end of your turn
        public List<string> UnplayedPlayable = new List<string>();   // card ids you could have played but didn't
        public List<string> Played = new List<string>();
        public bool HadLethal;
        /// <summary>HearthStoner's guess at the opponent's archetype at the start of this turn, and how sure it was.</summary>
        public string OpponentGuess;
        public double OpponentGuessProbability;
        public string LethalNote;
        /// <summary>The board when lethal was first available (for 'HearthStoner puzzles').</summary>
        public LethalPuzzle Puzzle;
        public int YourMinionsDied, TheirMinionsDied;
        public string TheirAoe;          // AoE card they played this turn
        public double TheirAoeProbability;
    }

    public sealed class LethalPuzzle
    {
        public int Mana;
        public int TheirHealth;
        public int TheirArmor;
        public List<string> YourMinions = new List<string>();   // "Name 3/2 (ready)" / "(can't attack)"
        public List<string> TheirMinions = new List<string>();
        public List<string> Hand = new List<string>();          // "Name (cost)"
        public string Weapon;
        public string Solution;
    }

    public sealed class MulliganPick
    {
        public string CardId;
        public bool Kept;
        public string Advice;            // KEEP / TOSS / either / null (no data)
        public double Delta;             // win-rate change when kept vs. the deck's baseline
    }

    public sealed class DiscoverPick
    {
        public int Turn;
        public string Source;
        public List<string> Options = new List<string>();
        public string Picked;
        public List<double?> WinRates = new List<double?>();
    }

    public sealed class ReviewNote
    {
        public int Turn;                 // 0 = whole game / mulligan
        public string Kind;              // MissedLethal, UnspentMana, Overextended, Mulligan, Discover, Secret
        public string Text;
        public int Severity;             // 1 minor .. 3 major
        /// <summary>Replay frame the note points at (-1: the start of its turn).</summary>
        public int Frame = -1;
    }

    /// <summary>One game, as saved to %LocalAppData%\HearthStoner\games\*.json.</summary>
    public sealed class GameRecord
    {
        /// <summary>Version 2 (1.4.1): Arena decks, Battlegrounds places, classes behind neutral hero cards. Older
        /// "1" records are repaired from their logs once (<see cref="GameLog.NeedsRepair"/>).</summary>
        public const string CurrentVersion = "2";

        public string Version = CurrentVersion;
        public DateTime Start;
        public DateTime End;
        public string Result;            // WON / LOST / TIED / null (unknown)
        public string GameType;
        public string Format;
        public string Rank;              // your rank when the game started, e.g. "Gold 3"
        public int StarLevel;
        public int LegendRank;
        /// <summary>Stars within the rank (0-2), the star bonus (stars per win, 1 = none) and Hearthstone's ranked
        /// season id when the game started; 0 for games saved before these were recorded.</summary>
        public int Stars;
        public int StarBonus;
        public int SeasonId;

        public string DeckName;
        public string DeckCode;
        public List<string> DeckCards = new List<string>();
        public string MyClass;
        public string MyArchetype;
        public bool WentFirst;

        public string OpponentName;
        public string OpponentClass;
        public string OpponentArchetype;
        public double OpponentArchetypeProbability;
        /// <summary>The opponent's rank as the game shows it (0 when not recorded).</summary>
        public int OpponentStarLevel;
        public int OpponentLegendRank;
        public List<string> OpponentCardsSeen = new List<string>();

        /// <summary>Battlegrounds: where you finished (1-8, 1 = first); 0 in other modes and older games.</summary>
        public int Place;
        /// <summary>Battlegrounds: the hero you played (card id); null in other modes.</summary>
        public string Hero;

        public int Turns;
        /// <summary>Recorded afterwards from a saved Hearthstone log rather than live.</summary>
        public bool Imported;
        /// <summary>This game's part of Power.log (gzip), next to this file in the games folder; null for older games.</summary>
        public string LogFile;
        public List<MulliganPick> Mulligan = new List<MulliganPick>();
        public List<DiscoverPick> Discovers = new List<DiscoverPick>();
        public List<TurnSnapshot> Snapshots = new List<TurnSnapshot>();
        public List<ReviewNote> Review = new List<ReviewNote>();

        [JsonIgnore] public bool Won => Result == "WON";
        [JsonIgnore] public bool Decided => Result == "WON" || Result == "LOST";
        /// <summary>
        /// The deck's name for tables: your saved deck's name, else its archetype, else "Paladin, deck not saved"
        /// (a bare "Paladin" next to "Aura Paladin" read like a deck of its own).
        /// </summary>
        [JsonIgnore] public string DeckLabel => DeckName ?? MyArchetype ?? MetaStats.ClassName(MyClass) + ", deck not saved";
        [JsonIgnore] public TimeSpan Duration => End - Start;

        public static string Dir
        {
            get
            {
                var d = AppPaths.Data("games");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// <summary>File name (without folder) this record saves as.</summary>
        [JsonIgnore] public string FileStem => $"{Start:yyyy-MM-dd_HH-mm-ss}_{San(MyClass)}_vs_{San(OpponentClass)}_{(Result ?? "unknown").ToLowerInvariant()}";

        public string Save()
        {
            var name = FileStem + ".json";
            var path = Path.Combine(Dir, name);
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            return path;
        }

        private static string San(string s) => string.IsNullOrEmpty(s) ? "unknown" : new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        /// <summary>Every saved game, oldest first. Unreadable files are skipped.</summary>
        public static List<GameRecord> LoadAll()
        {
            var list = new List<GameRecord>();
            foreach (var f in Directory.GetFiles(Dir, "*.json"))
            {
                try
                {
                    var r = JsonConvert.DeserializeObject<GameRecord>(File.ReadAllText(f));
                    if (r == null) continue;
                    if (string.IsNullOrEmpty(r.GameType)) Relabel(r, f);
                    // A game without a deck list can't have a deck archetype: one saved by 1.4.1 or older was the
                    // previous game's (a solo game after a ranked one). Cleared once.
                    if (r.MyArchetype != null && r.DeckCards.Count == 0)
                    {
                        r.MyArchetype = null;
                        File.WriteAllText(f, JsonConvert.SerializeObject(r, Formatting.Indented));
                    }
                    list.Add(r);
                }
                catch (Exception swallowed) { Diag.Ignored(swallowed, "GameRecord"); }
            }
            return list.OrderBy(r => r.Start).ToList();
        }

        /// <summary>
        /// Fills in the mode of a game saved without one, from its saved log, and saves it once ("GT_UNKNOWN" when the
        /// log doesn't say). Battlegrounds and Mercenaries games keep only their basics.
        /// </summary>
        private static void Relabel(GameRecord r, string file)
        {
            try
            {
                var (gt, ft) = GameLog.ModeOf(GameLog.Load(r));
                r.GameType = GameModes.Known(gt) ? gt : "GT_UNKNOWN";
                if (string.IsNullOrEmpty(r.Format) || r.Format == "FT_UNKNOWN") r.Format = ft;
                if (!GameModes.IsCardGame(r.GameType)) { r.Snapshots.Clear(); r.Mulligan.Clear(); r.Discovers.Clear(); r.Review.Clear(); }
                File.WriteAllText(file, JsonConvert.SerializeObject(r, Formatting.Indented));
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "GameRecord"); }
        }
    }
}
