using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HearthStoner.Knowledge
{
    /// <summary>
    /// What a card does, derived automatically from its text and keywords in HearthstoneJSON.
    /// This is a heuristic reading of card text (it handles the common wordings of removal, board
    /// clears, burn, healing, draw and so on), not a rules engine: conditional effects ("if you've
    /// played a Fire spell...") are treated as if the condition is met.
    /// </summary>
    public sealed class CardProfile
    {
        public string CardId;
        public string CleanText = "";

        /// <summary>Single-target damage (spell or battlecry). 0 if none.</summary>
        public int Damage;
        /// <summary>The single-target damage can hit a hero (burn / reach).</summary>
        public bool DamageHitsFace;
        /// <summary>Random-target damage (e.g. "3 damage to a random enemy").</summary>
        public int RandomDamage;
        /// <summary>Damage to all/each enemy (or all) minions. 0 if none.</summary>
        public int AoeDamage;
        public bool AoeHitsFace;
        /// <summary>Destroys all (or a class of) minions.</summary>
        public bool BoardClear;
        public bool Destroy;
        public bool Transform;
        public bool Silence;
        public bool Freeze;
        public bool Bounce;
        public int Heal;
        public int Armor;
        public bool Draws;

        public bool Taunt, Rush, Charge, DivineShield, Stealth, Poisonous, Lifesteal, Windfury, Deathrattle,
                    Reborn, Elusive, SpellPower, Aura, Trigger, Secret, Discover, Battlecry;

        /// <summary>Kills or neutralises one enemy minion.</summary>
        public bool IsSingleRemoval => Damage > 0 || Destroy || Transform || Bounce || RandomDamage > 0;
        public bool IsAoe => AoeDamage > 0 || BoardClear;

        public string Summary()
        {
            var parts = new List<string>();
            if (BoardClear) parts.Add("board clear");
            if (AoeDamage > 0) parts.Add($"{AoeDamage} dmg to all{(AoeHitsFace ? " (incl. face)" : "")}");
            if (Damage > 0) parts.Add($"{Damage} dmg{(DamageHitsFace ? " (can go face)" : " to a minion")}");
            if (RandomDamage > 0) parts.Add($"{RandomDamage} random dmg");
            if (Destroy) parts.Add("destroy");
            if (Transform) parts.Add("transform");
            if (Silence) parts.Add("silence");
            if (Freeze) parts.Add("freeze");
            if (Bounce) parts.Add("bounce");
            if (Heal > 0) parts.Add($"heal {Heal}");
            if (Armor > 0) parts.Add($"{Armor} armor");
            if (Draws) parts.Add("draw");
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------------ text parsing

        private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex Markup = new Regex(@"<[^>]+>|\[x\]", O);
        private static readonly Regex Spaces = new Regex(@"\s+", O);

        private static readonly Regex Aoe = new Regex(@"deal (\d+) damage to (?:all|each)\b[^.]*?(?:minions|enemies|characters|minion|enemy)", O);
        private static readonly Regex AoeFace = new Regex(@"to (?:all|each) (?:enemies|characters|other characters)", O);
        private static readonly Regex Clear = new Regex(@"destroy all (?:other |enemy )?minions", O);
        private static readonly Regex Single = new Regex(@"deal (\d+) damage(?:\.| to (?:a|an|another|target|the|that|it|one)\b(?! random))", O);
        private static readonly Regex SingleFace = new Regex(@"deal \d+ damage(?:\.| to (?:a character|an enemy(?! minion)|the enemy(?! minion)|the enemy hero|your opponent|their hero))", O);
        private static readonly Regex RandomDmg = new Regex(@"deal (\d+) damage (?:to (?:a |an |\d+ )?random|randomly split)", O);
        private static readonly Regex DestroyOne = new Regex(@"destroy (?:a|an|another|target|that|1|one|two|2) (?:random )?(?:enemy )?minions?", O);
        private static readonly Regex TransformRx = new Regex(@"transform (?:a|an|another|an enemy) minion", O);
        private static readonly Regex SilenceRx = new Regex(@"\bsilence (?:a|an|another|all|it)", O);
        private static readonly Regex FreezeRx = new Regex(@"\bfreeze (?:a|an|all|it|two|three|the)", O);
        private static readonly Regex BounceRx = new Regex(@"return (?:a|an|another|an enemy) (?:enemy )?minion to (?:its owner's|your opponent's|their) hand", O);
        private static readonly Regex HealRx = new Regex(@"restore (\d+) health", O);
        private static readonly Regex ArmorRx = new Regex(@"gain (\d+) armor", O);
        private static readonly Regex DrawRx = new Regex(@"\bdraw (?:a|an|\d+|two|three|four) ", O);

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var t = Markup.Replace(text, "").Replace("$", "").Replace("#", "").Replace("\n", " ").Replace("\u00a0", " ").Replace("&", " ");
            return Spaces.Replace(t, " ").Trim();
        }

        public static CardProfile Build(CardInfo c)
        {
            var p = new CardProfile { CardId = c.Id, CleanText = Clean(c.Text) };
            var t = p.CleanText.ToLowerInvariant();

            var m = Aoe.Match(t);
            if (m.Success)
            {
                p.AoeDamage = int.Parse(m.Groups[1].Value);
                p.AoeHitsFace = AoeFace.IsMatch(t);
            }
            p.BoardClear = Clear.IsMatch(t);

            if (p.AoeDamage == 0)
            {
                m = Single.Match(t);
                if (m.Success)
                {
                    p.Damage = int.Parse(m.Groups[1].Value);
                    p.DamageHitsFace = SingleFace.IsMatch(t);
                }
                else
                {
                    m = RandomDmg.Match(t);
                    if (m.Success) p.RandomDamage = int.Parse(m.Groups[1].Value);
                }
            }

            p.Destroy = !p.BoardClear && DestroyOne.IsMatch(t);
            p.Transform = TransformRx.IsMatch(t);
            p.Silence = SilenceRx.IsMatch(t);
            p.Freeze = FreezeRx.IsMatch(t);
            p.Bounce = BounceRx.IsMatch(t);
            p.Draws = DrawRx.IsMatch(t);
            m = HealRx.Match(t);
            if (m.Success) p.Heal = int.Parse(m.Groups[1].Value);
            m = ArmorRx.Match(t);
            if (m.Success) p.Armor = int.Parse(m.Groups[1].Value);

            p.Taunt = c.Has("TAUNT");
            p.Rush = c.Has("RUSH");
            p.Charge = c.Has("CHARGE");
            p.DivineShield = c.Has("DIVINE_SHIELD");
            p.Stealth = c.Has("STEALTH");
            p.Poisonous = c.Has("POISONOUS") || c.Has("VENOMOUS");
            p.Lifesteal = c.Has("LIFESTEAL");
            p.Windfury = c.Has("WINDFURY") || c.Has("MEGA_WINDFURY");
            p.Deathrattle = c.Has("DEATHRATTLE");
            p.Reborn = c.Has("REBORN");
            p.Elusive = c.Has("ELUSIVE");
            p.SpellPower = c.Has("SPELLPOWER");
            p.Aura = c.Has("AURA");
            p.Trigger = c.Has("TRIGGER_VISUAL");
            p.Secret = c.Has("SECRET");
            p.Discover = c.Has("DISCOVER");
            p.Battlecry = c.Has("BATTLECRY");
            return p;
        }
    }
}
