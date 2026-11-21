namespace ClickToImages.App.State;

// Which of the grid's pages are still owed a thumbnail render: ThumbnailLoader's bookkeeping, kept
// apart from the WinUI loader so it can be unit-tested, as ThumbnailQueue is. Pages are told apart
// by identity, as PageItem is. A removed PDF's pages are forgotten, so nothing here keeps it (and
// its renderer) alive once its undo step goes; if an undo brings it back, its pages are new again,
// and only the ones whose first render never finished are owed.
public sealed class UnrenderedPages<TPage> where TPage : notnull
{
    private readonly HashSet<TPage> _known = [];
    private readonly HashSet<TPage> _owed = [];

    public IReadOnlyCollection<TPage> Owed => _owed;

    // pages is the grid as it is now. A page not seen before is owed a render when neverRendered
    // says so; a page no longer there is forgotten.
    public void Sync(IReadOnlyCollection<TPage> pages, Func<TPage, bool> neverRendered)
    {
        foreach (TPage page in pages.Where(page => _known.Add(page) && neverRendered(page)))
        {
            _owed.Add(page);
        }

        _known.IntersectWith(pages);
        _owed.IntersectWith(pages);
    }

    // A display-scale change owes every page a render at the new density.
    public void OweAll() => _owed.UnionWith(_known);

    public void Rendered(TPage page) => _owed.Remove(page);
}
