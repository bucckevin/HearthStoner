using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>Entity stats as the log sets them.</summary>
    public class EntityTests
    {
        /// <summary>
        /// Hand of Infinity gives int.MaxValue attack (and a Bloodsail Raider copying that weapon too); adding two of
        /// those up overflowed and crashed the app as the game ended (Nov 8, 00:14).
        /// </summary>
        [Fact]
        public void Infinite_stats_add_up_without_overflowing()
        {
            Entity Minion(string atk, string hp, string dmg = "0")
            {
                var e = new Entity();
                e.Tags["ATK"] = atk; e.Tags["HEALTH"] = hp; e.Tags["DAMAGE"] = dmg; e.Tags["ARMOR"] = atk;
                return e;
            }
            var board = new List<Entity>
            {
                Minion("2147483647", "3"), Minion("2147483647", "2147483647"), Minion("4", "5", "2147483647"),
            };
            Assert.Equal(2 * Entity.Infinite + 4, board.Sum(m => m.Attack));
            Assert.Equal(3 + Entity.Infinite, board.Sum(m => System.Math.Max(0, m.Health)));
            Assert.Equal(2 * Entity.Infinite + 4, board.Sum(m => m.Armor));
            Assert.Equal(7, Minion("7", "6").Attack);
        }
    }
}
