using ClickToImages.App.State;

namespace ClickToImages.App.Tests;

// Each fake render is a task the test finishes by hand. A finished render's continuation runs
// inline on the test's thread, the way the UI thread runs it in the app, so every step is
// deterministic.
public class ThumbnailSchedulerTests
{
    private readonly Dictionary<int, TaskCompletionSource> _renders = [];
    private readonly List<int> _started = [];

    [Fact]
    public async Task Add_WhenEveryOtherPageIsInABusyDocument_Returns()
    {
        ThumbnailScheduler scheduler = NewScheduler();

        // The 2026-10-21 hang: with one document, every loop after the first found its pages busy,
        // gave its count back, and Add started it again, forever.
        Task add = Task.Run(() => scheduler.Add(Pages(document: 0, first: 0, count: 5)));

        await add.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([0], _started);
    }

    [Fact]
    public void Add_ManyDocuments_RendersFourAtOnce_EachFromADifferentDocument()
    {
        ThumbnailScheduler scheduler = NewScheduler();

        scheduler.Add([.. Enumerable.Range(0, 6).SelectMany(document => Pages(document, first: document * 2, count: 2))]);

        Assert.Equal([0, 2, 4, 6], _started);
    }

    [Fact]
    public void FinishingARender_StartsTheNextPageOfAFreeDocument()
    {
        ThumbnailScheduler scheduler = NewScheduler();
        scheduler.Add([.. Pages(document: 0, first: 0, count: 2), .. Pages(document: 1, first: 2, count: 2)]);

        Finish(0);

        Assert.Equal([0, 2, 1], _started);
    }

    [Fact]
    public void EveryPage_RendersExactlyOnce_InVisibilityOrder()
    {
        ThumbnailScheduler scheduler = NewScheduler();
        scheduler.Add(Pages(document: 0, first: 0, count: 6));
        scheduler.SetVisibleRange(4, 5);

        while (_started.Count > 0 && _renders.Values.Any(render => !render.Task.IsCompleted))
        {
            Finish(_started.Last());
        }

        Assert.Equal([0, 4, 5, 3, 2, 1], _started);
    }

    [Fact]
    public void Add_AfterEveryLoopHasFinished_StartsAgain()
    {
        ThumbnailScheduler scheduler = NewScheduler();
        scheduler.Add(Pages(document: 0, first: 0, count: 1));
        Finish(0);

        scheduler.Add(Pages(document: 0, first: 1, count: 1));

        Assert.Equal([0, 1], _started);
    }

    // A PDF removed or moved in the list renumbers the grid, so the waiting pages are swapped for
    // ones at their new indexes; a stale index would render the wrong page or run off the end.
    [Fact]
    public void Replace_DropsThePagesWaitingAtOldIndexes()
    {
        ThumbnailScheduler scheduler = NewScheduler();
        scheduler.Add(Pages(document: 0, first: 0, count: 3));

        scheduler.Replace(Pages(document: 1, first: 10, count: 1));
        Finish(0);
        Finish(10);

        Assert.Equal([0, 10], _started);
    }

    // The render already running isn't cancelled, and its document stays busy until it ends.
    [Fact]
    public void Replace_WhileAPageRenders_StillRendersOneAtATimePerDocument()
    {
        ThumbnailScheduler scheduler = NewScheduler();
        scheduler.Add(Pages(document: 0, first: 0, count: 2));

        scheduler.Replace(Pages(document: 0, first: 5, count: 1));

        Assert.Equal([0], _started);
        Finish(0);
        Assert.Equal([0, 5], _started);
    }

    private ThumbnailScheduler NewScheduler() => new(index =>
    {
        var render = new TaskCompletionSource();
        _renders[index] = render;
        _started.Add(index);
        return render.Task;
    });

    private void Finish(int index) => _renders[index].SetResult();

    private static IEnumerable<(int Index, int Document)> Pages(int document, int first, int count) =>
        Enumerable.Range(first, count).Select(index => (index, document));
}
