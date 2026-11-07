using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace HearthStoner.Tests
{
    public class ArtJobTests
    {
        [Theory]
        [InlineData(null, 0, "1.2", true)]      // never made
        [InlineData("1.2", 0, "1.2", true)]     // version written but the boards are gone
        [InlineData("1.1", 43, "1.2", true)]    // Hearthstone updated
        [InlineData("1.2\r\n", 43, "1.2", false)]
        [InlineData(null, 43, "1.2", true)]     // boards from before version.txt existed: remade once
        public void Remakes_art_when_missing_or_for_another_version(string madeFor, int boards, string game, bool expected) =>
            Assert.Equal(expected, ArtJob.NeedsBuild(madeFor, boards, game));

        private static readonly DateTime Now = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);
        private const string Start = "D 15:59:02 GameState.DebugPrintPower() - CREATE_GAME\n";
        private const string End = "D 16:09:09 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE\n";

        [Fact]
        public void In_a_game_after_create_game_until_complete()
        {
            Assert.True(HearthstoneLocator.GameInProgress(End + Start + "turn 3", false, Now.AddMinutes(-1), Now));
            Assert.False(HearthstoneLocator.GameInProgress(Start + End + "menu", false, Now.AddMinutes(-1), Now));
            Assert.False(HearthstoneLocator.GameInProgress("no games yet", false, Now, Now));
        }

        [Fact]
        public void A_log_quiet_for_10_minutes_means_hearthstone_was_closed_mid_game() =>
            Assert.False(HearthstoneLocator.GameInProgress(End + Start, false, Now.AddMinutes(-11), Now));

        [Fact]
        public void A_cut_off_tail_with_no_markers_is_the_middle_of_a_long_game() =>
            Assert.True(HearthstoneLocator.GameInProgress("TAG_CHANGE ...", true, Now.AddSeconds(-5), Now));

        private static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "hs-artjob-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        [Fact]
        public void Status_flow_waits_for_the_game_then_reports_progress_and_writes_the_version()
        {
            var dir = TempDir();
            try
            {
                var kinds = new List<string>();
                var inGameCalls = 0;
                ArtJob.Version = _ => "6000.3.11";
                ArtJob.InGame = () => { inGameCalls++; if (inGameCalls > 1) kinds.Add(ArtJob.Status.Kind); return inGameCalls == 1; };
                ArtJob.Build = (hs, outDir, progress, ct) =>
                {
                    kinds.Add(ArtJob.Status.Kind);
                    Directory.CreateDirectory(Path.Combine(outDir, "boards"));
                    for (var i = 1; i <= 3; i++)
                    {
                        File.WriteAllText(Path.Combine(outDir, "boards", $"B{i}.jpg"), "");
                        progress("boards", i, 3);
                        kinds.Add(ArtJob.Status.Text);
                    }
                };
                var builds = ArtJob.Builds;
                ArtJob.Run(dir, dir, force: false, pollMs: 1);

                Assert.Equal("waiting", kinds[0]);
                Assert.Equal("running", kinds[1]);
                Assert.Equal("Making board art: boards 2 of 3", kinds[3]);
                Assert.Equal("Board art: up to date for Hearthstone 6000.3.11 (3 boards)", ArtJob.Status.Text);
                Assert.Equal("6000.3.11", File.ReadAllText(Path.Combine(dir, "version.txt")));
                Assert.Equal(builds + 1, ArtJob.Builds);

                // Up to date: nothing is rebuilt.
                ArtJob.Build = (hs, outDir, progress, ct) => throw new Exception("should not run");
                ArtJob.Run(dir, dir, force: false, pollMs: 1);
                Assert.Equal("done", ArtJob.Status.Kind);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void A_failing_builder_reports_why_and_keeps_the_old_version()
        {
            var dir = TempDir();
            try
            {
                ArtJob.Version = _ => "2";
                ArtJob.InGame = () => false;
                ArtJob.Build = (hs, outDir, progress, ct) => { progress("textures", 1, 10); throw new NotImplementedException(); };
                File.WriteAllText(Path.Combine(dir, "version.txt"), "1");
                ArtJob.Run(dir, dir, force: false, pollMs: 1);
                Assert.Equal("failed", ArtJob.Status.Kind);
                Assert.StartsWith("Couldn't make board art: ", ArtJob.Status.Text);
                Assert.Equal("1", File.ReadAllText(Path.Combine(dir, "version.txt")));

                ArtJob.Version = _ => null;
                ArtJob.Run(dir, dir, force: true, pollMs: 1);
                Assert.Equal("Couldn't make board art: Hearthstone wasn't found.", ArtJob.Status.Text);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
