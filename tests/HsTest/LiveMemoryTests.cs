using System;
using HearthStoner.Live;
using Xunit;

namespace HearthStoner.Tests
{
    public class LiveMemoryTests
    {
        private static readonly DateTime Attached = new DateTime(2026, 11, 7, 10, 58, 0, DateTimeKind.Utc);

        /// <summary>
        /// An attachment made while Hearthstone was loading returns only empty reads; it is made again once it is
        /// old enough, but one that has returned something is kept, however long it then reads nothing.
        /// </summary>
        [Fact]
        public void An_attachment_that_only_reads_empty_is_remade_after_the_grace_period()
        {
            Assert.False(LiveMemory.IsStale(false, Attached, Attached.AddSeconds(30)));
            Assert.True(LiveMemory.IsStale(false, Attached, Attached + LiveMemory.ProveWithin));
            Assert.True(LiveMemory.IsStale(false, Attached, Attached.AddHours(6)));
            Assert.False(LiveMemory.IsStale(true, Attached, Attached.AddHours(6)));
        }
    }
}
