using System;

namespace HearthStoner
{
    /// <summary>
    /// The app's zoom when Settings' Text size is Auto: 100% in a normal window, and grown to fill the screen when
    /// maximized. It follows the screen's height, not its width, so an ultrawide screen fills up without pages getting
    /// so tall that everything needs scrolling.
    /// </summary>
    public static class AutoZoom
    {
        /// <summary>The page height the layouts are drawn for at 100% (a maximized 1920x1080 screen).</summary>
        public const double BaseHeight = 1080;
        /// <summary>The window width the layouts are drawn for at 100% (the default window).</summary>
        public const double BaseWidth = 1640;
        /// <summary>The zoom is rounded to this step, so lines and text land on whole pixels more often.</summary>
        public const double Step = 0.05;
        /// <summary>The smallest and largest zoom Auto picks.</summary>
        public const double Min = 1.0, Max = 1.5;

        /// <summary>
        /// The zoom for a window whose content area is <paramref name="width"/> x <paramref name="height"/> (device
        /// independent pixels). Only a maximized window is zoomed; bad sizes give 1.
        /// </summary>
        public static double For(double width, double height, bool maximized)
        {
            if (!maximized || !(width > 0) || !(height > 0)) return 1.0;
            var fit = Math.Min(height / BaseHeight, width / BaseWidth);
            var stepped = Math.Round(fit / Step, MidpointRounding.AwayFromZero) * Step;
            return Math.Round(Math.Max(Min, Math.Min(Max, stepped)), 2);
        }
    }
}