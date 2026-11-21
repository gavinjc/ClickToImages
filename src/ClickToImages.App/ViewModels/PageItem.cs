using ClickToImages.App.State;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ClickToImages.App.ViewModels;

// One tile in the page grid. Created as soon as its document opens, so the tile shows a placeholder
// before its thumbnail renders. Pages keep the document's order (spec §4.3), but documents can be
// removed and reordered, so Index changes with them.
public partial class PageItem : ObservableObject
{
    public PageItem(DocumentGroup document, int pageNumber, int index)
    {
        Document = document;
        PageNumber = pageNumber;
        Index = index;
    }

    public DocumentGroup Document { get; }

    // 1-based, within Document.
    public int PageNumber { get; }

    // Position in the whole grid, across every document; what ThumbnailQueue orders by. Only
    // MainViewModel sets it, renumbering every page whenever a document is removed or moved.
    public int Index { get; set; }

    // The tile's image area in DIPs; MainWindow.xaml's item template sizes its Image from this.
    public static Size ThumbnailArea { get; } = new(158, 184);

    // Scales a quarter-turned thumbnail down (or up) so it still fits the tile; see RotatedFit.
    public double ThumbnailScale => RotatedFit.Scale(ThumbnailArea, ThumbnailPixelSize, RotationDegrees);

    // Whether the page is exported. Every page starts included (spec §4.3).
    [ObservableProperty]
    private bool _isChecked = true;

    // Applied to the exported image only, never to the source PDF (spec §7).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailScale))]
    private int _rotationDegrees;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    // Only the aspect ratio matters, to ThumbnailScale.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailScale))]
    private Size _thumbnailPixelSize;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasError;

    // Only set with HasError, for the tile's tooltip.
    [ObservableProperty]
    private string? _errorDetail;

    // Mirrors GridView.SelectedItems, the tiles R and Shift+R act on; not the same as IsChecked.
    // MainWindow's SelectionChanged handler is the only writer: a GridViewItem template can't
    // reach into its content to draw the highlight, so the tile's DataTemplate draws it from this.
    [ObservableProperty]
    private bool _isSelected;

    public void RotateClockwise() => RotationDegrees = (RotationDegrees + 90) % 360;

    public void RotateCounterClockwise() => RotationDegrees = (RotationDegrees + 270) % 360;
}
