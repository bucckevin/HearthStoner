using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    public class CardProfileTests
    {
        [Theory]
        [InlineData("CORE_CS2_093", "Consecration")]
        [InlineData("CORE_CS2_029", "Fireball")]
        [InlineData("CORE_EX1_246", "Hex")]
        [InlineData("CORE_EX1_312", "Twisting Nether")]
        [InlineData("CORE_CS2_032", "Flamestrike")]
        [InlineData("CORE_CS2_024", "Frostbolt")]
        [InlineData("CORE_CS2_089", "Holy Light")]
        [InlineData("CORE_CS2_023", "Arcane Intellect")]
        [InlineData("EX1_332", "Silence")]
        [InlineData("CORE_CS2_012", "Swipe")]
        public void Profiles_known_cards(string id, string name)
        {
            var c = TestData.Cards.ById(id);
            Assert.NotNull(c);
            Assert.Equal(name, c.Name);
            var p = c.Profile;
            Func<CardProfile, bool> expect = name switch
            {
                "Consecration" => x => x.AoeDamage == 2 && x.AoeHitsFace,
                "Fireball" => x => x.Damage == 6 && x.DamageHitsFace && !x.IsAoe,
                "Hex" => x => x.Transform,
                "Twisting Nether" => x => x.BoardClear,
                "Flamestrike" => x => x.AoeDamage == 5 && !x.AoeHitsFace,
                "Frostbolt" => x => x.Damage == 3 && x.DamageHitsFace && x.Freeze,
                "Holy Light" => x => x.Heal == 8,
                "Arcane Intellect" => x => x.Draws,
                "Silence" => x => x.Silence,
                _ => x => x.Damage == 4 && x.DamageHitsFace,
            };
            Assert.True(expect(p), $"{name}: {p.Summary()}");
        }

        [Fact]
        public void Standard_pool_loads()
        {
            var std = TestData.Cards.All.Count(c => c.Collectible && c.IsStandard && c.Type != "HERO_SKIN");
            Assert.True(std > 900, $"{std} Standard cards");
        }

        [Fact]
        public void Every_standard_secret_has_a_rule()
        {
            var missing = TestData.Cards.All.Where(c => c.Collectible && c.IsStandard && c.Has("SECRET") && Secrets.ById(c.Id) == null)
                .Select(c => c.Name).ToList();
            Assert.Empty(missing);
            foreach (var s in Secrets.Standard)
            {
                var c = TestData.Cards.ById(s.CardId);
                Assert.True(c != null && c.Has("SECRET") && c.IsStandard && c.CardClass == s.Class && c.Name == s.Name, s.Name);
            }
        }
    }

    public class DeckCodeTests
    {
        [Fact]
        public void Beatrix_adds_ten_copies() =>
            Assert.Equal(10, DeckCode.CopiesJoining("<b>Taunt</b> While building your deck, pick a 2-Cost minion. Ten copies join your deck!"));

        [Fact]
        public void Etc_adds_nothing() => Assert.Equal(0, DeckCode.CopiesJoining(TestData.Cards.ById("ETC_080")?.Text));

        [Fact]
        public void Saved_decks_round_trip()
        {
            foreach (var d in DecksLog.SavedDecks(TestData.InstallDir))
            {
                var list = DeckCode.Decode(d.DeckCode, TestData.Cards);
                Assert.NotNull(list);
                if (list.Count == 0) continue;
                var full = DeckCode.DecodeFull(d.DeckCode, TestData.Cards);
                var heroClass = DeckCode.HeroClass(d.DeckCode, TestData.Cards);
                Assert.NotNull(heroClass);
                var hero = TestData.Cards.All.FirstOrDefault(c => c.Type == "HERO" && c.CardClass == heroClass && !c.Collectible && c.Id.StartsWith("HERO_"))
                        ?? TestData.Cards.All.First(c => c.Type == "HERO" && c.CardClass == heroClass);
                var re = DeckCode.Encode(new HackF5.UnitySpy.HearthstoneLib.Detail.Deck.Deck { DeckList = full.Main, HeroCardId = hero.Id, FormatType = 2 }, TestData.Cards);
                Assert.Equal(full.Main.OrderBy(x => x), DeckCode.DecodeFull(re, TestData.Cards).Main.OrderBy(x => x));
            }
        }
    }

    public class HotkeyTests
    {
        [Fact] public void Ctrl_shift_letter() { Assert.True(Hotkeys.Parse("Ctrl+Shift+H", out var m, out var v)); Assert.Equal(6u, m); Assert.Equal((uint)'H', v); }
        [Fact] public void Alt_function_key() { Assert.True(Hotkeys.Parse("Alt+F2", out var m, out var v)); Assert.Equal(1u, m); Assert.Equal(0x71u, v); }
        [Fact] public void Rejects_nonsense() => Assert.False(Hotkeys.Parse("Ctrl+Banana", out _, out _));
    }

    public class LethalSolverTests
    {
        private static LethalSolver.Attacker A(int a, bool face = true, bool wf = false) => new LethalSolver.Attacker { Name = "m" + a, Attack = a, CanHitFace = face, Windfury = wf };
        private static LethalSolver.Taunt T(int h, bool shield = false) => new LethalSolver.Taunt { Name = "Taunt", Health = h, DivineShield = shield };
        private static LethalSolver.Spell Bolt(int cost, int dmg) => new LethalSolver.Spell { Name = "bolt", Cost = cost, Damage = dmg, CanHitFace = true };
        private static readonly List<LethalSolver.Spell> None = new List<LethalSolver.Spell>();
        private static bool Lethal(LethalSolver.Attacker[] a, LethalSolver.Taunt[] t, int needed, int mana = 0, params LethalSolver.Spell[] s) =>
            LethalSolver.Solve(a.ToList(), t.ToList(), s.ToList(), mana, needed).Lethal;

        [Fact] public void Open_board() => Assert.True(Lethal(new[] { A(3), A(4) }, new LethalSolver.Taunt[0], 7));
        [Fact] public void Through_one_taunt() => Assert.True(Lethal(new[] { A(2), A(5) }, new[] { T(2) }, 5));
        [Fact] public void Divine_shield_needs_two_hits() => Assert.True(Lethal(new[] { A(1), A(1), A(5) }, new[] { T(1, true) }, 5));
        [Fact] public void Divine_shield_one_hit_is_not_enough() => Assert.False(Lethal(new[] { A(1), A(5) }, new[] { T(1, true) }, 5));
        [Fact] public void Spell_clears_taunt() => Assert.True(Lethal(new[] { A(6) }, new[] { T(3) }, 6, 2, Bolt(2, 3)));
        [Fact] public void Spell_needs_mana() => Assert.False(Lethal(new[] { A(6) }, new[] { T(3) }, 6, 1, Bolt(2, 3)));
        [Fact] public void Rush_minion_clears_taunt() => Assert.True(Lethal(new[] { A(4, face: false), A(3) }, new[] { T(4) }, 3));
        [Fact] public void Windfury_hits_taunt_then_face() => Assert.True(Lethal(new[] { A(3, wf: true) }, new[] { T(3) }, 3));

        [Fact]
        public void Worst_case_is_fast()
        {
            var big = Enumerable.Range(0, 7).Select(i => A(2 + i % 3, wf: i == 0)).ToArray();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LethalSolver.Solve(big.ToList(), new List<LethalSolver.Taunt> { T(5), T(4, true), T(7) },
                Enumerable.Range(0, 5).Select(i => Bolt(1, 2)).ToList(), 10, 40);
            Assert.True(sw.ElapsedMilliseconds < 400, $"{sw.ElapsedMilliseconds} ms");
        }
    }

    public class LadderTests
    {
        [Fact]
        public void Faster_at_higher_win_rate()
        {
            var g55 = LadderEstimate.GamesToReach(31, 41, 0.55);
            var g60 = LadderEstimate.GamesToReach(31, 41, 0.60);
            Assert.True(g55 > g60 && g60 > 0, $"{g55} vs {g60}");
        }

        [Fact] public void No_climb_at_50_percent() => Assert.Null(LadderEstimate.GamesToReach(1, 11, 0.50));
    }

    public class MetaTests
    {
        [Fact]
        public void Archetypes_load_and_shares_add_up()
        {
            var m = TestData.Meta;
            Assert.True(m != null && m.Archetypes.Count > 10, "meta stats didn't load");
            Assert.InRange(m.Archetypes.Sum(a => a.Share), 0.99, 1.01);
            Assert.True(m.Archetypes.Count(a => a.Cards.Count > 0) > m.Archetypes.Count / 2);
        }
    }
}

namespace HearthStoner.Tests
{
    public class BaseCardIdTests
    {
        [Theory]
        [InlineData("CATA_479t", "CATA_479")]
        [InlineData("CATA_479t3", "CATA_479")]
        [InlineData("TIME_009t1", "TIME_009")]
        [InlineData("JAIL_328", "JAIL_328")]
        [InlineData("CORE_EX1_103", "CORE_EX1_103")]
        public void Token_forms_map_to_their_card(string id, string expected) => Assert.Equal(expected, GameTracker.BaseCardId(id));
    }
}

namespace HearthStoner.Tests
{
    public class HandGeometryTests
    {
        [Fact]
        public void One_card_sits_near_the_centre_top()
        {
            var (x, y) = HandGeometry.Position(1, 0, 1920, 1080);
            Assert.InRange(x, 1920 * 0.44, 1920 * 0.5);
            Assert.InRange(y, 0, 1080 * 0.08);
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1080)]
        [InlineData(1024, 768)]
        public void Cards_run_left_to_right_inside_the_board(double w, double h)
        {
            var board = (4.0 / 3.0) / (w / h);
            var left = w * (1 - board) / 2;
            for (var n = 1; n <= 10; n++)
            {
                double prev = -1;
                for (var i = 0; i < n; i++)
                {
                    var (x, y) = HandGeometry.Position(n, i, w, h);
                    Assert.True(x > prev, $"{n} cards: card {i} not right of card {i - 1}");
                    Assert.InRange(x, left, left + w * board);
                    Assert.InRange(y, 0, h * 0.1);
                    prev = x;
                }
            }
        }

        [Fact]
        public void At_4_3_it_matches_the_measured_pixels() =>
            Assert.Equal((480.0, 48.0), HandGeometry.Position(1, 0, 1024, 768));
    }
}

namespace HearthStoner.Tests
{
    public class CoinTests
    {
        [Theory]
        [InlineData("GAME_005", true)]
        [InlineData("JAIL_COIN5", true)]
        [InlineData("BE_COIN1", true)]
        [InlineData("JAIL_328", false)]
        [InlineData(null, false)]
        public void Recognises_every_coin(string id, bool coin) => Assert.Equal(coin, TestData.Cards.IsCoin(id));
    }
}
