using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace HearthStoner.Tests
{
    /// <summary>
    /// The full self-test ('HearthStoner selftest'): replays every saved Power.log through the real tracker,
    /// advisor and recorder and checks each game (you found, names matched, advice every turn, deck tracked,
    /// mana sane, game recorded). Fails with the FAIL lines in the message.
    /// </summary>
    [Trait("Category", TestData.MyGames)]
    public class GameLogTests
    {
        private readonly ITestOutputHelper _out;
        public GameLogTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Selftest_passes_on_all_saved_games()
        {
            var lines = new List<string>();
            var fails = SelfTest.Run(TestData.Cards, TestData.InstallDir, TestData.Meta, l => { lines.Add(l); _out.WriteLine(l); });
            Assert.True(lines.Any(l => l.Contains("Power.log files with games") && !l.Contains(" 0 Power.log")), "no game logs found");
            Assert.True(fails == 0, string.Join("\n", lines.Where(l => l.Contains("FAIL"))));
        }
    }
}
