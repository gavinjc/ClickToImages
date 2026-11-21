using System.ComponentModel;
using Bureau.Common;
using Bureau.Common.Help;
using Bureau.Common.Messages;
using ClickToImages.App.Converters;
using ClickToImages.App.Diagnostics;
using ClickToImages.App.State;
using ClickToImages.App.ViewModels;
using ClickToImages.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;

namespace ClickToImages.App;

public sealed partial class MainWindow : Window
{
    private static readonly ResourceLoader ResourceLoader = new();

    // One row each in the message bar; a new message of a kind replaces the one showing. Unreadable
    // pages share the skipped row, as they shared its InfoBar before.
    private const string SkippedMessageKey = "skipped";
    private const string SavedMessageKey = "saved";
    private const string CopiedMessageKey = "copied";
    private const string ErrorMessageKey = "error";
    private const string WaitingToSaveMessageKey = "waiting-to-save";

    private readonly ThumbnailLoader _thumbnails;
    private readonly IReadOnlyList<string> _launchPaths;

    // Non-PDFs from the launch selection and from every drop since, listed in the skipped row.
    private readonly List<string> _skippedNames;
    private int _skippedCountShown;

    // The PDF list as it was when a row drag began, for the reorder's undo step.
    private List<DocumentGroup> _orderBeforeDrag = [];

    // Held by whichever ContentDialog is open; see ShowDialogAsync.
    private readonly SemaphoreSlim _dialogTurn = new(1, 1);

    // A dialog can return, and another can reach its turn, after the window has gone.
    private bool _windowClosed;

    private bool _optionsExpanded;
    // The page the overlay is showing; what its rotate buttons, tick box and R act on.
    private PageItem? _previewItem;

    // The page being rendered to replace it, if any. Kept apart so nothing acts on a page that
    // isn't on screen yet, and so a render that a later step overtook is dropped.
    private PageItem? _requestedPreview;

    // pdfPaths come from the shell extension's .list file or the dev command line; skippedNames
    // are items the selection included but the shell extension excluded. A dev command line can
    // name anything, so it is expanded on load the way the shell extension would have.
    public MainWindow(IReadOnlyList<string> pdfPaths, IReadOnlyList<string> skippedNames)
    {
        InitializeComponent();

        _launchPaths = pdfPaths;
        _skippedNames = [.. skippedNames];
        SetTitleFromFolderOf(_launchPaths.FirstOrDefault());

        AboutDialog.ProductName = ResourceLoader.GetString("WindowTitle");
        AboutDialog.Description = ResourceLoader.GetString("AppDescriptionText");
        AboutDialog.LogDirectoryPath = AppLog.LogDirectoryPath;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Package.appxmanifest's Square44x44Logo only covers the Start tile/pinned-shortcut
        // icon and (when packaged) the taskbar — the running window's own titlebar icon needs
        // this explicit call regardless of packaging state, or it falls back to WinUI's
        // generic default icon.
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon("Assets\\ClickToImages.ico");
        appWindow.Closing += AppWindow_Closing;

        TitleBarTheme.Follow(this, RootGrid);

        ViewModel = new MainViewModel(
            AskPasswordAsync, (path, failure) => AppLog.LogWarning("OpenDocument", $"{path}: {failure}"));
        RefreshSkippedMessage();
        RootGrid.DataContext = ViewModel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.PagesChanged += ViewModel_PagesChanged;
        DocumentsList.ItemsSource = ViewModel.Documents;
        PagesGridView.ItemsSource = new CollectionViewSource
        {
            IsSourceGrouped = true,
            ItemsPath = new PropertyPath(nameof(DocumentGroup.Pages)),
            Source = ViewModel.Documents,
        }.View;
        _thumbnails = new ThumbnailLoader(ViewModel.Pages);
        _ = new VisibleTileTracker(PagesGridView, _thumbnails.SetVisibleRange);

        ShowOptionsInControls();
        // On close rather than on every change: a Slider drag or typing in the pattern box would
        // otherwise write the file at every step.
        Closed += (_, _) =>
        {
            _windowClosed = true;
            AppSettings.Save(_options);
        };

        // On the root, not the grid, so the keys still work once focus is on the preview overlay's
        // controls or the toolbar. GridView marks arrow keys (and some others) handled before a
        // plain KeyDown handler sees them (lessons.md); handledEventsToo keeps them reachable.
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootGrid_KeyDown), true);
        HelpKey.Attach(RootGrid, ShowHelp);
    }

    public MainViewModel ViewModel { get; }

    public void ShowErrorMessage(string message) => Messages.Show(ErrorMessageKey, MessageSeverity.Error, message);

    // Loading waits for this rather than starting in the constructor: the thumbnails' pixel size
    // needs XamlRoot's display scale, and the window should be on screen before any PDF is read.
    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        _thumbnails.SetRasterizationScale(RootGrid.XamlRoot.RasterizationScale);
        RootGrid.XamlRoot.Changed += (root, _) => _thumbnails.SetRasterizationScale(root.RasterizationScale);

        PreviewOverlay.SizeChanged += (_, _) => FitPreviewImage();
        FocusRootGrid();

        if (_launchPaths.Count > 0)
        {
            await AddPathsAsync(_launchPaths);
        }
    }

    // Folders are listed off the UI thread: one on a slow or unreachable share can take as long as
    // the network's timeout, and the window would show "Not responding" for all of it.
    private async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        PdfSelection selection = await Task.Run(() => PdfSelection.Expand(paths));
        _skippedNames.AddRange(selection.SkippedNames);
        await LoadPdfsAsync(selection.PdfPaths);
    }

    // Adds to the grid. The title names the folder of the first PDF the window was given, as at
    // launch, so PDFs added later from other folders don't rename it.
    private async Task LoadPdfsAsync(IReadOnlyList<string> pdfPaths)
    {
        if (ViewModel.Documents.Count == 0 && pdfPaths.Count > 0)
        {
            SetTitleFromFolderOf(pdfPaths[0]);
        }

        await ViewModel.LoadAsync(pdfPaths);
        RefreshSkippedMessage();
    }

    // Skipped non-PDFs and any PDF skipped for its encryption, in one list. Shown again only when
    // something new was skipped, so a row the user closed stays closed while they add PDFs that load.
    private void RefreshSkippedMessage()
    {
        int count = _skippedNames.Count + ViewModel.SkippedDocuments.Count;
        if (count == _skippedCountShown)
        {
            return;
        }

        _skippedCountShown = count;

        Messages.Show(
            SkippedMessageKey,
            MessageSeverity.Warning,
            SkippedMessage.Format(_skippedNames, ViewModel.SkippedDocuments, ResourceLoader.GetString));
    }

    // One prompt per protected PDF; the view model opens it with what comes back and keeps
    // nothing. The box is cleared straight away, so the password doesn't sit in the control
    // until the next prompt (spec §10).
    private async Task<string?> AskPasswordAsync(string fileName)
    {
        PasswordPromptText.Text = string.Format(ResourceLoader.GetString("PasswordPromptFormat"), fileName);
        PasswordInput.Password = "";
        ContentDialogResult result = await ShowDialogAsync(PasswordDialog);
        string password = PasswordInput.Password;
        PasswordInput.Password = "";
        return result == ContentDialogResult.Primary ? password : null;
    }

    private void SetTitleFromFolderOf(string? pdfPath)
    {
        Title = WindowTitle.Format(
            pdfPath is null ? null : Path.GetDirectoryName(pdfPath),
            ResourceLoader.GetString("WindowTitle"),
            ResourceLoader.GetString("WindowTitleWithFolderFormat"));
    }

    // A PDF loaded, removed, restored by undo or moved in the list.
    private void ViewModel_PagesChanged(object? sender, EventArgs e)
    {
        _thumbnails.Refresh();
        RefreshNamingExample();
        if (_previewItem is { } page && !ViewModel.Documents.Contains(page.Document))
        {
            ClosePreview();
        }
    }

    // The list row's second line: "48 pages · 12 ticked", or why the PDF didn't open.
    public static string DocumentDetailText(int pageCount, int checkedPageCount, bool hasFailed) =>
        hasFailed
            ? ResourceLoader.GetString("DocumentDetailFailed")
            : string.Format(
                ResourceLoader.GetString("DocumentDetailFormat"), PageCountConverter.Describe(pageCount), checkedPageCount);

    private void RemoveDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DocumentGroup document)
        {
            // Removing the last PDF takes the list and this button with it, and focus would fall to
            // Undo, where Enter would put the PDF back. A row left behind takes focus by itself.
            if (ViewModel.Documents.Count == 1)
            {
                FocusRootGrid();
            }

            ViewModel.RemoveDocument(document);
        }
    }

    private void DocumentsList_ItemClick(object sender, ItemClickEventArgs e) => ScrollGridTo(e.ClickedItem);

    private void DocumentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (DocumentGroup document in e.RemovedItems.OfType<DocumentGroup>())
        {
            document.IsSelected = false;
        }

        foreach (DocumentGroup document in e.AddedItems.OfType<DocumentGroup>())
        {
            document.IsSelected = true;
        }

        ScrollGridTo(DocumentsList.SelectedItem);
    }

    // The section's header, not its first page, goes to the top, so the PDF's name shows. A PDF
    // that didn't open has no pages to scroll to; its header is where its neighbours put it.
    private void ScrollGridTo(object? document)
    {
        if (document is not DocumentGroup { Pages: [PageItem firstPage, ..] })
        {
            return;
        }

        PagesGridView.ScrollIntoView(firstPage, ScrollIntoViewAlignment.Leading);
        PagesGridView.UpdateLayout();
        if (PagesGridView.ContainerFromItem(firstPage) is { } container &&
            PagesGridView.GroupHeaderContainerFromItemContainer(container) is UIElement header)
        {
            header.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
        }
    }

    private void DocumentsList_DragItemsStarting(object sender, DragItemsStartingEventArgs e) =>
        _orderBeforeDrag = [.. ViewModel.Documents];

    // Emptied afterwards so it doesn't keep a since-removed PDF alive.
    private void DocumentsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        ViewModel.CommitReorder(_orderBeforeDrag);
        _orderBeforeDrag = [];
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CheckedPageCount) or nameof(MainViewModel.TotalPageCount))
        {
            CheckedPagesText.Text = ViewModel.TotalPageCount == 0
                ? ""
                : string.Format(
                    ResourceLoader.GetString("CheckedPagesFormat"), ViewModel.CheckedPageCount, ViewModel.TotalPageCount);
        }
        else if (e.PropertyName is nameof(MainViewModel.CanEdit))
        {
            RefreshOptionDependents();
        }
        else if (e.PropertyName is nameof(MainViewModel.CanSave))
        {
            RefreshSaveButton();
        }
    }

    // The empty state's Choose PDFs and the "+" both land here, and both add.
    private async void ChoosePdfsButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SettingsIdentifier = "ChoosePdfs",
        };
        foreach (string extension in SupportedExtensions.All)
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        // The empty state's button goes as the list fills, and focus would fall to Undo, where Enter
        // wouldn't save. Moved first, so a password prompt the add raises hands it back here too.
        if (sender == ChoosePdfsButton)
        {
            FocusRootGrid();
        }

        await LoadPdfsAsync(files.Select(file => file.Path).ToList());
    }

    // Refused while a save runs, so the cursor says no rather than the drop doing nothing.
    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (ViewModel.CanEdit && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    // Sorted as the right-click menu sorts a selection: a folder gives the PDFs directly inside it,
    // and anything else goes to the skipped row.
    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        // Held only while reading the items: loading can wait on a password prompt.
        DragOperationDeferral deferral = e.GetDeferral();
        IReadOnlyList<IStorageItem> items;
        try
        {
            items = await e.DataView.GetStorageItemsAsync();
        }
        finally
        {
            deferral.Complete();
        }

        // A file inside a zip, or an attachment dragged from a mail app, has no path on disk; it is
        // skipped by the name the source gives it.
        _skippedNames.AddRange(items.Where(item => item.Path.Length == 0).Select(item => item.Name));
        await AddPathsAsync(items.Where(item => item.Path.Length > 0).Select(item => item.Path).ToList());
    }

    private async void AboutButton_Click(object sender, RoutedEventArgs e) => await ShowDialogAsync(AboutDialog);

    private void HelpButton_Click(object sender, RoutedEventArgs e) => ShowHelp();

    private void ShowHelp() => HelpWindow.Show(
        this,
        ResourceLoader.GetString("WindowTitle"),
        HelpTopics.Load(ResourceLoader.GetString, HelpTopicKeys.InOrder));

    // Only one ContentDialog can be open per window, and ShowAsync throws if another still is
    // (lessons.md). The window has four, and a password prompt can come up mid-load while the
    // Replace or close dialog is open, so each waits its turn here rather than hiding a dialog the
    // user is answering. About is the exception: it only shows information, so it is closed to
    // make way, which also hands over its turn.
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (dialog != AboutDialog)
        {
            AboutDialog.Hide();
        }

        await _dialogTurn.WaitAsync();
        try
        {
            if (_windowClosed)
            {
                return ContentDialogResult.None;
            }

            dialog.XamlRoot = RootGrid.XamlRoot;
            object? focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
            ContentDialogResult result = await dialog.ShowAsync();

            // Any real control gets focus back from the dialog; these two have nowhere to go back to.
            if (focused is null || focused == RootGrid)
            {
                FocusRootGrid();
            }

            return result;
        }
        finally
        {
            _dialogTurn.Release();
        }
    }

    // Enter from RootGrid saves (RootGrid_KeyDown), but with nothing focused XAML gives focus to the
    // first tab stop, Undo, whenever it can: on Enter's save, on a return from another window, and a
    // ContentDialog has nothing to give focus back to. On Undo, Enter stops saving. XAML can't go back
    // to nothing focused, so the grid takes focus itself: the keys come from the same source and it
    // draws no rectangle. A tab stop only for this call, or Tab would pass through an invisible stop
    // and a click on empty space would take focus from the control in use.
    private void FocusRootGrid()
    {
        if (_windowClosed)
        {
            return;
        }

        RootGrid.IsTabStop = true;
        RootGrid.Focus(FocusState.Programmatic);
        RootGrid.IsTabStop = false;
    }

    private void OptionsHeaderButton_Click(object sender, RoutedEventArgs e) => SetOptionsExpanded(!_optionsExpanded);

    private void UndoButton_Click(object sender, RoutedEventArgs e) => UndoLastChange();

    private void SelectAll_Click(object sender, RoutedEventArgs e) => ViewModel.CheckAllPages();

    private void SelectNone_Click(object sender, RoutedEventArgs e) => ViewModel.CheckNoPages();

    private void InvertSelection_Click(object sender, RoutedEventArgs e) => ViewModel.InvertChecks();

    private void SelectOdd_Click(object sender, RoutedEventArgs e) => ViewModel.CheckOddPages();

    private void SelectEven_Click(object sender, RoutedEventArgs e) => ViewModel.CheckEvenPages();

    private void ApplyRangeButton_Click(object sender, RoutedEventArgs e) => ApplyPageRange();

    private void PageRangeBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ApplyPageRange();
            e.Handled = true;
        }
    }

    private void ApplyPageRange()
    {
        if (!ViewModel.TryCheckRange(PageRangeBox.Text))
        {
            FlyoutBase.ShowAttachedFlyout(PageRangeBox);
        }
    }

    // The checkbox has already toggled itself; this records the change, undoably. Set back from the
    // page afterwards, so a toggle the view model refused (during a save) doesn't stay showing.
    private void PageCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var checkBox = (CheckBox)sender;
        if (checkBox.Tag is PageItem page)
        {
            ViewModel.SetPageChecked(page, checkBox.IsChecked == true);
            checkBox.IsChecked = page.IsChecked;
        }
    }

    // Source of truth stays GridView.SelectedItems; this mirrors it onto each PageItem so the
    // DataTemplate can draw the highlight.
    private void PagesGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (PageItem page in e.RemovedItems.OfType<PageItem>())
        {
            page.IsSelected = false;
        }

        foreach (PageItem page in e.AddedItems.OfType<PageItem>())
        {
            page.IsSelected = true;
        }
    }

    // On the tile's root rather than per button, so moving between the tile and its own buttons
    // never fires Exited (app-conventions.md → Tiles over images).
    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e) => SetChromeOpacity(sender, 1.0);

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e) => SetChromeOpacity(sender, 0.55);

    private static void SetChromeOpacity(object tile, double opacity)
    {
        if (((FrameworkElement)tile).FindName("ChromePill") is Border pill)
        {
            pill.Opacity = opacity;
        }
    }

    private void RotateLeftButton_Click(object sender, RoutedEventArgs e) => RotateTile(sender, clockwise: false);

    private void RotateRightButton_Click(object sender, RoutedEventArgs e) => RotateTile(sender, clockwise: true);

    private void RotateTile(object button, bool clockwise)
    {
        if (((FrameworkElement)button).Tag is PageItem page)
        {
            ViewModel.Rotate([page], clockwise);
        }
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PageItem page)
        {
            await ShowPreviewAsync(page);
        }
    }

    // DoubleTapped bubbles up from the tile's buttons and checkbox too, so two quick clicks on
    // one of them would also open the preview underneath (lessons.md → Routed events).
    private async void Tile_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && HasButtonAncestor(source))
        {
            return;
        }

        if (((FrameworkElement)sender).Tag is PageItem page)
        {
            await ShowPreviewAsync(page);
        }
    }

    private static bool HasButtonAncestor(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    // Rendered afresh at the window's own pixel size rather than enlarging the thumbnail. A page
    // that failed as a thumbnail would only fail again, so it doesn't open.
    private async Task ShowPreviewAsync(PageItem page)
    {
        if (page.Document.Renderer is not { } renderer || page.HasError)
        {
            return;
        }

        _requestedPreview = page;
        uint longEdgePixels = (uint)Math.Round(
            Math.Max(RootGrid.ActualWidth, RootGrid.ActualHeight) * RootGrid.XamlRoot.RasterizationScale);
        BitmapImage image;
        try
        {
            byte[] png = await Task.Run(() => renderer.RenderScaledPngAsync(page.PageNumber, longEdgePixels));
            image = await BitmapImages.FromEncodedAsync(png);
        }
        catch (Exception ex)
        {
            // Best-effort, like the siblings' preview: the tile is still there to work with.
            AppLog.LogWarning("ShowPreviewAsync", $"{page.Document.FileName} p{page.PageNumber}: {ex}");
            if (_requestedPreview == page)
            {
                _requestedPreview = null;
            }

            return;
        }

        // Stepped to another page, or closed, while this one rendered.
        if (_requestedPreview != page)
        {
            return;
        }

        _requestedPreview = null;
        _previewItem = page;
        PreviewImage.Source = image;
        PreviewIndicatorText.Text = string.Format(
            ResourceLoader.GetString("PreviewIndicatorFormat"), page.Document.FileName, page.PageNumber, page.Document.PageCount);
        PreviewOverlay.Visibility = Visibility.Visible;
        RefreshPreviewState();
    }

    // ←/→ step through the whole grid, across documents, and stop at the ends rather than wrapping.
    // Steps on from a page still rendering, so pressing → quickly twice moves two pages.
    private async Task StepPreviewAsync(int delta)
    {
        if ((_requestedPreview ?? _previewItem) is { } from &&
            ViewModel.FindPreviewablePage(from.Index, delta) is { } target)
        {
            await ShowPreviewAsync(target);
        }
    }

    private void RefreshPreviewState()
    {
        if (_previewItem is null)
        {
            return;
        }

        PreviewCheckBox.IsChecked = _previewItem.IsChecked;
        FitPreviewImage();
    }

    private void FitPreviewImage()
    {
        if (_previewItem is null || PreviewImage.Source is not BitmapImage image)
        {
            return;
        }

        // Zero, not negative, on the first open: the overlay hasn't been laid out yet, and its
        // SizeChanged calls this again once it has.
        var area = new Size(
            Math.Max(0, PreviewOverlay.ActualWidth - PreviewImage.Margin.Left - PreviewImage.Margin.Right),
            Math.Max(0, PreviewOverlay.ActualHeight - PreviewImage.Margin.Top - PreviewImage.Margin.Bottom));
        double scale = RotatedFit.Scale(area, new Size(image.PixelWidth, image.PixelHeight), _previewItem.RotationDegrees);
        PreviewImageTransform.Rotation = _previewItem.RotationDegrees;
        PreviewImageTransform.ScaleX = scale;
        PreviewImageTransform.ScaleY = scale;
    }

    private void PreviewOverlay_Tapped(object sender, TappedRoutedEventArgs e) => ClosePreview();

    private void PreviewCloseButton_Click(object sender, RoutedEventArgs e) => ClosePreview();

    private void PreviewRotateLeftButton_Click(object sender, RoutedEventArgs e) => RotatePreview(clockwise: false);

    private void PreviewRotateRightButton_Click(object sender, RoutedEventArgs e) => RotatePreview(clockwise: true);

    private void RotatePreview(bool clockwise)
    {
        if (_previewItem is not null)
        {
            ViewModel.Rotate([_previewItem], clockwise);
            RefreshPreviewState();
        }
    }

    private void PreviewCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_previewItem is not null)
        {
            ViewModel.SetPageChecked(_previewItem, PreviewCheckBox.IsChecked == true);
            RefreshPreviewState();
        }
    }

    private void StopTappedPropagation(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void ClosePreview()
    {
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        _previewItem = null;
        _requestedPreview = null;
    }

    // The preview overlay shows rotation and the include box itself, so it follows an undo too.
    private void UndoLastChange()
    {
        ViewModel.Undo();
        RefreshPreviewState();
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Typing in the range or file-name box: R is a letter there, Ctrl+Z undoes the typing, and
        // Enter in the range box applies the range.
        if (e.OriginalSource is TextBox or PasswordBox)
        {
            return;
        }

        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        bool previewOpen = PreviewOverlay.Visibility == Visibility.Visible;

        switch (e.Key)
        {
            case VirtualKey.Escape when previewOpen:
                ClosePreview();
                e.Handled = true;
                break;

            case VirtualKey.Left when previewOpen:
                _ = StepPreviewAsync(-1);
                e.Handled = true;
                break;

            case VirtualKey.Right when previewOpen:
                _ = StepPreviewAsync(1);
                e.Handled = true;
                break;

            case VirtualKey.R when previewOpen:
                RotatePreview(clockwise: !shift);
                e.Handled = true;
                break;

            case VirtualKey.R:
                ViewModel.Rotate(PagesGridView.SelectedItems.OfType<PageItem>().ToList(), clockwise: !shift);
                e.Handled = true;
                break;

            case VirtualKey.Z when ctrl:
                UndoLastChange();
                e.Handled = true;
                break;

            // Not from a focused button or list box, where Enter already means "press this one".
            case VirtualKey.Enter when e.OriginalSource is not (ButtonBase or ComboBox or ComboBoxItem):
                _ = ExportThenOfferRatingAsync();
                e.Handled = true;
                break;
        }
    }
}
