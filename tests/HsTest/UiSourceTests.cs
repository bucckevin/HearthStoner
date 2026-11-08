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
            // Statement by statement (split at ';'), so an object initializer spread over several lines is caught too.
            var bad = Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories)
                .SelectMany(f =>
                {
                    var text = File.ReadAllText(f);
                    var found = new System.Collections.Generic.List<string>();
                    var start = 0;
                    foreach (var statement in text.Split(';'))
                    {
                        if (statement.Contains("CornerRadius") && statement.Contains("ClipToBounds = true"))
                            found.Add($"{Path.GetFileName(f)}:{text.Substring(0, start).Count(c => c == '\n') + 1}");
                        start += statement.Length + 1;
                    }
                    return found;
                })
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
