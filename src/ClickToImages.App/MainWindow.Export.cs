using System.Diagnostics;
using Bureau.Common;
using Bureau.Common.Messages;
using ClickToImages.App.Diagnostics;
using ClickToImages.App.State;
using ClickToImages.App.ViewModels;
using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace ClickToImages.App;

// The options panel, the save flow and copy-to-clipboard (spec §4.3, M3). The grid itself is in
// MainWindow.xaml.cs.
public sealed partial class MainWindow
{
    // Stands in for the page count in the naming example when no PDF is open: two digits, so the
    // example shows {page}'s zero-padding.
    private const int ExamplePageCount = 12;

    private ExportOptions _options = AppSettings.Load();

    private CancellationTokenSource? _exportCancellation;
    private Task? _exportRun;
    private bool _closeConfirmed;

    // Set while the close dialog's Save waits for PDFs still opening.
    private CancellationTokenSource? _saveAfterLoadingWait;
    private bool _closeWithoutSaving;

    // Also subscribes the controls' handlers, last, so setting them here doesn't echo back.
    private void ShowOptionsInControls()
    {
        SelectTag(ResolutionCombo, _options.Resolution);
        SelectTag(FormatCombo, _options.Format);
        JpegQualitySlider.Value = _options.JpegQuality;
        TransparentCheckBox.IsChecked = _options.TransparentBackground;
        SelectTag(OutputLocationCombo, _options.OutputLocation);
        NamingPatternBox.Text = _options.NamingPattern;
        RefreshOptionDependents();

        ResolutionCombo.SelectionChanged += (_, _) =>
            UpdateOptions(_options with { Resolution = SelectedTag<ResolutionPreset>(ResolutionCombo) });
        FormatCombo.SelectionChanged += (_, _) =>
            UpdateOptions(_options with { Format = SelectedTag<PageImageFormat>(FormatCombo) });
        JpegQualitySlider.ValueChanged += (_, e) =>
            UpdateOptions(_options with { JpegQuality = (int)Math.Round(e.NewValue) });
        TransparentCheckBox.Click += (_, _) =>
            UpdateOptions(_options with { TransparentBackground = TransparentCheckBox.IsChecked == true });
        OutputLocationCombo.SelectionChanged += OutputLocationCombo_SelectionChanged;
        NamingPatternBox.TextChanged += (_, _) => UpdateOptions(_options with { NamingPattern = NamingPatternBox.Text });
    }

    private static void SelectTag(ComboBox combo, Enum value) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == value.ToString());

    private static T SelectedTag<T>(ComboBox combo) where T : struct, Enum =>
        Enum.Parse<T>((string)((ComboBoxItem)combo.SelectedItem).Tag);

    private void UpdateOptions(ExportOptions options)
    {
        _options = options;
        RefreshOptionDependents();
    }

    // The other option controls bind IsEnabled to CanEdit; these two also depend on the format, so
    // they are set here, and again when a save starts or ends.
    private void RefreshOptionDependents()
    {
        JpegQualitySlider.IsEnabled = ViewModel.CanEdit && _options.Format == PageImageFormat.Jpeg;
        TransparentCheckBox.IsEnabled = ViewModel.CanEdit && _options.Format.SupportsTransparency();

        bool chosenFolder = _options.OutputLocation == OutputLocation.ChosenFolder;
        ChooseFolderButton.Visibility = chosenFolder ? Visibility.Visible : Visibility.Collapsed;
        ChosenFolderText.Text = chosenFolder ? _options.ChosenFolder : "";
        ToolTipService.SetToolTip(ChosenFolderText, chosenFolder ? _options.ChosenFolder : null);

        RefreshNamingExample();
    }

    // The first page of the first open PDF, as the current options would name it, or what is
    // wrong with the pattern.
    private void RefreshNamingExample()
    {
        FileNamePatternProblem? problem = _options.NamingProblem;
        NamingExampleText.Visibility = problem is null ? Visibility.Visible : Visibility.Collapsed;
        NamingProblemText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        if (problem is not null)
        {
            NamingProblemText.Text = DescribeNamingProblem(problem.Value);
            return;
        }

        DocumentGroup? first = ViewModel.Documents.FirstOrDefault(document => !document.HasFailed);
        string pdfPath = first?.Path ?? ResourceLoader.GetString("ExamplePdfName") + ".pdf";
        string fileName = _options.FileName(
            Path.GetFileNameWithoutExtension(pdfPath), 1, first?.PageCount ?? ExamplePageCount);
        NamingExampleText.Text = string.Format(ResourceLoader.GetString("NamingExampleFormat"), fileName);
    }

    private static string DescribeNamingProblem(FileNamePatternProblem problem) => problem switch
    {
        FileNamePatternProblem.Empty => ResourceLoader.GetString("NamingProblemEmpty"),
        FileNamePatternProblem.UnknownToken => ResourceLoader.GetString("NamingProblemUnknownToken"),
        FileNamePatternProblem.UnclosedBrace => ResourceLoader.GetString("NamingProblemUnclosedBrace"),
        _ => ResourceLoader.GetString("NamingProblemInvalidCharacter"),
    };

    // Picking "A folder I choose" with no folder chosen yet asks for one straight away; cancelling
    // that picker puts the previous location back.
    private async void OutputLocationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OutputLocation location = SelectedTag<OutputLocation>(OutputLocationCombo);
        if (location == OutputLocation.ChosenFolder && _options.ChosenFolder is null)
        {
            string? folder = await PickExportFolderAsync();
            if (folder is null)
            {
                SelectTag(OutputLocationCombo, _options.OutputLocation);
                return;
            }

            UpdateOptions(_options with { OutputLocation = location, ChosenFolder = folder });
            return;
        }

        UpdateOptions(_options with { OutputLocation = location });
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (await PickExportFolderAsync() is { } folder)
        {
            UpdateOptions(_options with { ChosenFolder = folder });
        }
    }

    private async Task<string?> PickExportFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SettingsIdentifier = "ExportFolder",
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void SetOptionsExpanded(bool expanded)
    {
        _optionsExpanded = expanded;
        OptionsBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        OptionsHeaderChevron.Glyph = expanded ? "" : "";
    }

    private void RefreshSaveButton() => SaveButton.IsEnabled = ViewModel.CanSave;

    // Save disables itself for the export, and focus would go on to Help, where the next Enter opens
    // Help. The grid holds focus meanwhile, so Save gets it back the way it had it, a click drawing no
    // rectangle, and focus the user moved during the export stays where they put it.
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        FocusState focusState = SaveButton.FocusState;
        bool hadFocus = focusState != FocusState.Unfocused;
        if (hadFocus)
        {
            FocusRootGrid();
        }

        await ExportThenOfferRatingAsync();

        if (hadFocus && !_windowClosed && FocusManager.GetFocusedElement(RootGrid.XamlRoot) == RootGrid)
        {
            SaveButton.Focus(focusState);
        }
    }

    // Only an export the user asked for directly, never the save-then-close path, where a Store
    // dialog would hold the closing window open. And only a clean one: any of
    // MainViewModel.HasLoadProblems means this session let the user down, and the Store prompt must
    // never reach them then (Bureau.Common RatingPrompt).
    private async Task ExportThenOfferRatingAsync()
    {
        bool hadProblems = ViewModel.HasLoadProblems(_skippedNames);
        if (await ExportAsync() && !hadProblems)
        {
            await RatingPrompt.RequestOnceAsync(this);
        }
    }

    private void CancelExportButton_Click(object sender, RoutedEventArgs e) => _exportCancellation?.Cancel();

    // True only when every planned image was written. Locked from the first click of Save to the end
    // of the export, dialogs included.
    private Task<bool> ExportAsync() => ViewModel.RunSaveAsync(PlanAndRunExportAsync);

    private async Task<bool> PlanAndRunExportAsync()
    {
        // A row still saying "Saved" from the last export would read as this one's result.
        Messages.Close(ErrorMessageKey);
        Messages.Close(SavedMessageKey);
        Messages.Close(CopiedMessageKey);
        if (ViewModel.CheckedPageCount == 0)
        {
            ShowErrorMessage(ResourceLoader.GetString("NothingCheckedMessage"));
            return false;
        }

        // Taken once, so an option changed while the export runs can't change it halfway.
        ExportOptions options = _options;
        if (options.NamingProblem is { } problem)
        {
            ShowErrorMessage(string.Format(
                ResourceLoader.GetString("NamingProblemMessage"), DescribeNamingProblem(problem)));
            SetOptionsExpanded(true);
            return false;
        }

        IReadOnlyList<PageExport> exports = ViewModel.PlanExport(options);
        if (exports.Count == 0)
        {
            ShowErrorMessage(ResourceLoader.GetString("NothingExportableMessage"));
            return false;
        }

        if (PageExporter.FindNameClash(exports, new FileNamePattern(options.NamingPattern)) is { } clash)
        {
            // Each GetString spelled out, so ResourceKeyTests sees every key.
            string format = clash.Fix switch
            {
                NameClashFix.AddPage => ResourceLoader.GetString("DuplicateNameAddPageMessage"),
                NameClashFix.AddName => ResourceLoader.GetString("DuplicateNameAddNameMessage"),
                _ => ResourceLoader.GetString("DuplicateNameRewordMessage"),
            };
            ShowErrorMessage(string.Format(format, clash.OutputPath));
            SetOptionsExpanded(true);
            return false;
        }

        List<string> existing = exports.Select(export => export.OutputPath).Where(File.Exists).ToList();
        if (existing.Count > 0 && !await ConfirmReplaceAsync(existing))
        {
            return false;
        }

        IReadOnlyList<PageItem> unreadable = ViewModel.CheckedUnreadablePages();
        int changeVersion = ViewModel.ChangeVersion;
        if (!await RunExportAsync(exports, options.RenderSettings))
        {
            return false;
        }

        ViewModel.MarkExported(changeVersion);
        ShowExportSuccess(exports);
        if (unreadable.Count > 0)
        {
            ReportUnreadablePages(unreadable);
        }

        return true;
    }

    private async Task<bool> ConfirmReplaceAsync(List<string> existing)
    {
        ReplaceFilesDialog.Content = string.Format(
            ResourceLoader.GetString("ReplaceFilesFormat"), existing.Count, existing[0]);
        return await ShowDialogAsync(ReplaceFilesDialog) == ContentDialogResult.Primary;
    }

    private async Task<bool> RunExportAsync(IReadOnlyList<PageExport> exports, PageRenderSettings settings)
    {
        _exportCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _exportCancellation.Token;
        ShowExportProgress(0, exports.Count);
        ExportProgressPanel.Visibility = Visibility.Visible;

        // Created here, on the UI thread, so its reports come back to it.
        var progress = new Progress<int>(done => ShowExportProgress(done, exports.Count));
        try
        {
            // On the thread pool: the renderer's forced GC after each page would stall the UI.
            _exportRun = Task.Run(() => PageExporter.ExportAsync(exports, settings, progress, cancellationToken));
            await _exportRun;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // The exporter has already removed everything it wrote and put back any file it had
            // replaced, so the window's state is still good; only the user needs telling.
            AppLog.LogError("ExportAsync", ex);
            ShowErrorMessage(string.Format(ResourceLoader.GetString("ExportFailedFormat"), DescribeFailure(ex)));
            return false;
        }
        finally
        {
            // Cancel goes with the panel, and focus would go on to Help, as from Save above.
            if (CancelExportButton.FocusState != FocusState.Unfocused)
            {
                FocusRootGrid();
            }

            ExportProgressPanel.Visibility = Visibility.Collapsed;
            _exportCancellation.Dispose();
            _exportCancellation = null;
            _exportRun = null;
        }
    }

    // Windows.Data.Pdf's and WIC's COM errors often come with an empty message; the HRESULT is then
    // all there is to go on.
    private static string DescribeFailure(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? $"0x{ex.HResult:X8}" : ex.Message;

    private void ShowExportProgress(int done, int total)
    {
        ExportProgressBar.Maximum = total;
        ExportProgressBar.Value = done;
        ExportProgressText.Text = string.Format(ResourceLoader.GetString("ExportProgressFormat"), done, total);
    }

    // Open and Show in folder act on the first image written.
    private void ShowExportSuccess(IReadOnlyList<PageExport> exports)
    {
        string firstPath = exports[0].OutputPath;
        List<string> folders = exports.Select(export => Path.GetDirectoryName(export.OutputPath)!).Distinct().ToList();
        string text = exports.Count == 1
            ? string.Format(ResourceLoader.GetString("ExportedOneImageFormat"), firstPath)
            : folders.Count == 1
                ? string.Format(ResourceLoader.GetString("ExportedOneFolderFormat"), exports.Count, folders[0])
                : string.Format(ResourceLoader.GetString("ExportedManyFoldersFormat"), exports.Count, folders.Count);
        Messages.Show(
            SavedMessageKey,
            MessageSeverity.Success,
            text,
            new MessageAction(
                ResourceLoader.GetString("OpenExportedAction"),
                () => Process.Start(new ProcessStartInfo(firstPath) { UseShellExecute = true })),
            new MessageAction(
                ResourceLoader.GetString("ShowInFolderAction"),
                () => Process.Start("explorer.exe", $"/select,\"{firstPath}\"")));
    }

    private void ReportUnreadablePages(IReadOnlyList<PageItem> pages)
    {
        List<string> pageNames = pages.Select(page => string.Format(
            ResourceLoader.GetString("UnreadablePageFormat"), page.Document.FileName, page.PageNumber)).ToList();
        Messages.Show(
            SkippedMessageKey,
            MessageSeverity.Warning,
            UnreadablePagesMessage.Format(pageNames, ResourceLoader.GetString));
    }

    // Closing mid-export cancels it, which deletes what it had written, and closes without asking:
    // waiting for it, or leaving its temp files behind, would both be worse.
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed)
        {
            return;
        }

        if (_exportRun is { } exportRun)
        {
            args.Cancel = true;
            _exportCancellation?.Cancel();
            await Task.WhenAny(exportRun);
            CloseWithoutAsking();
            return;
        }

        // Closed again while Save waits for PDFs to open: as closing mid-export, it stops the wait
        // and closes without saving. The first close's handler does the closing, once it unwinds.
        if (_saveAfterLoadingWait is { } wait)
        {
            args.Cancel = true;
            _closeWithoutSaving = true;
            wait.Cancel();
            return;
        }

        if (!ViewModel.HasUnsavedChanges || ViewModel.IsSaving)
        {
            return;
        }

        args.Cancel = true;

        ContentDialogResult result = await ShowDialogAsync(CloseConfirmDialog);
        bool shouldClose = result switch
        {
            ContentDialogResult.Primary => await SaveAfterLoadingAsync() || _closeWithoutSaving,
            ContentDialogResult.Secondary => true,
            _ => false,
        };

        if (shouldClose)
        {
            CloseWithoutAsking();
        }
    }

    // The close dialog's Save, which waits for PDFs still opening rather than being refused, with
    // a row saying so whose Cancel stops the wait and leaves the window open, as a cancelled save
    // does.
    private async Task<bool> SaveAfterLoadingAsync()
    {
        if (!ViewModel.IsLoadingDocuments)
        {
            return await ExportAsync();
        }

        using var wait = new CancellationTokenSource();
        _saveAfterLoadingWait = wait;
        Messages.Show(
            WaitingToSaveMessageKey,
            MessageSeverity.Info,
            ResourceLoader.GetString("WaitingToSaveMessage"),
            new MessageAction(ResourceLoader.GetString("WaitingToSaveCancelAction"), wait.Cancel));
        try
        {
            return await ViewModel.RunSaveAfterLoadingAsync(
                () =>
                {
                    Messages.Close(WaitingToSaveMessageKey);
                    return PlanAndRunExportAsync();
                },
                wait.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            Messages.Close(WaitingToSaveMessageKey);
            _saveAfterLoadingWait = null;
        }
    }

    private void CloseWithoutAsking()
    {
        _closeConfirmed = true;
        Close();
    }

    private async void CopyPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PageItem page)
        {
            await CopyPageAsync(page);
        }
    }

    // At the Resolution option's DPI and with the page's rotation, so what pastes is what Save would
    // write. Always opaque PNG: Word and Outlook paste a transparent bitmap's background as black.
    private async Task CopyPageAsync(PageItem page)
    {
        if (page.Document.Renderer is not { } renderer)
        {
            return;
        }

        PageRenderSettings settings = _options.RenderSettings with
        {
            Format = PageImageFormat.Png,
            TransparentBackground = false,
            RotationDegrees = page.RotationDegrees,
        };
        Messages.Close(CopiedMessageKey);
        using var image = new InMemoryRandomAccessStream();
        try
        {
            await Task.Run(() => renderer.RenderToStreamAsync(page.PageNumber, settings, image));
            image.Seek(0);
            var content = new DataPackage();
            content.SetBitmap(RandomAccessStreamReference.CreateFromStream(image));
            Clipboard.SetContent(content);

            // Renders the data onto the clipboard now, so it outlives this stream and the app.
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            // The clipboard can be held open by another app, and the page may not render.
            AppLog.LogWarning("CopyPageAsync", $"{page.Document.FileName} p{page.PageNumber}: {ex}");
            ShowErrorMessage(string.Format(
                ResourceLoader.GetString("CopyFailedFormat"),
                page.PageNumber,
                page.Document.FileName,
                DescribeFailure(ex)));
            return;
        }

        Messages.Show(
            CopiedMessageKey,
            MessageSeverity.Success,
            string.Format(ResourceLoader.GetString("CopiedFormat"), page.PageNumber, page.Document.FileName));
    }
}
