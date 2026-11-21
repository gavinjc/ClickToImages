using ClickToImages.App.State;

namespace ClickToImages.App.Tests;

public sealed class UnrenderedPagesTests
{
    [Fact]
    public void Sync_NewPages_OwesOnlyThoseNeverRendered()
    {
        Page rendered = new(rendered: true);
        Page fresh = new(rendered: false);
        var unrendered = new UnrenderedPages<Page>();

        unrendered.Sync([rendered, fresh], NeverRendered);

        Assert.Equal([fresh], unrendered.Owed);
    }

    [Fact]
    public void Rendered_IsNoLongerOwed()
    {
        Page page = new(rendered: false);
        var unrendered = new UnrenderedPages<Page>();
        unrendered.Sync([page], NeverRendered);

        unrendered.Rendered(page);

        Assert.Empty(unrendered.Owed);
    }

    // A removed PDF's pages are forgotten outright, so a later rescale doesn't bring them back and
    // nothing here keeps them alive.
    [Fact]
    public void Sync_RemovedPage_IsForgotten_EvenByALaterRescale()
    {
        Page kept = new(rendered: true);
        Page removed = new(rendered: false);
        var unrendered = new UnrenderedPages<Page>();
        unrendered.Sync([kept, removed], NeverRendered);

        unrendered.Sync([kept], NeverRendered);
        unrendered.OweAll();

        Assert.Equal([kept], unrendered.Owed);
    }

    // An undo restores the PDF's own pages: the ones already rendered keep their thumbnails, and
    // only the one whose render never finished is owed.
    [Fact]
    public void Sync_RestoredPages_OwesOnlyThoseNeverRendered()
    {
        Page done = new(rendered: false);
        Page unfinished = new(rendered: false);
        var unrendered = new UnrenderedPages<Page>();
        unrendered.Sync([done, unfinished], NeverRendered);
        unrendered.Rendered(done);
        done.IsRendered = true;
        unrendered.Sync([], NeverRendered);

        unrendered.Sync([done, unfinished], NeverRendered);

        Assert.Equal([unfinished], unrendered.Owed);
    }

    [Fact]
    public void OweAll_OwesEveryPageInTheGrid_RenderedOrNot()
    {
        Page rendered = new(rendered: true);
        Page fresh = new(rendered: false);
        var unrendered = new UnrenderedPages<Page>();
        unrendered.Sync([rendered, fresh], NeverRendered);

        unrendered.OweAll();

        Assert.Equal(2, unrendered.Owed.Count);
        Assert.Contains(rendered, unrendered.Owed);
    }

    private static bool NeverRendered(Page page) => !page.IsRendered;

    // A class, not a record: pages are told apart by identity, and two records with the same
    // values would be one page.
    private sealed class Page(bool rendered)
    {
        public bool IsRendered { get; set; } = rendered;
    }
}
