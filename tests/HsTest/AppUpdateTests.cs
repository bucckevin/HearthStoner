using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace HearthStoner.Tests
{
    public class AppUpdateTests
    {
        private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static string ReleaseJson(string tag, bool withSha = true) => @"{
  ""tag_name"": """ + tag + @""",
  ""html_url"": ""https://github.com/bucckevin/HearthStoner/releases/tag/" + tag + @""",
  ""body"": ""- New things"",
  ""assets"": [
    { ""name"": ""HearthStoner.exe"", ""browser_download_url"": ""https://example.com/HearthStoner.exe"" }" +
            (withSha ? @",
    { ""name"": ""HearthStoner.exe.sha256"", ""browser_download_url"": ""https://example.com/HearthStoner.exe.sha256"" }" : "") + @"
  ]
}";

        [Fact]
        public void Parses_a_release()
        {
            var r = AppUpdate.ParseRelease(ReleaseJson("v1.3.0"));
            Assert.NotNull(r);
            Assert.Equal(new Version(1, 3, 0), r.Version);
            Assert.Equal("https://example.com/HearthStoner.exe", r.ExeUrl);
            Assert.Equal("https://example.com/HearthStoner.exe.sha256", r.Sha256Url);
            Assert.Equal("- New things", r.Notes);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData(@"{ ""tag_name"": ""nightly"", ""assets"": [] }")]
        public void Rejects_unusable_releases(string json) => Assert.Null(AppUpdate.ParseRelease(json));

        [Fact]
        public void Rejects_a_release_without_its_checksum() => Assert.Null(AppUpdate.ParseRelease(ReleaseJson("v1.3.0", withSha: false)));

        [Theory]
        [InlineData("v1.3.0", "1.2.0", true)]
        [InlineData("v1.2.10", "1.2.9", true)]
        [InlineData("v1.2.0", "1.2.0", false)]
        [InlineData("v1.1.1", "1.2.0", false)]
        [InlineData("v1.0.0", "0.0.0", true)]
        public void Compares_versions_as_numbers(string tag, string current, bool newer) =>
            Assert.Equal(newer, AppUpdate.IsNewer(AppUpdate.ParseRelease(ReleaseJson(tag)), current));

        [Theory]
        [InlineData("1.2", false)]
        [InlineData("1.2.3.4", false)]
        [InlineData("V2.0.1", true)]
        [InlineData(null, false)]
        public void Versions_need_three_numbers(string text, bool ok) => Assert.Equal(ok, AppUpdate.TryParseVersion(text, out _));

        [Theory]
        [InlineData(Hash + "  HearthStoner.exe", Hash)]
        [InlineData("  " + Hash + "\n", Hash)]
        [InlineData("0123ABCD  HearthStoner.exe", null)]
        [InlineData("", null)]
        public void Reads_sha256_files(string text, string expected) => Assert.Equal(expected, AppUpdate.ParseSha256(text));

        [Fact]
        public void Checks_the_hash_of_a_download()
        {
            var data = Encoding.UTF8.GetBytes("HearthStoner");
            var hex = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            Assert.True(AppUpdate.HashMatches(data, hex));
            Assert.False(AppUpdate.HashMatches(data, Hash));
        }

        [Fact]
        public void Stays_off_while_the_repo_is_private()
        {
            var status = AppUpdate.Check("1.0.0");
            Assert.Null(status.Newer);
            Assert.NotNull(status.Error);
        }
    }
}
