using System;
using Xunit;

namespace HearthStoner.Tests
{
    public class WeeklyResetTests
    {
        private static readonly DateTime Reset = new DateTime(2026, 11, 9);   // a Monday, midnight

        [Theory]
        [InlineData(2026, 11, 7, 1, 0, 2)]     // Saturday 1am: 1 day 23 hours rounds up to 2
        [InlineData(2026, 11, 8, 0, 0, 1)]     // exactly one day before
        [InlineData(2026, 11, 8, 23, 59, 1)]   // a minute before: still "1 day", never 0
        [InlineData(2026, 11, 2, 0, 0, 7)]     // a week before
        public void Days_until_the_weekly_reset_round_up_and_are_at_least_one(int y, int mo, int d, int h, int mi, int expected)
        {
            Assert.Equal(expected, QuestPlan.DaysUntil(Reset, new DateTime(y, mo, d, h, mi, 0)));
        }
    }
}
