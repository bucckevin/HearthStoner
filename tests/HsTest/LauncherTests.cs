using System.Collections.Generic;
using Xunit;

namespace HearthStoner.Tests
{
    public class LauncherTests
    {
        private const string Bnet = @"C:\Program Files (x86)\Battle.net\Battle.net.exe";

        [Theory]
        [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe", Bnet)]           // DisplayIcon
        [InlineData(@"C:\Program Files (x86)\Battle.net\", Bnet)]                         // InstallLocation
        [InlineData("\"C:\\Program Files (x86)\\Battle.net\\Battle.net.exe\",0", Bnet)]   // ApplicationIcon
        [InlineData("\"C:\\Program Files (x86)\\Battle.net\\Battle.net.exe\" \"%1\"", Bnet)] // battlenet:// handler
        [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe,0", Bnet)]
        [InlineData("", null)]
        public void Reads_every_registry_format(string value, string expected) => Assert.Equal(expected, GameLauncher.ExeFrom(value));

        private static GameLauncher.Target Resolve(string configured, Dictionary<string, string> reg, params string[] files) =>
            GameLauncher.Resolve(configured, (k, v) => reg.TryGetValue(k + "|" + v, out var x) ? x : null, p => System.Array.IndexOf(files, p) >= 0);

        [Fact]
        public void Finds_battle_net_from_the_registry_and_launches_hearthstone_through_it()
        {
            var reg = new Dictionary<string, string> { [@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net|DisplayIcon"] = @"D:\Games\Battle.net\Battle.net.exe" };
            var t = Resolve(null, reg, @"D:\Games\Battle.net\Battle.net.exe");
            Assert.Equal(@"D:\Games\Battle.net\Battle.net.exe", t.Path);
            Assert.Equal("--exec=\"launch WTCG\"", t.Arguments);
            Assert.True(t.IsBattleNet);
        }

        [Fact]
        public void Your_setting_wins_and_hearthstone_exe_runs_without_arguments()
        {
            var t = Resolve(@"S:\Hearthstone\Hearthstone.exe", new Dictionary<string, string>(), @"S:\Hearthstone\Hearthstone.exe", Bnet);
            Assert.Equal(@"S:\Hearthstone\Hearthstone.exe", t.Path);
            Assert.Equal("", t.Arguments);
            Assert.Equal("your setting", t.Source);
        }

        [Fact]
        public void A_missing_setting_falls_back_to_automatic() =>
            Assert.Equal(Bnet, Resolve(@"X:\gone\Battle.net.exe", new Dictionary<string, string>(), Bnet).Path);

        [Fact]
        public void Nothing_installed_means_no_target() =>
            Assert.Null(Resolve(null, new Dictionary<string, string>()).Path);
    }
}
