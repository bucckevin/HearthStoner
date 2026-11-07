using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    public class QuestTests
    {
        private static CardDb Cards => TestData.Cards;
        private static readonly string Spell = Cards.All.First(c => c.Collectible && c.Type == "SPELL" && c.SpellSchool == "FIRE").Id;
        private static readonly string Beast = Cards.All.First(c => c.Collectible && c.Type == "MINION" && c.Race == "BEAST" && c.Cost == 1).Id;
        private static readonly string Power = Cards.All.First(c => c.Type == "HERO_POWER").Id;

        private static QuestEntry Quest(int id, string template, int quota, int progress = 0, string pool = "Daily", int xp = 1000, string icon = "Cards") =>
            new QuestEntry { Id = id, Text = QuestEntry.Fill(template, quota), Quota = quota, Progress = progress, Pool = pool, Xp = xp, Icon = icon, ResetDay = pool == "Weekly" ? 1 : -1 };

        /// <summary>A game where you played these cards on your one turn and took the opponent from 30 to 30 - damage.</summary>
        private static GameRecord Game(string deck, string cls, bool won, int damage, params string[] played) => new GameRecord
        {
            DeckName = deck, MyClass = cls, Result = won ? "WON" : "LOST", GameType = "GT_RANKED", DeckCards = Enumerable.Repeat(deck, 30).ToList(),
            Snapshots = new List<TurnSnapshot>
            {
                new TurnSnapshot { Turn = 1, Yours = true, TheirHp = 30, ManaAvailable = 1, Played = played.ToList(), MyDeck = 25 },
                new TurnSnapshot { Turn = 2, Yours = false, TheirHp = 30 - damage, MyDeck = 24, TheirMinionsDied = 1 },
            },
        };

        private static PlayerDeck Deck(string name, string cls) => new PlayerDeck { Name = name, Class = cls, Cards = Enumerable.Repeat(name, 30).ToList() };

        [Theory]
        [InlineData("Summon $q Black Empire minions.<i>(2x - Legendary, Old God)</i>", "Summon 10 Black Empire minions. (2x - Legendary, Old God)")]
        [InlineData("Play $q <b>Battlecry</b> cards .", "Play 10 Battlecry cards.")]
        [InlineData("Win $q games.\n", "Win 10 games.")]
        public void Quest_text_loses_the_game_markup(string template, string expected) => Assert.Equal(expected, QuestEntry.Fill(template, 10));

        [Fact]
        public void Rerolled_and_rewarded_quests_are_not_open()
        {
            var active = Quest(1, "Win $q games.", 10).With(1, 2);   // slow: would be the reroll pick
            var rerolled = Quest(2, "Play $q games as Death Knight, Rogue, or Shaman.", 3).With(0, 6);
            var rewarded = Quest(3, "Cast $q spells.", 25).With(25, 5);
            Assert.True(active.Open);
            Assert.False(rerolled.Open);
            Assert.False(rewarded.Open);
            Assert.True(Quest(4, "Win $q games.", 2).Open);   // saved before statuses were read (0)
            var log = new QuestLog { Quests = { active, rerolled, rewarded } };
            var plan = QuestPlan.Make(log, new List<PlayerDeck> { Deck("A", "PALADIN") }, new List<GameRecord> { Game("A", "PALADIN", true, 5) }, Cards, null, DateTime.Now);
            Assert.DoesNotContain(plan.GamesFor.Keys, id => id == 2);
            Assert.Null(plan.Reroll);   // today's daily reroll is used up

            // Without a rerolled daily, the slowest daily is the one to reroll.
            var slow = Quest(5, "Win $q games.", 10).With(0, 2);
            var plan2 = QuestPlan.Make(new QuestLog { Quests = { slow } }, new List<PlayerDeck> { Deck("A", "PALADIN") }, new List<GameRecord> { Game("A", "PALADIN", true, 5) }, Cards, null, DateTime.Now);
            Assert.Equal(slow, plan2.Reroll);
        }

        [Fact]
        public void Season_countdown_in_days_then_hours_then_minutes()
        {
            var end = new DateTime(2026, 12, 1, 8, 3, 36, DateTimeKind.Utc);
            Assert.Equal("Season ends in 24 days", QuestLog.SeasonEndsText(end, new DateTime(2026, 11, 7, 4, 0, 0, DateTimeKind.Utc)));
            Assert.Equal("Season ends in 47 h", QuestLog.SeasonEndsText(end, end.AddHours(-47.5)));
            Assert.Equal("Season ends in 1 h", QuestLog.SeasonEndsText(end, end.AddMinutes(-61)));
            Assert.Equal("Season ends in 5 min", QuestLog.SeasonEndsText(end, end.AddMinutes(-5)));
            Assert.Null(QuestLog.SeasonEndsText(end, end.AddSeconds(1)));   // a new season the app hasn't read yet
            Assert.Null(QuestLog.SeasonEndsText(null, end));
        }

        [Theory]
        [InlineData("Cast $q spells.", 25, 2)]
        [InlineData("Play $q spells.", 25, 2)]
        [InlineData("Cast $q Fire or Shadow spells.", 12, 2)]
        [InlineData("Play $q Beasts.", 12, 1)]
        [InlineData("Play $q Beasts and/or Elementals.", 50, 1)]
        [InlineData("Play $q minions that cost 1.", 8, 1)]
        [InlineData("Play $q cards.", 30, 3)]
        [InlineData("Use your Hero Power $q times.", 5, 1)]
        [InlineData("Deal $q damage to enemy heroes.", 65, 7)]
        [InlineData("Destroy $q minions.", 25, 1)]
        [InlineData("Take $q turns.", 20, 1)]
        [InlineData("Draw $q cards.", 30, 1)]
        [InlineData("Play $q games.", 3, 1)]
        [InlineData("Play $q games as Death Knight, Hunter, or Mage.", 3, 1)]
        [InlineData("Play $q games of Hearthstone's Ranked Mode.", 10, 1)]
        public void Maps_quest_text_to_a_count_per_game(string template, int quota, double expected)
        {
            var rule = QuestPlan.RuleFor(Quest(1, template, quota), Cards);
            Assert.NotNull(rule);
            Assert.False(rule.Wins);
            Assert.Equal(expected, rule.Count(Game("Hunter", "HUNTER", true, 7, Spell, Spell, Beast, Power)));
        }

        [Fact]
        public void Class_goals_count_only_games_as_those_classes()
        {
            var rule = QuestPlan.RuleFor(Quest(1, "Play $q games as Death Knight, Demon Hunter, or Druid.", 3), Cards);
            Assert.Equal(1.0, rule.Count(Game("x", "DEMONHUNTER", true, 0)));
            Assert.Equal(0.0, rule.Count(Game("x", "MAGE", true, 0)));
        }

        [Theory]
        [InlineData("Win $q games.", "Games")]
        [InlineData("Win $q games of Hearthstone's Ranked Mode.", "Games")]
        public void Win_goals_use_the_win_rate(string template, string icon) => Assert.True(QuestPlan.RuleFor(Quest(1, template, 5, icon: icon), Cards).Wins);

        [Theory]
        [InlineData("Play $q Lost City cards.", "Cards")]
        [InlineData("Win $q games of Tavern Brawl, Arena, or Battlegrounds.", "Games")]
        [InlineData("Win a game with 4 different classes.", "Games")]
        [InlineData("Finish in Top 4 in Battlegrounds.", "Battlegrounds")]
        [InlineData("Watch a friend win a game in Spectator Mode.", "Social")]
        [InlineData("Complete $q Daily Quests.", "")]
        public void Goals_it_cant_model_have_no_rule(string template, string icon) => Assert.Null(QuestPlan.RuleFor(Quest(1, template, 5, icon: icon), Cards));

        [Fact]
        public void Keyword_cards_use_the_card_data() =>
            Assert.NotNull(QuestPlan.RuleFor(Quest(1, "Play $q Battlecry cards.", 50), Cards));

        [Fact]
        public void Recommends_the_deck_that_finishes_the_most_quest_xp_per_game()
        {
            var spells = Deck("Spell Mage", "MAGE");
            var beasts = Deck("Beast Hunter", "HUNTER");
            var games = new List<GameRecord>();
            for (var i = 0; i < 4; i++) games.Add(Game(spells.Name, "MAGE", i % 2 == 0, 5, Spell, Spell, Spell, Spell, Spell));   // 5 spells, 50% wins
            for (var i = 0; i < 4; i++) games.Add(Game(beasts.Name, "HUNTER", i < 3, 10, Beast, Beast));                         // 2 beasts, 75% wins
            var log = new QuestLog
            {
                Quests =
                {
                    Quest(1, "Cast $q spells.", 20, xp: 1000),
                    Quest(2, "Win $q games.", 3, xp: 1000, icon: "Games"),
                    Quest(3, "Play $q Beasts.", 12, progress: 6, xp: 1000),
                    Quest(4, "Play $q Lost City cards.", 75, pool: "Weekly", xp: 1750),
                },
            };
            var plan = QuestPlan.Make(log, new List<PlayerDeck> { spells, beasts }, games, Cards, d => null, new DateTime(2026, 11, 3));

            // Spell Mage: 5/20 spells + 0.5/3 wins = 250 + 167 XP a game. Beast Hunter: 0.75/3 wins + 2/6 beasts = 250 + 333.
            Assert.Equal("Beast Hunter", plan.Best.Deck.Name);
            Assert.Equal(1000 * 0.75 / 3 + 1000 * 2.0 / 6, plan.Best.XpPerGame, 6);
            Assert.Equal("~4 games: Play 12 Beasts + Win 3 games", plan.Best.Reason);
            Assert.Equal(4.0, plan.GamesFor[1]);                 // spells: best with Spell Mage
            Assert.Equal(4.0, plan.GamesFor[2]);                 // 3 wins at 75%
            Assert.False(plan.GamesFor.ContainsKey(4));        // "can't estimate"
            Assert.Equal(1, plan.Reroll.Id);                   // ties at 4 games and 250 XP a game: the first
        }

        [Fact]
        public void Rerolls_the_daily_that_takes_the_most_games()
        {
            var a = Quest(1, "Cast $q spells.", 25);
            var b = Quest(2, "Play $q Beasts.", 12);
            var c = Quest(3, "Play $q Lost City cards.", 10);
            var games = new Dictionary<int, double> { [1] = 3, [2] = double.PositiveInfinity };
            Assert.Equal(2, QuestPlan.PickReroll(new[] { a, b, c }, games).Id);
            Assert.Null(QuestPlan.PickReroll(new[] { a }, new Dictionary<int, double> { [1] = 1.5 }));   // quick ones aren't worth a reroll
        }

        [Fact]
        public void Estimates_the_games_to_finish_every_weekly_before_the_reset()
        {
            var deck = Deck("Spell Mage", "MAGE");
            var games = Enumerable.Range(0, 4).Select(i => Game(deck.Name, "MAGE", true, 10, Spell, Spell)).ToList();
            var log = new QuestLog { Quests = { Quest(1, "Cast $q spells.", 20, pool: "Weekly"), Quest(2, "Deal $q damage to enemy heroes.", 200, pool: "Weekly") } };
            var plan = QuestPlan.Make(log, new List<PlayerDeck> { deck }, games, Cards, d => null, new DateTime(2026, 11, 3));   // a Tuesday
            Assert.Equal(20.0, plan.WeekliesGames.Value);              // 200 damage at 10 a game
            Assert.Equal(new DateTime(2026, 11, 9), plan.WeeklyReset);   // next Monday
        }

        [Fact]
        public void Battlegrounds_quests_are_left_out_of_the_plan()
        {
            var deck = Deck("Spell Mage", "MAGE");
            var games = Enumerable.Range(0, 4).Select(i => Game(deck.Name, "MAGE", true, 10, Spell, Spell)).ToList();
            var bg = Quest(2, "Play $q Demons or Elementals.", 30, pool: "Weekly", xp: 60, icon: "Battlegrounds");
            bg.RewardTrack = QuestEntry.BattlegroundsTrack;
            var log = new QuestLog { Quests = { Quest(1, "Cast $q spells.", 20, pool: "Weekly"), bg } };
            var plan = QuestPlan.Make(log, new List<PlayerDeck> { deck }, games, Cards, d => null, new DateTime(2026, 11, 3));
            Assert.True(bg.IsBattlegrounds);
            Assert.False(new QuestEntry().IsBattlegrounds);          // quests saved before the track was read count as main
            Assert.False(plan.GamesFor.ContainsKey(2));
            Assert.Equal(10.0, plan.WeekliesGames.Value);            // only the spell weekly: 20 spells at 2 a game
        }

        [Fact]
        public void No_quests_file_means_no_plan()
        {
            var plan = QuestPlan.Make(null, new List<PlayerDeck>(), new List<GameRecord>(), Cards, d => null, DateTime.Now);
            Assert.Null(plan.Best);
            Assert.Empty(plan.GamesFor);
        }
    }
}
