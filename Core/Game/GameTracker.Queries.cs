using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HearthStoner.Game
{
    public sealed partial class GameTracker
    {

        // ================================================================== queries used by the advisor

        public Entity PlayerEntity(int playerId) =>
            _playerIdToEntity.TryGetValue(playerId, out var eid) && Entities.TryGetValue(eid, out var e) ? e : null;


        public Entity Hero(int playerId)
        {
            var p = PlayerEntity(playerId);
            var heroId = p?.Int("HERO_ENTITY") ?? 0;
            if (heroId != 0 && Entities.TryGetValue(heroId, out var h)) return h;
            return Entities.Values.FirstOrDefault(x => x.CardType == "HERO" && x.Zone == "PLAY" && x.Controller == playerId);
        }


        /// <summary>
        /// The player's class. When their hero now is classless (Deathwing, Worldbreaker and other neutral hero cards),
        /// the class of the hero they started with. Hero skins the card list doesn't know fall back to the game's own
        /// CLASS tag. Adventure bosses really are "NEUTRAL".
        /// </summary>
        public string HeroClass(int playerId)
        {
            var now = ClassOf(Hero(playerId));
            if (HeroClasses.IsReal(now)) return now;
            // The starting hero: created in play at setup, not a hero card from the deck.
            var start = Entities.Values
                .Where(e => e.CardType == "HERO" && !e.StartedInDeck && (e.StartController == playerId || e.Controller == playerId))
                .OrderBy(e => e.Id)
                .Select(ClassOf)
                .FirstOrDefault(HeroClasses.IsReal);
            return start ?? now;
        }

        /// <summary>
        /// Battlegrounds: your place (PLAYER_LEADERBOARD_PLACE on your hero, 1-8; 0 when not shown yet) and your hero's
        /// card id; (0, null) while it isn't known which player you are.
        /// </summary>
        public (int Place, string HeroCardId) BattlegroundsFinish()
        {
            var me = FriendlyPlayerId;
            var hero = me == 0 ? null : Hero(me);
            if (hero == null) return (0, null);
            var place = hero.Int("PLAYER_LEADERBOARD_PLACE");
            return (place is >= 1 and <= 8 ? place : 0, string.IsNullOrEmpty(hero.KnownCardId) ? null : hero.KnownCardId);
        }

        private string ClassOf(Entity hero)
        {
            if (hero == null) return null;
            var known = _cards.ById(hero.KnownCardId)?.CardClass;
            if (HeroClasses.IsReal(known)) return known;
            var tag = hero.Tag("CLASS");   // a name like DEATHKNIGHT; ignore anything else
            return tag != null && tag.Length > 0 && tag.All(char.IsLetter) ? tag : known;
        }


        public IEnumerable<Entity> Zone(int playerId, string zone) =>
            Entities.Values.Where(e => e.Controller == playerId && e.Zone == zone && IsCardEntity(e));


        public List<Entity> Minions(int playerId) =>
            Zone(playerId, "PLAY").Where(e => e.IsMinion).OrderBy(e => e.Int("ZONE_POSITION")).ToList();


        public List<Entity> Hand(int playerId) =>
            Zone(playerId, "HAND").OrderBy(e => e.Int("ZONE_POSITION")).ToList();


        public int DeckCount(int playerId) => Zone(playerId, "DECK").Count();


        public Entity Weapon(int playerId) => Zone(playerId, "PLAY").FirstOrDefault(e => e.CardType == "WEAPON");


        /// <summary>Mana available right now (this turn's crystals, minus used and locked, plus temporary).</summary>
        public int AvailableMana(int playerId)
        {
            var p = PlayerEntity(playerId);
            if (p == null) return 0;
            return Math.Max(0, p.Int("RESOURCES") + p.Int("TEMP_RESOURCES") - p.Int("RESOURCES_USED") - p.Int("OVERLOAD_LOCKED"));
        }


        /// <summary>The player's mana crystal cap: 10, unless a card raised it (MAXRESOURCES, e.g. some Druid cards).</summary>
        public int MaxMana(int playerId)
        {
            var m = PlayerEntity(playerId)?.Int("MAXRESOURCES") ?? 0;
            return m > 0 ? m : 10;
        }


        /// <summary>Mana crystals the player will have next turn (up to their cap), minus overload owed.</summary>
        public int NextTurnMana(int playerId)
        {
            var p = PlayerEntity(playerId);
            if (p == null) return 0;
            return Math.Max(0, Math.Min(MaxMana(playerId), p.Int("RESOURCES") + 1) - p.Int("OVERLOAD_OWED"));
        }


        /// <summary>Card ids the opponent revealed that were part of their starting deck (useful for archetype guessing).</summary>
        public List<string> OpponentOriginalCardsSeen() =>
            Entities.Values
                .Where(e => e.StartedInDeck && e.StartController == OpponentPlayerId && !string.IsNullOrEmpty(e.KnownCardId) && IsCardEntity(e)
                            && !_cards.IsCoin(e.KnownCardId))
                .Select(e => e.KnownCardId)
                .ToList();


        /// <summary>Your deck's remaining known cards (deck list minus cards that have left your deck), with counts.</summary>
        public Dictionary<string, int> FriendlyRemainingDeck()
        {
            var result = new Dictionary<string, int>();
            if (FriendlyDeckList == null) return result;
            foreach (var id in FriendlyDeckList) result[id] = (result.TryGetValue(id, out var n) ? n : 0) + 1;

            foreach (var e in Entities.Values)
            {
                if (!IsCardEntity(e)) continue;
                // Cards from your starting deck count as gone once they leave it, even if the opponent stole them.
                var mine = e.StartedInDeck ? e.StartController == FriendlyPlayerId : e.Controller == FriendlyPlayerId;
                if (!mine) continue;
                var id = e.KnownCardId;
                if (string.IsNullOrEmpty(id)) continue;
                if (e.StartedInDeck && (e.Zone != "DECK" || e.Controller != FriendlyPlayerId))
                {
                    // Match the deck list by the card it is now, the card it first was, or the base card of a
                    // changed form (CATA_479t -> CATA_479).
                    var match = new[] { id, e.FirstCardId, BaseCardId(id), BaseCardId(e.FirstCardId) }
                        .FirstOrDefault(c => !string.IsNullOrEmpty(c) && result.TryGetValue(c, out var left) && left > 0);
                    if (match != null) result[match]--;
                }
                else if (!e.StartedInDeck && e.Zone == "DECK")
                {
                    // Shuffled into your deck by an effect and visible to you.
                    result[id] = (result.TryGetValue(id, out var n) ? n : 0) + 1;
                }
            }
            foreach (var k in result.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList()) result.Remove(k);
            return result;
        }


        /// <summary>"CATA_479t" / "CATA_479t3" -> "CATA_479": the card a token form or alternate version belongs to.</summary>
        public static string BaseCardId(string id) =>
            string.IsNullOrEmpty(id) ? null : Regex.Replace(id, @"t\d*$", "");


        public string CardName(Entity e) => string.IsNullOrEmpty(e?.KnownCardId) ? "an unknown card" : _cards.NameOf(e.KnownCardId);


        /// <summary>Card id of an entity if known, else null (never an empty string).</summary>
        public string EntityCardId(int entityId) =>
            Entities.TryGetValue(entityId, out var e) && !string.IsNullOrEmpty(e.KnownCardId) ? e.KnownCardId : null;


        private static bool IsCardEntity(Entity e)
        {
            var t = e.CardType;
            return t != "PLAYER" && t != "GAME" && t != "ENCHANTMENT" && t != "HERO_POWER" && t != "HERO" || (t == "HERO" && e.Zone != "PLAY" && e.Zone != "SETASIDE" && e.Zone != "REMOVEDFROMGAME" && e.Zone != "GRAVEYARD");
        }
    }
}
