using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LiveCaptionsTranslator.utils
{
    public static class ScrollHelper
    {
        // Makes `scroller` the one that scrolls `page`. The navigation view wraps every page in its
        // own ScrollViewer, which gives the page unlimited height: the page's ScrollViewer then has
        // nothing to scroll but still swallows the mouse wheel, so neither of them moves.
        public static void UseOwnScrollViewer(Page page, ScrollViewer scroller)
        {
            var disabled = new List<(ScrollViewer Viewer, ScrollBarVisibility Visibility)>();

            page.Loaded += (s, e) =>
            {
                if (disabled.Count > 0)
                    return;
                for (var parent = VisualTreeHelper.GetParent(page); parent != null;
                     parent = VisualTreeHelper.GetParent(parent))
                {
                    if (parent is ScrollViewer outer)
                    {
                        disabled.Add((outer, outer.VerticalScrollBarVisibility));
                        outer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        outer.ScrollToTop();
                    }
                }
            };
            page.Unloaded += (s, e) =>
            {
                foreach (var (viewer, visibility) in disabled)
                    viewer.VerticalScrollBarVisibility = visibility;
                disabled.Clear();
            };

            // Scroll by the actual wheel delta, so precision touchpads (many small deltas) scroll
            // smoothly instead of jumping a few lines per event.
            scroller.PreviewMouseWheel += (s, e) =>
            {
                if (e.Handled || scroller.ScrollableHeight <= 0)
                    return;
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta * 0.5);
                e.Handled = true;
            };
        }
    }
}
