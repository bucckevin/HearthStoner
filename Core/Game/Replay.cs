using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner.Game
{
    /// <summary>
    /// A recorded game turned into a sequence of board states ("frames") you can step through, made by feeding the
    /// game's log through the same tracker the overlay uses. A frame is taken after every top-level action (a card
    /// played, an attack, a hero power, the start of a turn). The advice HearthStoner showed is kept for each turn.
    /// Only information you could see during the game is in the frames; <see cref="FinalCardIds"/> holds what was
    /// revealed later, for the "show hidden cards" option.
    /// </summary>
    public sealed class Replay
    {
        public sealed class Minion
        {
            public int EntityId;
            public string CardId;
            public int Attack, Health, MaxHealth;
            public bool Taunt, DivineShield, Frozen, Stealth, Poisonous, Lifesteal, Windfury, Reborn, Rush, Charge, Dormant;
            /// <summary>Has a deathrattle / a trigger (the game's skull and lightning icons); can't attack right now.</summary>
            public bool Deathrattle, Trigger, Exhausted;
        }

        public sealed class HandCard
        {
            public int EntityId;
            /// <summary>Null when you couldn't see it (the opponent's hand).</summary>
            public string CardId;
            public int Cost;
            /// <summary>Opponent cards: the same marks as the overlay's (turn drawn, mulligan, created...).</summary>
            public HandMark Mark;
        }

        public sealed class Side
        {
            public int PlayerId;
            public int HeroEntityId;
            public bool HeroExhausted;
            public string Name;
            public string HeroCardId, HeroPowerCardId;
            public bool HeroPowerUsed;
            public int Health, Armor, HeroAttack;
            public string WeaponCardId;
            public int WeaponAttack, WeaponDurability;
            public int Mana, Crystals, Overload;
            public int DeckCount;
            public List<Minion> Board = new List<Minion>();
            public List<HandCard> Hand = new List<HandCard>();
            /// <summary>Card ids of secrets in play ("" when hidden).</summary>
            public List<string> Secrets = new List<string>();
        }

        public sealed class Frame
        {
            public int Index;
            /// <summary>Game turn (1, 2, ...) and the round shown to players ((turn + 1) / 2).</summary>
            public int Turn;
            public int Round => (Turn + 1) / 2;
            public bool YourTurn;
            public bool Mulligan;
            /// <summary>What just happened, e.g. "Opponent played Fireball".</summary>
            public string Action;
            public List<string> Events = new List<string>();
            public Side You, Them;
            /// <summary>For an attack: the attacker and target entities, and whether the target was a hero.</summary>
            public int AttackerId, TargetId;
            public bool TargetIsHero;
            /// <summary>The card played in this action (null if none or hidden), who played it, and its target.</summary>
            public string PlayedCardId;
            public bool PlayedByYou;
            public int PlayTargetId;
        }

        public List<Frame> Frames = new List<Frame>();
        /// <summary>Frame index where each game turn starts.</summary>
        public Dictionary<int, int> TurnStarts = new Dictionary<int, int>();
        /// <summary>The overlay's advice at the start of each game turn.</summary>
        public Dictionary<int, List<AdviceSection>> AdviceByTurn = new Dictionary<int, List<AdviceSection>>();
        /// <summary>Every card id the game revealed by the end, by entity (for showing the opponent's hand).</summary>
        public Dictionary<int, string> FinalCardIds = new Dictionary<int, string>();
        public string Result;
        public int Turns;

        /// <summary>The advice in force at a frame: from the start of its turn (or the latest earlier turn).</summary>
        public List<AdviceSection> AdviceAt(int frame)
        {
            if (frame < 0 || frame >= Frames.Count) return new List<AdviceSection>();
            for (var t = Frames[frame].Turn; t >= 0; t--)
                if (AdviceByTurn.TryGetValue(t, out var a)) return a;
            return new List<AdviceSection>();
        }

        /// <summary>Builds the frames from a game's log lines. <paramref name="deck"/> is your deck list (for the advice).</summary>
        public static Replay Build(IEnumerable<string> lines, CardDb cards, MetaStats meta, List<string> deck = null, string deckName = null, string me = null)
        {
            var r = new Replay();
            var pending = new List<string>();
            GameTracker t = null;
            Advisor advisor = null;
            var started = false;
            t = new GameTracker(cards, ev =>
            {
                if (ev.Kind == GameEventKind.GameStart)
                {
                    started = true;
                    if (deck != null) { t.FriendlyDeckList = deck.ToList(); t.FriendlyDeckName = deckName; }
                }
                if (ev.Kind == GameEventKind.Turn)
                {
                    try { r.AdviceByTurn[t.Turn] = advisor.Compute().Sections; } catch (Exception swallowed) { Diag.Ignored(swallowed, "Replay"); }
                    r.TurnStarts[t.Turn] = r.Frames.Count;
                }
                if (!string.IsNullOrEmpty(ev.Text) && ev.Kind != GameEventKind.Info) pending.Add(ev.Text);
            });
            if (!string.IsNullOrEmpty(me)) t.FriendlyNameHint = me;
            advisor = new Advisor(t, cards, meta, new Settings());

            (string Type, int Entity, int Target) block = (null, 0, 0);
            var lastPlayed = new Dictionary<bool, int>();   // last card each side played (by entity), for naming plays revealed late
            foreach (var line in lines)
            {
                t.Feed(line);
                var bs = TopBlockStart.Match(line);
                if (bs.Success)
                {
                    block = (bs.Groups[1].Value, IdIn(bs.Groups[2].Value), IdIn(bs.Groups[3].Value));
                    if (block.Type == "PLAY" && t.Entities.TryGetValue(block.Entity, out var pe)) lastPlayed[pe.Controller == t.FriendlyPlayerId] = pe.Id;
                }
                if (!started || t.FriendlyPlayerId == 0) continue;
                if (IsBoundary(line, out var blockType))
                {
                    var f = Snapshot(t, advisor);
                    f.Index = r.Frames.Count;
                    // Plays announced before the card was revealed: name them now.
                    f.Events = pending.Select(ev =>
                    {
                        if (!ev.EndsWith("played an unknown card")) return ev;
                        var mine = ev.StartsWith("You");
                        return lastPlayed.TryGetValue(mine, out var id) && t.Entities.TryGetValue(id, out var pe2) && !string.IsNullOrEmpty(pe2.KnownCardId)
                            ? $"{(mine ? "You" : "Opponent")} played {t.CardName(pe2)}" : ev.Replace("an unknown card", "a card you can't see");
                    }).ToList();
                    // Described when the action is over, so cards revealed during it have their names.
                    f.Action = Capitalize(DescribeBlock(t, cards, block) ?? f.Events.LastOrDefault(e => !e.StartsWith("---")) ?? Describe(blockType));
                    if (block.Type == "PLAY" && t.Entities.TryGetValue(block.Entity, out var played) && played.CardType != "HERO_POWER")
                    {
                        f.PlayedCardId = string.IsNullOrEmpty(played.KnownCardId) ? null : played.KnownCardId;
                        f.PlayedByYou = played.Controller == t.FriendlyPlayerId;
                        f.PlayTargetId = block.Target;
                    }
                    if (block.Type == "ATTACK")
                    {
                        f.AttackerId = block.Entity; f.TargetId = block.Target;
                        f.TargetIsHero = t.Entities.TryGetValue(block.Target, out var te) && te.CardType == "HERO";
                    }
                    block = (null, 0, 0);
                    if (string.IsNullOrEmpty(f.Action))
                        f.Action = r.Frames.Count == 0 || r.Frames[r.Frames.Count - 1].Turn != f.Turn
                            ? (f.Mulligan ? "Mulligan" : $"{(f.YourTurn ? "Your" : "Opponent's")} turn {f.Round}")
                            : "Effects resolve";
                    pending.Clear();
                    // Skip frames that show nothing new (e.g. hidden triggers between actions).
                    if (r.Frames.Count > 0 && f.Events.Count == 0 && Same(r.Frames[r.Frames.Count - 1], f)) continue;
                    r.Frames.Add(f);
                }
            }
            foreach (var e in t.Entities.Values) if (!string.IsNullOrEmpty(e.KnownCardId)) r.FinalCardIds[e.Id] = e.KnownCardId;
            r.Result = t.Result;
            r.Turns = t.Turn;
            return r;
        }

        private static readonly System.Text.RegularExpressions.Regex TopBlockStart = new System.Text.RegularExpressions.Regex(
            @"GameState\.DebugPrintPower\(\) - BLOCK_START BlockType=(\w+) Entity=(.+?) EffectCardId=.*?(?:Target=(.+?)(?: SubOption=|$)|$)");
        private static readonly System.Text.RegularExpressions.Regex IdRe = new System.Text.RegularExpressions.Regex(@"\bid=(\d+)");

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static int IdIn(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var m = IdRe.Match(s);
            if (m.Success) return int.Parse(m.Groups[1].Value);
            return int.TryParse(s.Trim(), out var n) ? n : 0;
        }

        /// <summary>"You played Fireball", "Bloodfen Raptor attacked Opponent's hero", "Opponent used their hero power".</summary>
        private static string DescribeBlock(GameTracker t, CardDb cards, (string Type, int Entity, int Target) b)
        {
            if (b.Type == null || !t.Entities.TryGetValue(b.Entity, out var e)) return null;
            var who = e.Controller == t.FriendlyPlayerId ? "You" : "Opponent";
            string Name(Entity x) => x == null ? "?" : x.CardType == "HERO" ? (x.Controller == t.FriendlyPlayerId ? "your hero" : "the opponent's hero") : t.CardName(x);
            t.Entities.TryGetValue(b.Target, out var target);
            switch (b.Type)
            {
                case "PLAY":
                    if (e.CardType == "HERO_POWER") return $"{who} used {t.CardName(e)}" + (target != null ? $" on {Name(target)}" : "");
                    if (string.IsNullOrEmpty(e.KnownCardId)) return $"{who} played {(e.Flag("SECRET") || e.Zone == "SECRET" ? "a secret" : "a card you can't see")}";
                    return $"{who} played {t.CardName(e)}" + (target != null ? $" on {Name(target)}" : "");
                case "ATTACK":
                    return $"{Name(e)} attacked {Name(target)}";
                case "TRIGGER":
                case "POWER":
                case "DEATHS":
                case "FATIGUE":
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>A top-level BLOCK_END (an action finished) or a change of turn.</summary>
        private static bool IsBoundary(string line, out string blockType)
        {
            blockType = null;
            var i = line.IndexOf("GameState.DebugPrintPower() - ", StringComparison.Ordinal);
            if (i < 0) return false;
            var rest = line.Substring(i + 30);
            if (rest.StartsWith("BLOCK_END")) { blockType = "action"; return true; }
            if (rest.StartsWith("TAG_CHANGE Entity=GameEntity tag=STEP value=MAIN_ACTION")) { blockType = "turn"; return true; }
            if (rest.StartsWith("TAG_CHANGE Entity=GameEntity tag=STEP value=BEGIN_MULLIGAN")) { blockType = "mulligan"; return true; }
            return false;
        }

        private static string Describe(string blockType) => blockType == "mulligan" ? "Mulligan" : blockType == "turn" ? "Turn starts" : "";

        private static bool Same(Frame a, Frame b) =>
            a.Turn == b.Turn && Key(a.You) == Key(b.You) && Key(a.Them) == Key(b.Them);

        private static string Key(Side s) =>
            $"{s.Health}/{s.Armor}/{s.Mana}/{s.WeaponCardId}/{s.WeaponDurability}/{s.DeckCount}|" +
            string.Join(",", s.Board.Select(m => $"{m.EntityId}:{m.Attack}/{m.Health}/{m.DivineShield}/{m.Frozen}/{m.Taunt}")) + "|" +
            string.Join(",", s.Hand.Select(h => h.EntityId + ":" + h.CardId)) + "|" + string.Join(",", s.Secrets);

        private static Frame Snapshot(GameTracker t, Advisor advisor)
        {
            var f = new Frame { Turn = t.Turn, YourTurn = t.IsFriendlyTurn, Mulligan = t.InMulligan };
            f.You = SideOf(t, t.FriendlyPlayerId, null);
            List<HandMark> marks = null;
            try { marks = advisor.OpponentHandMarks(); } catch (Exception swallowed) { Diag.Ignored(swallowed, "Replay"); }
            f.Them = SideOf(t, t.OpponentPlayerId, marks);
            return f;
        }

        private static Side SideOf(GameTracker t, int pid, List<HandMark> marks)
        {
            var s = new Side { PlayerId = pid, Name = t.DisplayName(pid) };
            var hero = t.Hero(pid);
            if (hero != null)
            {
                s.HeroEntityId = hero.Id;
                s.HeroExhausted = hero.Flag("EXHAUSTED");
                s.HeroCardId = hero.KnownCardId;
                s.Health = hero.Health;
                s.Armor = hero.Armor;
                s.HeroAttack = hero.Attack;
            }
            // Not through Zone(): it leaves hero powers out (they aren't cards in hand, deck or play).
            var hp = t.Entities.Values.FirstOrDefault(e => e.Controller == pid && e.Zone == "PLAY" && e.CardType == "HERO_POWER");
            if (hp != null) { s.HeroPowerCardId = hp.KnownCardId; s.HeroPowerUsed = hp.Flag("EXHAUSTED"); }
            var w = t.Weapon(pid);
            if (w != null)
            {
                // Newer clients keep a weapon's durability in HEALTH; older ones in DURABILITY.
                s.WeaponCardId = w.KnownCardId; s.WeaponAttack = w.Attack;
                s.WeaponDurability = Math.Max(0, (w.Int("DURABILITY") > 0 ? w.Int("DURABILITY") : w.Int("HEALTH")) - w.Int("DAMAGE"));
            }
            var p = t.PlayerEntity(pid);
            s.Mana = t.AvailableMana(pid);
            s.Crystals = p?.Int("RESOURCES") ?? 0;
            s.Overload = p?.Int("OVERLOAD_LOCKED") ?? 0;
            s.DeckCount = t.DeckCount(pid);
            foreach (var m in t.Minions(pid))
                s.Board.Add(new Minion
                {
                    EntityId = m.Id, CardId = m.KnownCardId, Attack = m.Attack, Health = m.Health, MaxHealth = m.Int("HEALTH"),
                    Taunt = m.Flag("TAUNT"), DivineShield = m.Flag("DIVINE_SHIELD"), Frozen = m.Flag("FROZEN"), Stealth = m.Flag("STEALTH"),
                    Poisonous = m.Flag("POISONOUS") || m.Flag("VENOMOUS"), Lifesteal = m.Flag("LIFESTEAL"), Windfury = m.Flag("WINDFURY"),
                    Reborn = m.Flag("REBORN"), Rush = m.Flag("RUSH"), Charge = m.Flag("CHARGE"), Dormant = m.Flag("DORMANT"),
                    Deathrattle = m.Flag("DEATHRATTLE"), Trigger = m.Flag("TRIGGER_VISUAL"), Exhausted = m.Flag("EXHAUSTED"),
                });
            var hand = t.Hand(pid);
            for (var i = 0; i < hand.Count; i++)
            {
                var e = hand[i];
                var visible = !string.IsNullOrEmpty(e.CardId) ? e.CardId : null;
                s.Hand.Add(new HandCard
                {
                    EntityId = e.Id, CardId = visible, Cost = e.Int("COST"),
                    Mark = marks?.FirstOrDefault(m => m.Slot == (e.Int("ZONE_POSITION") > 0 ? e.Int("ZONE_POSITION") - 1 : i)),
                });
            }
            foreach (var sec in t.Zone(pid, "SECRET").OrderBy(e => e.Int("ZONE_POSITION")))
                if (sec.CardType != "HERO_POWER") s.Secrets.Add(sec.CardId ?? "");
            return s;
        }
    }
}
