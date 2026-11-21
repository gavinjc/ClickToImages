namespace ClickToImages.App.State;

// Runs the grid's thumbnail renders in ThumbnailQueue's order: at most four at once, as in both
// siblings, and never two from one document, because PdfDocument must not render from several
// threads (spec §5). The render itself comes in as a delegate so this can be tested without WinUI.
// Not thread-safe: every call, and every render's completion, is expected on the UI thread.
public sealed class ThumbnailScheduler
{
    public const int MaxConcurrentRenders = 4;

    private readonly ThumbnailQueue _queue = new();
    private readonly HashSet<int> _busyDocuments = [];

    // Given a grid index; must not throw, since a failed page is that tile's error to show.
    private readonly Func<int, Task> _renderPage;
    private int _runningRenderLoops;

    public ThumbnailScheduler(Func<int, Task> renderPage)
    {
        _renderPage = renderPage;
    }

    public void Add(IEnumerable<(int Index, int Document)> pages)
    {
        foreach ((int index, int document) in pages)
        {
            _queue.Add(index, document);
        }

        // Counted before starting any: a loop that finds every page's document busy ends
        // synchronously and gives its count back, so re-checking _runningRenderLoops here would
        // start that loop again forever, hanging the UI thread.
        int loopsToStart = Math.Min(MaxConcurrentRenders - _runningRenderLoops, _queue.Count);
        for (int i = 0; i < loopsToStart; i++)
        {
            _runningRenderLoops++;
            _ = RenderUntilNoneFreeAsync();
        }
    }

    // For after the grid is renumbered (a PDF removed, restored or moved): the pages still waiting
    // are given again at their new indexes. Renders already running finish as they are.
    public void Replace(IEnumerable<(int Index, int Document)> pages)
    {
        _queue.Clear();
        Add(pages);
    }

    public void SetVisibleRange(int firstVisibleIndex, int lastVisibleIndex) =>
        _queue.SetVisibleRange(firstVisibleIndex, lastVisibleIndex);

    // Stops when every page left belongs to a document that another loop is already rendering:
    // that loop will reach those pages itself, so waiting here would only hold a slot.
    private async Task RenderUntilNoneFreeAsync()
    {
        try
        {
            while (_queue.TryTake(_busyDocuments, out int index, out int document))
            {
                _busyDocuments.Add(document);
                try
                {
                    await _renderPage(index);
                }
                finally
                {
                    _busyDocuments.Remove(document);
                }
            }
        }
        finally
        {
            _runningRenderLoops--;
        }
    }
}
