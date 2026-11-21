using ClickToImages.App.Diagnostics;
using ClickToImages.App.State;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace ClickToImages.App.ViewModels;

// Renders the page grid's thumbnails, in the order and with the limits ThumbnailScheduler sets.
// One 200-page scan renders one page at a time; measured at 300 px, all 200 still took about 8 s
// against the 12 s budget (spec §5). Runs on the UI thread; only the render goes to the thread pool.
internal sealed class ThumbnailLoader
{
    private readonly IReadOnlyList<PageItem> _pages;
    private readonly ThumbnailScheduler _scheduler;

    private readonly UnrenderedPages<PageItem> _unrendered = new();

    // Rendered at the display's real pixel density so thumbnails aren't upscaled and blurred on a
    // 150%-scaled screen.
    private double _rasterizationScale = 1.0;

    // pages is the grid's own list, which MainViewModel grows, shrinks and renumbers.
    public ThumbnailLoader(IReadOnlyList<PageItem> pages)
    {
        _pages = pages;
        _scheduler = new ThumbnailScheduler(index => RenderThumbnailAsync(_pages[index]));
    }

    // Call after every MainViewModel.PagesChanged: new pages are taken on, removed ones forgotten,
    // and the queue is rebuilt at the pages' current indexes.
    public void Refresh()
    {
        // IsLoading stays true until a page's first render finishes.
        _unrendered.Sync(_pages, page => page.IsLoading);
        _scheduler.Replace(_unrendered.Owed.Select(page => (page.Index, page.Document.Id)));
    }

    public void SetVisibleRange(int firstVisibleIndex, int lastVisibleIndex) =>
        _scheduler.SetVisibleRange(firstVisibleIndex, lastVisibleIndex);

    // A window dragged to a screen with different scaling renders every page again at the new
    // density; each tile keeps its old thumbnail until the new one arrives.
    public void SetRasterizationScale(double rasterizationScale)
    {
        if (rasterizationScale == _rasterizationScale)
        {
            return;
        }

        _rasterizationScale = rasterizationScale;
        _unrendered.OweAll();
        Refresh();
    }

    private async Task RenderThumbnailAsync(PageItem page)
    {
        _unrendered.Rendered(page);

        // The page's longer edge, at most, fills the tile's longer side.
        double longEdgeDips = Math.Max(PageItem.ThumbnailArea.Width, PageItem.ThumbnailArea.Height);
        uint longEdgePixels = (uint)Math.Round(longEdgeDips * _rasterizationScale);
        try
        {
            byte[] png = await Task.Run(
                () => page.Document.Renderer!.RenderScaledPngAsync(page.PageNumber, longEdgePixels));
            BitmapImage thumbnail = await BitmapImages.FromEncodedAsync(png);
            page.ThumbnailPixelSize = new Size(thumbnail.PixelWidth, thumbnail.PixelHeight);
            page.Thumbnail = thumbnail;
            page.HasError = false;
            page.ErrorDetail = null;
        }
        catch (Exception ex)
        {
            // One page that won't render is that tile's error, not the grid's (spec §4.3).
            AppLog.LogWarning("RenderThumbnailAsync", $"{page.Document.FileName} p{page.PageNumber}: {ex}");
            page.ErrorDetail = ex.Message;
            page.HasError = true;
        }
        finally
        {
            page.IsLoading = false;
        }
    }
}
