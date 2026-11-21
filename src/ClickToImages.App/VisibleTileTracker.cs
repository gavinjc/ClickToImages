using ClickToImages.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace ClickToImages.App;

// Reports which page tiles are in the grid's viewport, as a range of PageItem.Index, so thumbnails
// render on-screen first (spec §5). Realized containers alone aren't the answer: GridView realizes
// a cache of several screens either side, so each one's bounds are checked against the viewport.
internal sealed class VisibleTileTracker
{
    private readonly GridView _grid;
    private readonly Action<int, int> _onVisibleRangeChanged;
    private readonly Dictionary<SelectorItem, PageItem> _realizedTiles = [];
    private readonly DispatcherQueueTimer _measureTimer;
    private ScrollViewer? _scrollViewer;

    public VisibleTileTracker(GridView grid, Action<int, int> onVisibleRangeChanged)
    {
        _grid = grid;
        _onVisibleRangeChanged = onVisibleRangeChanged;

        // Scrolling raises ViewChanged many times a second. This waits 50 ms after the first one
        // and then measures once, so priorities follow a scroll without measuring on every event.
        _measureTimer = grid.DispatcherQueue.CreateTimer();
        _measureTimer.Interval = TimeSpan.FromMilliseconds(50);
        _measureTimer.IsRepeating = false;
        _measureTimer.Tick += (_, _) => Measure();

        grid.ContainerContentChanging += Grid_ContainerContentChanging;
        grid.SizeChanged += (_, _) => ScheduleMeasure();
    }

    private void Grid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            _realizedTiles.Remove(args.ItemContainer);
        }
        else
        {
            _realizedTiles[args.ItemContainer] = (PageItem)args.Item;
        }

        ScheduleMeasure();
    }

    private void ScheduleMeasure()
    {
        if (!_measureTimer.IsRunning)
        {
            _measureTimer.Start();
        }
    }

    // A tile counts as visible if any of it is inside the viewport.
    private void Measure()
    {
        if (AttachScrollViewer() is not { } scrollViewer)
        {
            return;
        }

        int first = int.MaxValue;
        int last = -1;
        foreach ((SelectorItem container, PageItem page) in _realizedTiles)
        {
            if (!container.IsLoaded)
            {
                continue;
            }

            Rect bounds = container.TransformToVisual(scrollViewer)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            if (bounds.Bottom > 0 && bounds.Top < scrollViewer.ViewportHeight)
            {
                first = Math.Min(first, page.Index);
                last = Math.Max(last, page.Index);
            }
        }

        if (last >= 0)
        {
            _onVisibleRangeChanged(first, last);
        }
    }

    // The ScrollViewer is part of GridView's template, which isn't applied while the grid is
    // collapsed behind the empty state, so it is looked for on first use rather than at Loaded.
    private ScrollViewer? AttachScrollViewer()
    {
        if (_scrollViewer is null && FindDescendant<ScrollViewer>(_grid) is ScrollViewer found)
        {
            _scrollViewer = found;
            found.ViewChanged += (_, _) => ScheduleMeasure();
        }

        return _scrollViewer;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is T descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
