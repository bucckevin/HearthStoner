using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    public class ExtractedFilesTests
    {
        private static string TempApp()
        {
            var d = Path.Combine(Path.GetTempPath(), "hs-extract-" + Guid.NewGuid().ToString("N"), "HearthStoner");
            foreach (var id in new[] { "old1", "current", "old2" }) Directory.CreateDirectory(Path.Combine(d, id));
            return d;
        }

        [Fact]
        public void Older_builds_folders_are_stale_and_the_current_one_is_kept()
        {
            var app = TempApp();
            try
            {
                // The runtime may list the folder with a trailing slash, in another case, next to unrelated folders.
                var inUse = new[] { @"C:\Windows\System32", Path.Combine(app, "CURRENT") + Path.DirectorySeparatorChar };
                var stale = ExtractedFiles.Stale(app, inUse).Select(Path.GetFileName).OrderBy(n => n).ToList();
                Assert.Equal(new[] { "old1", "old2" }, stale);

                Assert.Equal(2, ExtractedFiles.RemoveStale(app, inUse));
                Assert.Equal(new[] { "current" }, Directory.GetDirectories(app).Select(Path.GetFileName));
            }
            finally { Directory.Delete(Path.GetDirectoryName(app), true); }
        }

        [Fact]
        public void A_folder_with_an_open_file_is_left_whole()
        {
            var app = TempApp();
            try
            {
                File.WriteAllText(Path.Combine(app, "old1", "a.dll"), "a");
                var locked = Path.Combine(app, "old1", "b.dll");
                File.WriteAllText(locked, "b");
                using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))   // like a loaded DLL
                {
                    if (!OperatingSystem.IsWindows()) return;   // only Windows refuses to share an open file
                    Assert.Equal(1, ExtractedFiles.RemoveStale(app, new[] { Path.Combine(app, "current") }));   // old2 only
                    Assert.True(File.Exists(Path.Combine(app, "old1", "a.dll")));                            // nothing taken from old1
                }
            }
            finally { Directory.Delete(Path.GetDirectoryName(app), true); }
        }

        [Fact]
        public void Nothing_is_deleted_when_the_folder_in_use_is_unknown()
        {
            var app = TempApp();
            try
            {
                Assert.Empty(ExtractedFiles.Stale(app, Array.Empty<string>()));                 // e.g. not a single-file build
                Assert.Empty(ExtractedFiles.Stale(app, new[] { @"C:\Windows\System32" }));
                Assert.Empty(ExtractedFiles.Stale(app + "-missing", new[] { Path.Combine(app, "current") }));
                Assert.Equal(0, ExtractedFiles.RemoveStale(app, Array.Empty<string>()));
                Assert.Equal(3, Directory.GetDirectories(app).Length);
            }
            finally { Directory.Delete(Path.GetDirectoryName(app), true); }
        }
    }
}
