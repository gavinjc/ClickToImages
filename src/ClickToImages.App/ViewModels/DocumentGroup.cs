using ClickToImages.Core.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClickToImages.App.ViewModels;

// One source PDF and its pages: a row in the PDF list, and a section under its own header in the
// grid (spec §4.3). A PDF that failed to open keeps its row and header, with the reason and no
// pages, so it isn't silently missing.
public sealed partial class DocumentGroup : ObservableObject
{
    private DocumentGroup(int id, string path, PdfPageRenderer? renderer, Exception? openFailure)
    {
        Id = id;
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Renderer = renderer;
        OpenFailure = openFailure;
    }

    // Unique to this document for the window's life, whatever is removed or moved; ThumbnailQueue's
    // per-document key.
    public int Id { get; }

    public string Path { get; }

    public string FileName { get; }

    // Null only when the PDF failed to open, in which case Pages is empty.
    public PdfPageRenderer? Renderer { get; }

    public bool HasFailed => Renderer is null;

    public Exception? OpenFailure { get; }

    public string? ErrorMessage => OpenFailure is null ? null : PdfOpenFailures.ReadableReason(OpenFailure);

    public IReadOnlyList<PageItem> Pages { get; private set; } = [];

    public int PageCount => Pages.Count;

    // For the list row's "48 pages · 12 ticked"; MainViewModel keeps it current.
    [ObservableProperty]
    private int _checkedPageCount;

    // Mirrors the PDF list's selected row, the one the grid last jumped to. The row's card is
    // opaque and hides ListViewItem's own selection visual, so the DataTemplate draws it from this,
    // as the page tiles do from PageItem.IsSelected.
    [ObservableProperty]
    private bool _isSelected;

    // firstPageIndex is where this document's first page sits in the whole grid.
    public static DocumentGroup Opened(int id, string path, PdfPageRenderer renderer, int firstPageIndex)
    {
        var group = new DocumentGroup(id, path, renderer, null);
        group.Pages = Enumerable.Range(1, renderer.PageCount)
            .Select(pageNumber => new PageItem(group, pageNumber, firstPageIndex + pageNumber - 1))
            .ToList();
        group.CheckedPageCount = group.Pages.Count;
        return group;
    }

    public static DocumentGroup Failed(int id, string path, Exception openFailure) =>
        new(id, path, null, openFailure);
}
