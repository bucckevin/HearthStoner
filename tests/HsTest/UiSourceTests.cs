using System.IO;
using System.Linq;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>Checks on the app's source for UI mistakes that are easy to miss in a full-size screenshot.</summary>
    public class UiSourceTests
    {
        /// <summary>
        /// A rounded Border with ClipToBounds clips its content to a rectangle, so art or coloured bars reaching the
        /// edge paint square corners over the rounding and the outline (the Best pick banner's right end, 1.4.0).
        /// RoundedBorder clips to the rounded shape.
        /// </summary>
        [Fact]
        public void Rounded_boxes_clip_their_content_with_RoundedBorder()
        {
            var app = Path.Combine(RepoRoot(), "App");
            var bad = Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
                .Where(x => x.Text.Contains("CornerRadius") && x.Text.Contains("ClipToBounds = true"))
                .Select(x => $"{Path.GetFileName(x.File)}:{x.Line}")
                .ToList();
            Assert.True(bad.Count == 0, "Use RoundedBorder instead of a rounded Border with ClipToBounds: " + string.Join(", ", bad));
        }

        /// <summary>The folder holding HearthStoner.slnx, found by walking up from the test's output folder.</summary>
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HearthStoner.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}
