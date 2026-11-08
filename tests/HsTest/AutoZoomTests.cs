using Newtonsoft.Json;
using Xunit;

namespace HearthStoner.Tests
{
    /// <summary>Text size Auto: the zoom picked for a window's size and state.</summary>
    public class AutoZoomTests
    {
        [Theory]
        [InlineData(3440, 1400, 1.30)]   // a 3440x1440 ultrawide, maximized (taskbar takes 40)
        [InlineData(2560, 1400, 1.30)]   // 2560x1440
        [InlineData(1920, 1040, 1.0)]    // 1920x1080: 0.96, never below 100%
        [InlineData(1920, 1200, 1.10)]   // 1920x1200: 1.11 rounds to 1.10
        [InlineData(3840, 2120, 1.50)]   // 4K at 100% Windows scaling: capped at 150%
        [InlineData(1700, 2000, 1.05)]   // a tall, narrow screen: limited by the width (1.037)
        public void Maximized_follows_the_screen_height(double width, double height, double expected) =>
            Assert.Equal(expected, AutoZoom.For(width, height, maximized: true), 3);

        [Fact]
        public void A_normal_window_is_never_zoomed() =>
            Assert.Equal(1.0, AutoZoom.For(3440, 1400, maximized: false));

        [Theory]
        [InlineData(0, 1400)]
        [InlineData(3440, 0)]
        [InlineData(double.NaN, 1400)]
        [InlineData(-5, -5)]
        public void Bad_sizes_give_100_percent(double width, double height) =>
            Assert.Equal(1.0, AutoZoom.For(width, height, maximized: true));

        [Fact]
        public void Auto_is_on_for_new_and_older_settings_files()
        {
            Assert.True(new Settings().AppScaleAuto);
            // A settings file saved before Auto existed has a Text size but no AppScaleAuto.
            Assert.True(JsonConvert.DeserializeObject<Settings>("{\"AppScale\":1.15}").AppScaleAuto);
            Assert.False(JsonConvert.DeserializeObject<Settings>("{\"AppScale\":1.15,\"AppScaleAuto\":false}").AppScaleAuto);
        }
    }
}