namespace ClickToImages.App.State;

// The order the page grid's thumbnails render in: the tiles on screen first, then outwards from
// them, so scrolling to the middle of a 200-page document doesn't wait for the 100 pages above it
// (spec §5; ClickTo: PDF only caps concurrency). Indexes are positions in the whole grid, across
// every document, so "nearest" also crosses group boundaries; a document is a number unique to it
// for the window's life (DocumentGroup.Id). Kept apart from the WinUI loader so
// the ordering can be unit-tested.
public sealed class ThumbnailQueue
{
    private readonly List<(int Index, int Document)> _pending = [];
    private readonly HashSet<int> _pendingIndexes = [];

    // Before anything has reported what is on screen, the top of the grid is.
    private int _firstVisible;
    private int _lastVisible;

    public int Count => _pending.Count;

    // A page already waiting isn't queued twice, so re-queuing every page (after a display-scale
    // change) costs one render per page, not two.
    public void Add(int index, int document)
    {
        if (_pendingIndexes.Add(index))
        {
            _pending.Add((index, document));
        }
    }

    public void Clear()
    {
        _pending.Clear();
        _pendingIndexes.Clear();
    }

    public void SetVisibleRange(int firstVisible, int lastVisible)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lastVisible, firstVisible);
        _firstVisible = firstVisible;
        _lastVisible = lastVisible;
    }

    // A document already rendering is skipped rather than waited for: PdfDocument renders one
    // page at a time (spec §5), so a second request for it would only queue behind the first,
    // holding a render slot another document could use.
    public bool TryTake(IReadOnlySet<int> busyDocuments, out int index, out int document)
    {
        int best = -1;
        for (int i = 0; i < _pending.Count; i++)
        {
            if (busyDocuments.Contains(_pending[i].Document))
            {
                continue;
            }

            if (best < 0 || IsBefore(_pending[i].Index, _pending[best].Index))
            {
                best = i;
            }
        }

        if (best < 0)
        {
            index = -1;
            document = -1;
            return false;
        }

        (index, document) = _pending[best];
        _pending.RemoveAt(best);
        _pendingIndexes.Remove(index);
        return true;
    }

    private bool IsBefore(int index, int other)
    {
        int distance = DistanceFromVisible(index);
        int otherDistance = DistanceFromVisible(other);
        return distance != otherDistance ? distance < otherDistance : index < other;
    }

    private int DistanceFromVisible(int index)
    {
        if (index < _firstVisible)
        {
            return _firstVisible - index;
        }

        return index > _lastVisible ? index - _lastVisible : 0;
    }
}
