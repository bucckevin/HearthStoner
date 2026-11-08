using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Pages
{
    /// <summary>
    /// A <see cref="Border"/> whose content is clipped to its rounded corners. WPF's ClipToBounds clips to a plain
    /// rectangle, so card art and coloured bars painted over the corners and the outline (the square right end of
    /// the Best pick banner). Use it for any rounded box whose content reaches its edges. Assumes the same radius on
    /// every corner, as every box in the app has.
    /// </summary>
    public sealed class RoundedBorder : Border
    {
        protected override Size ArrangeOverride(Size finalSize)
        {
            var arranged = base.ArrangeOverride(finalSize);
            if (Child is FrameworkElement child)
            {
                // The child sits inside the outline and padding, so its corners are rounded by what's left of the radius.
                var inset = Math.Max(BorderThickness.Left, 0) + Math.Max(Padding.Left, 0);
                var radius = Math.Max(0, CornerRadius.TopLeft - inset);
                var size = child.RenderSize;
                child.Clip = size.Width > 0 && size.Height > 0 ? new RectangleGeometry(new Rect(size), radius, radius) : null;
            }
            return arranged;
        }
    }
}
