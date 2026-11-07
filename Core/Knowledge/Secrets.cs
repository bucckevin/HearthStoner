using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    /// <summary>What a secret reacts to. Every Standard secret triggers on its owner's opponent's turn (i.e. yours).</summary>
    public enum SecretTrigger
    {
        /// <summary>Your hero or minion attacks the enemy hero.</summary>
        HeroAttacked,
        /// <summary>One of your minions attacks (anything).</summary>
        MinionAttacks,
        /// <summary>Your hero or minion attacks one of their minions.</summary>
        TheirMinionAttacked,
        /// <summary>You cast a spell.</summary>
        SpellCast,
        /// <summary>You play a minion.</summary>
        MinionPlayed,
        /// <summary>You play your third card in a turn.</summary>
        ThirdCardPlayed,
        /// <summary>One of their minions dies on your turn, the turn after they played it.</summary>
        TheirFreshMinionDies,
        /// <summary>Your turn ends.</summary>
        YourTurnEnds,
    }

    public enum SecretCondition
    {
        None,
        /// <summary>Summons a minion, so it can't trigger if their board is full (7 minions).</summary>
        TheyNeedBoardSpace,
        /// <summary>Needs a minion of yours to hit (e.g. "destroy a random enemy minion").</summary>
        YouNeedAMinion,
    }

    public sealed class SecretRule
    {
        public string CardId;
        public string Name;
        public string Class;
        public int Cost;
        public SecretTrigger Trigger;
        public SecretCondition Condition;
        public string Advice;
    }

    /// <summary>
    /// Every secret in Standard (Year of the Scarab, October 2026: 5 Hunter, 6 Mage).
    /// Hand-written because the trigger and the best way to play around each one need judgement.
    /// Update when sets rotate or a new secret is printed.
    /// </summary>
    public static class Secrets
    {
        public static readonly IReadOnlyList<SecretRule> Standard = new List<SecretRule>
        {
            // Hunter
            new SecretRule { CardId = "CORE_EX1_610", Name = "Explosive Trap", Class = "HUNTER", Cost = 2, Trigger = SecretTrigger.HeroAttacked,
                Advice = "Deals 2 to all your characters when you attack their face: don't swing at face with minions that have 2 or less Health." },
            new SecretRule { CardId = "CORE_EX1_611", Name = "Freezing Trap", Class = "HUNTER", Cost = 2, Trigger = SecretTrigger.MinionAttacks,
                Advice = "Bounces the first minion that attacks: attack first with your cheapest minion." },
            new SecretRule { CardId = "CORE_GIL_577", Name = "Rat Trap", Class = "HUNTER", Cost = 2, Trigger = SecretTrigger.ThirdCardPlayed, Condition = SecretCondition.TheyNeedBoardSpace,
                Advice = "Summons a 6/6 after your 3rd card in a turn: play at most 2 cards, or be ready for a 6/6." },
            new SecretRule { CardId = "CORE_ULD_152", Name = "Pressure Plate", Class = "HUNTER", Cost = 2, Trigger = SecretTrigger.SpellCast, Condition = SecretCondition.YouNeedAMinion,
                Advice = "Destroys a random minion of yours after you cast a spell: cast spells while you have only cheap minions, or none." },
            new SecretRule { CardId = "TIME_620", Name = "Untimely Death", Class = "HUNTER", Cost = 2, Trigger = SecretTrigger.TheirFreshMinionDies,
                Advice = "Resummons a minion they played last turn when it dies: kill it in a way that doesn't matter twice (silence/transform), or ignore it." },

            // Mage
            new SecretRule { CardId = "CORE_BAR_812", Name = "Oasis Ally", Class = "MAGE", Cost = 3, Trigger = SecretTrigger.TheirMinionAttacked, Condition = SecretCondition.TheyNeedBoardSpace,
                Advice = "Summons a 3/6 when you attack one of their minions: make your first attack on a minion with something cheap." },
            new SecretRule { CardId = "CORE_EX1_287", Name = "Counterspell", Class = "MAGE", Cost = 3, Trigger = SecretTrigger.SpellCast,
                Advice = "Counters your next spell: bait it with your cheapest spell before the important one." },
            new SecretRule { CardId = "CORE_EX1_289", Name = "Ice Barrier", Class = "MAGE", Cost = 3, Trigger = SecretTrigger.HeroAttacked,
                Advice = "Gives 8 Armor when you attack their face: count 8 extra Health for lethal, and pop it with a small attacker first." },
            new SecretRule { CardId = "CORE_LOOT_101", Name = "Explosive Runes", Class = "MAGE", Cost = 3, Trigger = SecretTrigger.MinionPlayed,
                Advice = "Deals 6 to the next minion you play (excess hits your face): play a cheap minion first." },
            new SecretRule { CardId = "END_024", Name = "Flames of Infinity", Class = "MAGE", Cost = 3, Trigger = SecretTrigger.YourTurnEnds, Condition = SecretCondition.YouNeedAMinion,
                Advice = "Kills your highest-Health minion when your turn ends: trade it away this turn or keep a decoy with more Health." },
            new SecretRule { CardId = "JAIL_315", Name = "Mystic Misdirection", Class = "MAGE", Cost = 3, Trigger = SecretTrigger.MinionAttacks,
                Advice = "Turns the first minion that attacks into a 1/1 Sheep: attack first with your weakest minion." },
        };

        public static IEnumerable<SecretRule> ForClass(string heroClass) =>
            Standard.Where(s => string.Equals(s.Class, heroClass, System.StringComparison.OrdinalIgnoreCase));

        public static SecretRule ById(string id) => Standard.FirstOrDefault(s => s.CardId == id);
    }
}
