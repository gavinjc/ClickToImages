using ClickToImages.App.State;

namespace ClickToImages.App.Tests;

public class ThumbnailQueueTests
{
    private static readonly HashSet<int> NoneBusy = [];

    [Fact]
    public void TryTake_BeforeAnyVisibleRange_TakesFromTheTopOfTheGrid()
    {
        ThumbnailQueue queue = QueueOf(document: 0, count: 5);

        Assert.Equal([0, 1, 2, 3, 4], TakeAll(queue, NoneBusy));
    }

    [Fact]
    public void TryTake_TakesVisibleTilesFirst_ThenNearestOutward()
    {
        ThumbnailQueue queue = QueueOf(document: 0, count: 10);
        queue.SetVisibleRange(4, 5);

        // An equal distance above and below goes to the earlier tile.
        Assert.Equal([4, 5, 3, 6, 2, 7, 1, 8, 0, 9], TakeAll(queue, NoneBusy));
    }

    [Fact]
    public void SetVisibleRange_AfterTakesBegan_ReordersWhatIsLeft()
    {
        ThumbnailQueue queue = QueueOf(document: 0, count: 10);
        queue.TryTake(NoneBusy, out _, out _);
        queue.TryTake(NoneBusy, out _, out _);

        queue.SetVisibleRange(8, 9);

        Assert.Equal([8, 9, 7, 6, 5, 4, 3, 2], TakeAll(queue, NoneBusy));
    }

    [Fact]
    public void TryTake_SkipsPagesOfABusyDocument_EvenWhenVisible()
    {
        var queue = new ThumbnailQueue();
        queue.Add(0, document: 0);
        queue.Add(1, document: 0);
        queue.Add(2, document: 1);
        queue.Add(3, document: 1);
        queue.SetVisibleRange(0, 1);

        Assert.True(queue.TryTake(new HashSet<int> { 0 }, out int index, out int document));
        Assert.Equal((2, 1), (index, document));
    }

    [Fact]
    public void TryTake_EveryPendingDocumentBusy_ReturnsFalseAndKeepsThePages()
    {
        ThumbnailQueue queue = QueueOf(document: 3, count: 2);

        Assert.False(queue.TryTake(new HashSet<int> { 3 }, out _, out _));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Add_APageAlreadyWaiting_IsTakenOnlyOnce()
    {
        ThumbnailQueue queue = QueueOf(document: 0, count: 3);

        queue.Add(1, document: 0);

        Assert.Equal([0, 1, 2], TakeAll(queue, NoneBusy));
    }

    [Fact]
    public void Add_APageAlreadyTaken_IsQueuedAgain()
    {
        ThumbnailQueue queue = QueueOf(document: 0, count: 2);
        queue.TryTake(NoneBusy, out _, out _);

        queue.Add(0, document: 0);

        Assert.Equal([0, 1], TakeAll(queue, NoneBusy));
    }

    private static ThumbnailQueue QueueOf(int document, int count)
    {
        var queue = new ThumbnailQueue();
        for (int index = 0; index < count; index++)
        {
            queue.Add(index, document);
        }

        return queue;
    }

    private static List<int> TakeAll(ThumbnailQueue queue, IReadOnlySet<int> busyDocuments)
    {
        var taken = new List<int>();
        while (queue.TryTake(busyDocuments, out int index, out _))
        {
            taken.Add(index);
        }

        return taken;
    }
}
