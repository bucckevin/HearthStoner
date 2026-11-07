using System;
using Xunit;

namespace HearthStoner.Tests
{
    public class AppVersionTests
    {
        [Fact]
        public void Reads_number_and_build_date()
        {
            var v = AppVersion.Parse("1.4.2", "2026-11-07T02:30");
            Assert.Equal("1.4.2", v.Number);
            Assert.Equal(new DateTime(2026, 11, 7, 2, 30, 0), v.BuildDate);
            Assert.Equal("1.4.2, built Nov 7, 2026", v.Detail);
        }

        [Fact]
        public void A_commit_added_after_a_plus_is_never_shown()
        {
            var v = AppVersion.Parse("1.4.2+2c99dcb8d1e5f0a7b3c4d5e6f708192a3b4c5d6e", "2026-11-07T02:30");
            Assert.Equal("1.4.2", v.Number);
            Assert.Equal("1.4.2, built Nov 7, 2026", v.Detail);
        }

        [Theory]
        [InlineData("1.0.0", null, "1.0.0")]                // no date
        [InlineData("1.0.0", "not a date", "1.0.0")]
        [InlineData(null, null, "0.0.0")]
        [InlineData("  ", "2026-11-07", "0.0.0")]           // date without the time: unknown
        public void Missing_parts_are_left_out(string informational, string buildDate, string expectedDetail)
        {
            var v = AppVersion.Parse(informational, buildDate);
            Assert.Null(v.BuildDate);
            Assert.Equal(expectedDetail, v.Detail);
        }

        [Fact]
        public void The_app_project_reads_its_version_from_the_VERSION_file()
        {
            var file = System.IO.Path.Combine(TestData.Root, "VERSION");
            Assert.Matches(@"^\d+\.\d+\.\d+$", System.IO.File.ReadAllText(file).Trim());
        }
    }
}
