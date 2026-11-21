using ClickToImages.App.State;
using ClickToImages.App.ViewModels;
using ClickToImages.Core.Export;
using ClickToImages.Core.Tests;

namespace ClickToImages.App.Tests;

// Runs over real generated PDFs: "long.pdf" has 3 pages and "short.pdf" 2, so selections that
// count pages across the whole grid instead of within each document come out visibly wrong.
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ClickToImagesAppTests").FullName;

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_OpensDocumentsInNaturalOrder_WithGridIndexesRunningAcrossThem()
    {
        var viewModel = new MainViewModel();

        await viewModel.LoadAsync([WritePdf("scan10.pdf", 2), WritePdf("scan2.pdf", 3)]);

        Assert.Equal(["scan2.pdf", "scan10.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal([0, 1, 2, 3, 4], viewModel.Pages.Select(page => page.Index));
        Assert.Equal(["scan2.pdf", "scan2.pdf", "scan2.pdf", "scan10.pdf", "scan10.pdf"],
            viewModel.Pages.Select(page => page.Document.FileName));
        Assert.Equal((5, 5), (viewModel.CheckedPageCount, viewModel.TotalPageCount));
    }

    [Fact]
    public async Task LoadAsync_CorruptPdf_BecomesAnErrorGroupWithoutStoppingTheOthers()
    {
        string corrupt = Path.Combine(_folder, "broken.pdf");
        File.WriteAllText(corrupt, "not a PDF at all");
        var viewModel = new MainViewModel();

        await viewModel.LoadAsync([corrupt, WritePdf("good.pdf", 2)]);

        DocumentGroup broken = viewModel.Documents.Single(document => document.FileName == "broken.pdf");
        Assert.True(broken.HasFailed);
        Assert.Empty(broken.Pages);
        Assert.Equal(2, viewModel.TotalPageCount);
        Assert.False(viewModel.ShowEmptyState);
    }

    // Windows' message for it is "The text associated with this error code could not be found ...
    // Invalid file, zero length. 0x8004808e", which the header showed under "Couldn't open this PDF."
    [Fact]
    public async Task LoadAsync_ZeroLengthPdf_FailsWithNoSystemMessageToShow()
    {
        string empty = Path.Combine(_folder, "empty.pdf");
        File.WriteAllBytes(empty, []);
        var viewModel = new MainViewModel();

        await viewModel.LoadAsync([empty]);

        DocumentGroup group = viewModel.Documents.Single();
        Assert.True(group.HasFailed);
        Assert.Null(group.ErrorMessage);
    }

    // The "+" button and drag-and-drop: a second batch goes after the first, even when it sorts
    // before it, and keeps the first's checks.
    [Fact]
    public async Task LoadAsync_SecondBatch_AddsAfterTheFirst_KeepingItsPagesAndChecks()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.SetPageChecked(viewModel.Pages[0], false);

        await viewModel.LoadAsync([WritePdf("added.pdf", 2)]);

        Assert.Equal(["long.pdf", "short.pdf", "added.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], viewModel.Pages.Select(page => page.Index));
        Assert.Equal([0, 1, 2], viewModel.Documents.Select(document => document.Id));
        Assert.Equal((6, 7), (viewModel.CheckedPageCount, viewModel.TotalPageCount));
    }

    // Listed in the skipped message as "already added: LONG.PDF", not opened twice.
    [Fact]
    public async Task LoadAsync_PdfAlreadyLoaded_IsSkippedAsAlreadyAdded_EvenInAnotherCase()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        await viewModel.LoadAsync([Path.Combine(_folder, "LONG.PDF"), WritePdf("added.pdf", 1)]);

        Assert.Equal(["long.pdf", "short.pdf", "added.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal(6, viewModel.TotalPageCount);
        Assert.Equal([new SkippedDocument("LONG.PDF", SkipReason.AlreadyAdded)], viewModel.SkippedDocuments);
    }

    [Fact]
    public async Task LoadAsync_SamePathTwiceInOneBatch_LoadsItOnce_AndReportsTheSecond()
    {
        var viewModel = new MainViewModel();
        string path = WritePdf("once.pdf", 1);

        await viewModel.LoadAsync([path, path]);

        Assert.Single(viewModel.Documents);
        Assert.Equal([SkipReason.AlreadyAdded], viewModel.SkippedDocuments.Select(skipped => skipped.Reason));
    }

    // A batch holding a removed PDF is loading when the user undoes the removal. Here the undo
    // runs during the batch's password prompt for locked.pdf, which sorts before x.pdf.
    [Fact]
    public async Task LoadAsync_UndoMidBatchRestoresAPdfTheBatchHolds_OpensItOnce()
    {
        string locked = Path.Combine(_folder, "locked.pdf");
        TestPdf.WriteEncrypted(locked, "secret", "owner", (200, 300, 0));
        string x = WritePdf("x.pdf", 1);
        MainViewModel viewModel = null!;
        viewModel = new MainViewModel(_ =>
        {
            viewModel.Undo();
            return Task.FromResult<string?>(null);
        });
        await viewModel.LoadAsync([x]);
        viewModel.RemoveDocument(viewModel.Documents[0]);

        await viewModel.LoadAsync([locked, x]);

        Assert.Equal(["x.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Contains(new SkippedDocument("x.pdf", SkipReason.AlreadyAdded), viewModel.SkippedDocuments);
    }

    [Fact]
    public async Task LoadAsync_PdfThatWontOpen_IsReportedOnce()
    {
        string corrupt = Path.Combine(_folder, "broken.pdf");
        File.WriteAllText(corrupt, "not a PDF at all");
        List<string> reported = [];
        var viewModel = new MainViewModel(openFailed: (path, _) => reported.Add(path));

        await viewModel.LoadAsync([corrupt, WritePdf("good.pdf", 1)]);
        await viewModel.LoadAsync([WritePdf("later.pdf", 1)]);

        Assert.Equal([corrupt], reported);
    }

    // The rating prompt rule (Gavin, 2026-10-31): turning away a file the app doesn't take is no
    // failure, so it mustn't hold the prompt back.
    [Fact]
    public async Task HasLoadProblems_OnlyAnUnsupportedFileSkipped_IsFalse()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        Assert.False(viewModel.HasLoadProblems(["notes.txt", "Photos"]));
    }

    // Nor is dropping a PDF that is already open.
    [Fact]
    public async Task HasLoadProblems_OnlyADuplicateSkipped_IsFalse()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        await viewModel.LoadAsync([viewModel.Documents[0].Path]);

        Assert.NotEmpty(viewModel.SkippedDocuments);
        Assert.False(viewModel.HasLoadProblems([]));
    }

    [Fact]
    public async Task HasLoadProblems_APdfThatWontOpen_IsTrue()
    {
        string corrupt = Path.Combine(_folder, "broken.pdf");
        File.WriteAllText(corrupt, "not a PDF at all");
        var viewModel = new MainViewModel();

        await viewModel.LoadAsync([corrupt]);

        Assert.True(viewModel.HasLoadProblems([]));
    }

    // A PDF dragged out of a zip has no path, so the window skips it by name; it is still a PDF
    // that couldn't be opened, unlike the unsupported file beside it.
    [Fact]
    public async Task HasLoadProblems_APdfSkippedByNameForHavingNoPath_IsTrue()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        Assert.True(viewModel.HasLoadProblems(["notes.txt", "Report.PDF"]));
    }

    [Fact]
    public async Task HasLoadProblems_ADeclinedPassword_IsTrue()
    {
        string locked = Path.Combine(_folder, "locked.pdf");
        TestPdf.WriteEncrypted(locked, "secret", "owner", (200, 300, 0));
        var viewModel = new MainViewModel(_ => Task.FromResult<string?>(null));

        await viewModel.LoadAsync([locked, WritePdf("plain.pdf", 1)]);

        Assert.True(viewModel.HasLoadProblems([]));
    }

    [Fact]
    public async Task HasLoadProblems_ATickedPageThatCouldntBeRead_IsTrue()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        viewModel.Pages[2].HasError = true;

        Assert.True(viewModel.HasLoadProblems([]));
    }

    [Fact]
    public async Task RemoveDocument_TakesItsPagesOutOfTheGridAndCounts_AndRenumbersTheRest()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.SetPageChecked(viewModel.Pages[3], false);

        viewModel.RemoveDocument(viewModel.Documents[0]);

        Assert.Equal(["short.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal(["short.pdf p1", "short.pdf p2"], viewModel.Pages.Select(page => $"{page.Document.FileName} p{page.PageNumber}"));
        Assert.Equal([0, 1], viewModel.Pages.Select(page => page.Index));
        Assert.Equal((1, 2), (viewModel.CheckedPageCount, viewModel.TotalPageCount));
    }

    [Fact]
    public async Task RemoveDocument_Undo_PutsItBackInPlace_WithItsTicksAndRotations()
    {
        string middle = WritePdf("middle.pdf", 1);
        MainViewModel viewModel = await LoadLongAndShortAsync();
        await viewModel.LoadAsync([middle]);
        DocumentGroup longPdf = viewModel.Documents[0];
        viewModel.SetPageChecked(longPdf.Pages[1], false);
        viewModel.Rotate([longPdf.Pages[2]], clockwise: true);

        viewModel.RemoveDocument(longPdf);
        viewModel.Undo();

        Assert.Equal(["long.pdf", "short.pdf", "middle.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Same(longPdf.Pages[0], viewModel.Pages[0]);
        Assert.Equal([0, 1, 2, 3, 4, 5], viewModel.Pages.Select(page => page.Index));
        Assert.False(longPdf.Pages[1].IsChecked);
        Assert.Equal(90, longPdf.Pages[2].RotationDegrees);
        Assert.Equal((5, 6), (viewModel.CheckedPageCount, viewModel.TotalPageCount));
    }

    [Fact]
    public async Task RemoveDocument_TheLast_ShowsTheEmptyState()
    {
        var viewModel = new MainViewModel();
        await viewModel.LoadAsync([WritePdf("only.pdf", 1)]);

        viewModel.RemoveDocument(viewModel.Documents[0]);

        Assert.True(viewModel.ShowEmptyState);
        Assert.Empty(viewModel.Pages);
    }

    // Removed, then added again by path: undoing the removal must not load a second copy.
    [Fact]
    public async Task RemoveDocument_UndoAfterTheSamePdfWasAddedAgain_KeepsOneCopy()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        string longPath = viewModel.Documents[0].Path;
        viewModel.RemoveDocument(viewModel.Documents[0]);
        await viewModel.LoadAsync([longPath]);

        viewModel.Undo();

        Assert.Equal(["short.pdf", "long.pdf"], viewModel.Documents.Select(document => document.FileName));
    }

    // ListView's drag moves the item itself; CommitReorder then renumbers and records it.
    [Fact]
    public async Task CommitReorder_MovesTheSectionsPages_AndUndoMovesThemBack()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        List<DocumentGroup> before = [.. viewModel.Documents];

        viewModel.Documents.Move(1, 0);
        viewModel.CommitReorder(before);

        Assert.Equal(["short.pdf p1", "short.pdf p2", "long.pdf p1", "long.pdf p2", "long.pdf p3"],
            viewModel.Pages.Select(page => $"{page.Document.FileName} p{page.PageNumber}"));
        Assert.Equal([0, 1, 2, 3, 4], viewModel.Pages.Select(page => page.Index));
        Assert.True(viewModel.HasUnsavedChanges);

        viewModel.Undo();

        Assert.Equal(["long.pdf", "short.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal("long.pdf", viewModel.Pages[0].Document.FileName);
    }

    [Fact]
    public async Task CommitReorder_OrderUnchanged_AddsNoUndoStep()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.Rotate([viewModel.Pages[0]], clockwise: true);

        viewModel.CommitReorder([.. viewModel.Documents]);
        viewModel.Undo();

        Assert.Equal(0, viewModel.Pages[0].RotationDegrees);
    }

    // What WinUI's ListView actually does on a row drag (Bureau.Common lessons.md): Remove then
    // Insert, never Move. The window takes the order before from DragItemsStarting, which the list
    // raises because CanDragItems is on, and commits on DragItemsCompleted.
    [Fact]
    public async Task CommitReorder_AfterARemoveThenInsertDrag_IsOneUndoStep()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        await viewModel.LoadAsync([WritePdf("third.pdf", 1)]);
        viewModel.Rotate([viewModel.Pages[0]], clockwise: true);
        List<DocumentGroup> before = [.. viewModel.Documents];

        DocumentGroup dragged = viewModel.Documents[2];
        viewModel.Documents.RemoveAt(2);
        viewModel.Documents.Insert(0, dragged);
        viewModel.CommitReorder(before);
        viewModel.Undo();

        Assert.Equal(["long.pdf", "short.pdf", "third.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal("long.pdf", viewModel.Pages[0].Document.FileName);
        Assert.Equal(90, viewModel.Pages[0].RotationDegrees);
    }

    [Fact]
    public async Task Edits_DuringASave_AreIgnored()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.Rotate([viewModel.Pages[1]], clockwise: true);
        int versionBefore = viewModel.ChangeVersion;

        await viewModel.RunSaveAsync(() =>
        {
            Assert.False(viewModel.CanEdit);
            viewModel.SetPageChecked(viewModel.Pages[0], false);
            viewModel.CheckOddPages();
            Assert.False(viewModel.TryCheckRange("1"));
            viewModel.Rotate([viewModel.Pages[2]], clockwise: true);
            viewModel.RemoveDocument(viewModel.Documents[0]);
            viewModel.Undo();
            return Task.FromResult(true);
        });

        Assert.Equal(["long.pdf", "short.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal((5, 5), (viewModel.CheckedPageCount, viewModel.TotalPageCount));
        Assert.Equal([0, 90, 0, 0, 0], viewModel.Pages.Select(page => page.RotationDegrees));
        Assert.Equal(versionBefore, viewModel.ChangeVersion);
    }

    // A folder dropped just before Save reaches LoadAsync only once it has been listed, by which
    // time the save may be running. Its PDFs must arrive after the save, not be dropped.
    [Fact]
    public async Task LoadAsync_DuringASave_WaitsForItThenLoads()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        string added = WritePdf("added.pdf", 1);
        var finishSave = new TaskCompletionSource<bool>();

        Task<bool> save = viewModel.RunSaveAsync(() => finishSave.Task);
        Task load = viewModel.LoadAsync([added]);
        await Task.Delay(200);

        Assert.False(load.IsCompleted);
        Assert.Equal(["long.pdf", "short.pdf"], viewModel.Documents.Select(document => document.FileName));

        finishSave.SetResult(true);
        await save;
        await load.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["long.pdf", "short.pdf", "added.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal(6, viewModel.TotalPageCount);
    }

    // A save that started part-way through a load would export a list still changing under it.
    [Fact]
    public async Task Save_WhileALoadRuns_IsRefusedUntilItEnds()
    {
        var finishPassword = new TaskCompletionSource<string?>();
        var viewModel = new MainViewModel(askPassword: _ => finishPassword.Task);
        await viewModel.LoadAsync([WritePdf("first.pdf", 1)]);
        List<bool> canSaveSeen = [];
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CanSave))
            {
                canSaveSeen.Add(viewModel.CanSave);
            }
        };
        bool saveRan = false;

        // The password prompt holds the load open for as long as the test likes.
        Task load = viewModel.LoadAsync([WriteLockedPdf()]);

        Assert.False(viewModel.CanSave);
        Assert.False(await viewModel.RunSaveAsync(() =>
        {
            saveRan = true;
            return Task.FromResult(true);
        }));
        Assert.False(saveRan);

        finishPassword.SetResult(null);
        await load;

        Assert.True(viewModel.CanSave);
        Assert.Equal([false, true], canSaveSeen);
        Assert.True(await viewModel.RunSaveAsync(() => Task.FromResult(true)));
    }

    // The close dialog's Save: the user has chosen to leave, so it waits for the load rather than
    // being refused, and saves what the load added.
    [Fact]
    public async Task RunSaveAfterLoadingAsync_WaitsForTheLoadThenSaves()
    {
        var finishPassword = new TaskCompletionSource<string?>();
        var viewModel = new MainViewModel(askPassword: _ => finishPassword.Task);
        await viewModel.LoadAsync([WritePdf("first.pdf", 1)]);
        Task load = viewModel.LoadAsync([WriteLockedPdf(), WritePdf("second.pdf", 2)]);
        int? pagesWhenSaved = null;

        Task<bool> save = viewModel.RunSaveAfterLoadingAsync(
            () =>
            {
                pagesWhenSaved = viewModel.TotalPageCount;
                return Task.FromResult(true);
            },
            CancellationToken.None);
        await Task.Delay(200);

        Assert.False(save.IsCompleted);
        Assert.Null(pagesWhenSaved);

        finishPassword.SetResult(null);
        await load;

        Assert.True(await save.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(3, pagesWhenSaved);
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task RunSaveAfterLoadingAsync_CancelledWhileWaiting_DoesNotSave()
    {
        var finishPassword = new TaskCompletionSource<string?>();
        var viewModel = new MainViewModel(askPassword: _ => finishPassword.Task);
        await viewModel.LoadAsync([WritePdf("first.pdf", 1)]);
        Task load = viewModel.LoadAsync([WriteLockedPdf()]);
        using var cancellation = new CancellationTokenSource();
        bool saveRan = false;

        Task<bool> save = viewModel.RunSaveAfterLoadingAsync(
            () =>
            {
                saveRan = true;
                return Task.FromResult(true);
            },
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        finishPassword.SetResult(null);
        await load;
        Assert.False(saveRan);
    }

    // Every editing control's IsEnabled binding hangs on this notification, not on reading CanEdit.
    [Fact]
    public async Task RunSaveAsync_RaisesCanEditChanged_OnEntryAndExit()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        List<bool> canEditSeen = [];
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CanEdit))
            {
                canEditSeen.Add(viewModel.CanEdit);
            }
        };

        await viewModel.RunSaveAsync(() =>
        {
            Assert.Equal([false], canEditSeen);
            return Task.FromResult(true);
        });

        Assert.Equal([false, true], canEditSeen);
    }

    // The window's export catches the cancellation and returns false, as here.
    [Fact]
    public async Task Editing_ComesBackAfterACancelledSave()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        using var cancellation = new CancellationTokenSource();

        Task<bool> save = viewModel.RunSaveAsync(async () =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellation.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        });
        Assert.False(viewModel.CanEdit);
        cancellation.Cancel();

        Assert.False(await save);
        Assert.True(viewModel.CanEdit);
        viewModel.SetPageChecked(viewModel.Pages[0], false);
        Assert.Equal(4, viewModel.CheckedPageCount);
    }

    // An exception that escapes the save, not one the window turns into an error message.
    [Fact]
    public async Task Editing_ComesBackAfterAFailedSave()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        await Assert.ThrowsAsync<IOException>(() => viewModel.RunSaveAsync(() => throw new IOException("disk full")));

        Assert.True(viewModel.CanEdit);
        viewModel.Rotate([viewModel.Pages[0]], clockwise: true);
        Assert.Equal(90, viewModel.Pages[0].RotationDegrees);
    }

    [Fact]
    public async Task RunSaveAsync_WhileASaveRuns_DoesNotStartASecond()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        bool secondRan = false;

        bool first = await viewModel.RunSaveAsync(async () =>
        {
            Assert.False(await viewModel.RunSaveAsync(() =>
            {
                secondRan = true;
                return Task.FromResult(true);
            }));
            return true;
        });

        Assert.True(first);
        Assert.False(secondRan);
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task DocumentCheckedPageCount_FollowsTicksAndUndo()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        DocumentGroup longPdf = viewModel.Documents[0];
        Assert.Equal(3, longPdf.CheckedPageCount);

        viewModel.CheckOddPages();
        Assert.Equal((2, 1), (longPdf.CheckedPageCount, viewModel.Documents[1].CheckedPageCount));

        viewModel.Undo();
        Assert.Equal(3, longPdf.CheckedPageCount);
    }

    // A drop while the launch's PDFs are still opening: without waiting its turn, both batches
    // would take the same document and page indexes.
    [Fact]
    public async Task LoadAsync_TwoBatchesAtOnce_LoadOneAfterTheOther()
    {
        var viewModel = new MainViewModel();
        string first = WritePdf("first.pdf", 2);
        string second = WritePdf("second.pdf", 3);

        await Task.WhenAll(viewModel.LoadAsync([first]), viewModel.LoadAsync([second]));

        Assert.Equal([0, 1, 2, 3, 4], viewModel.Pages.Select(page => page.Index));
        Assert.Equal([0, 1], viewModel.Documents.Select(document => document.Id));
    }

    [Fact]
    public async Task CheckOddPages_CountsPagesWithinEachDocument()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        viewModel.CheckOddPages();

        Assert.Equal(["long.pdf p1", "long.pdf p3", "short.pdf p1"], CheckedPages(viewModel));
        Assert.Equal(3, viewModel.CheckedPageCount);
    }

    [Fact]
    public async Task CheckEvenPages_CountsPagesWithinEachDocument()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        viewModel.CheckEvenPages();

        Assert.Equal(["long.pdf p2", "short.pdf p2"], CheckedPages(viewModel));
    }

    [Fact]
    public async Task InvertChecks_SwapsCheckedAndUnchecked()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.SetPageChecked(viewModel.Pages[1], false);

        viewModel.InvertChecks();

        Assert.Equal(["long.pdf p2"], CheckedPages(viewModel));
    }

    [Fact]
    public async Task TryCheckRange_AppliesToEveryDocument_AndAllowsPagesOnlyTheLongerOneHas()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        Assert.True(viewModel.TryCheckRange("1, 3"));

        Assert.Equal(["long.pdf p1", "long.pdf p3", "short.pdf p1"], CheckedPages(viewModel));
    }

    [Theory]
    [InlineData("4")]
    [InlineData("2-1")]
    [InlineData("first page")]
    public async Task TryCheckRange_InvalidRange_ReturnsFalseAndChangesNothing(string rangeText)
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.CheckOddPages();

        Assert.False(viewModel.TryCheckRange(rangeText));

        Assert.Equal(["long.pdf p1", "long.pdf p3", "short.pdf p1"], CheckedPages(viewModel));
    }

    [Fact]
    public async Task Undo_RevertsABulkSelectionInOneStep()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.SetPageChecked(viewModel.Pages[0], false);
        viewModel.CheckNoPages();

        viewModel.Undo();

        Assert.Equal(["long.pdf p2", "long.pdf p3", "short.pdf p1", "short.pdf p2"], CheckedPages(viewModel));
        Assert.Equal(4, viewModel.CheckedPageCount);
    }

    [Fact]
    public async Task Selection_ThatChangesNothing_AddsNoUndoStep()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.SetPageChecked(viewModel.Pages[0], false);
        viewModel.CheckNoPages();
        viewModel.CheckNoPages();

        viewModel.Undo();

        Assert.Equal(4, viewModel.CheckedPageCount);
    }

    [Fact]
    public async Task Undo_RestoresEachRotatedPage()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.Rotate([viewModel.Pages[0]], clockwise: true);
        viewModel.Rotate([viewModel.Pages[0], viewModel.Pages[4]], clockwise: false);

        Assert.Equal((0, 270), (viewModel.Pages[0].RotationDegrees, viewModel.Pages[4].RotationDegrees));

        viewModel.Undo();

        Assert.Equal((90, 0), (viewModel.Pages[0].RotationDegrees, viewModel.Pages[4].RotationDegrees));
    }

    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(3, -1, 1)]
    public async Task FindPreviewablePage_StepsPastAPageThatFailedToRender(int fromIndex, int step, int expectedIndex)
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.Pages[2].HasError = true;

        Assert.Equal(expectedIndex, viewModel.FindPreviewablePage(fromIndex, step)?.Index);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(4, 1)]
    public async Task FindPreviewablePage_PastEitherEnd_IsNull(int fromIndex, int step)
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        Assert.Null(viewModel.FindPreviewablePage(fromIndex, step));
    }

    [Fact]
    public async Task PlanExport_NamesEachCheckedPageFromItsOwnDocument_WithItsRotation()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.SetPageChecked(viewModel.Pages[1], false);
        viewModel.Rotate([viewModel.Pages[3]], clockwise: true);

        IReadOnlyList<PageExport> plan = viewModel.PlanExport(new ExportOptions());

        Assert.Equal(
            [@"long\long-p1.png", @"long\long-p3.png", @"short\short-p1.png", @"short\short-p2.png"],
            plan.Select(export => Path.GetRelativePath(_folder, export.OutputPath)));
        Assert.Equal([0, 0, 90, 0], plan.Select(export => export.RotationDegrees));
        Assert.Equal([1, 3, 1, 2], plan.Select(export => export.PageNumber));
    }

    [Fact]
    public async Task PlanExport_LeavesOutACheckedPageThatFailedToRender_AndReportsIt()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.Pages[2].HasError = true;

        IReadOnlyList<PageExport> plan = viewModel.PlanExport(new ExportOptions());

        Assert.DoesNotContain(plan, export => export.OutputPath.EndsWith("long-p3.png", StringComparison.Ordinal));
        Assert.Equal([viewModel.Pages[2]], viewModel.CheckedUnreadablePages());
    }

    // Two report.pdf files from different folders, exported into one chosen folder.
    [Fact]
    public async Task PlanExport_SameNamedPdfsIntoOneFolder_SuffixesTheSecondInLoadOrder()
    {
        string first = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "a")).FullName, "report.pdf");
        string second = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "b")).FullName, "report.pdf");
        TestPdf.Write(first, (200, 300, 0), (200, 300, 0));
        TestPdf.Write(second, (200, 300, 0));
        var viewModel = new MainViewModel();
        await viewModel.LoadAsync([second, first]);
        var options = new ExportOptions { OutputLocation = OutputLocation.ChosenFolder, ChosenFolder = _folder };

        IReadOnlyList<PageExport> plan = viewModel.PlanExport(options);

        // LoadAsync sorts, so a\ loads first whichever order the paths came in.
        Assert.Equal(
            ["report-p1.png", "report-p2.png", "report (2)-p1.png"],
            plan.Select(export => Path.GetRelativePath(_folder, export.OutputPath)));
    }

    // b\report.pdf would sort first in one batch; added later, it is second, so it takes " (2)".
    [Fact]
    public async Task PlanExport_SameNamedPdfAddedLater_TakesTheSuffix()
    {
        string first = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "b")).FullName, "report.pdf");
        string added = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "a")).FullName, "report.pdf");
        TestPdf.Write(first, (200, 300, 0));
        TestPdf.Write(added, (200, 300, 0));
        var viewModel = new MainViewModel();
        await viewModel.LoadAsync([first]);
        await viewModel.LoadAsync([added]);
        var options = new ExportOptions { OutputLocation = OutputLocation.ChosenFolder, ChosenFolder = _folder };

        IReadOnlyList<PageExport> plan = viewModel.PlanExport(options);

        Assert.Equal(first, viewModel.Documents[0].Path);
        Assert.Equal(
            [(viewModel.Documents[0].Renderer!, "report-p1.png"), (viewModel.Documents[1].Renderer!, "report (2)-p1.png")],
            plan.Select(export => (export.Renderer, Path.GetRelativePath(_folder, export.OutputPath))));
    }

    // a\ loads first; dragged below b\, it is the second and takes " (2)".
    [Fact]
    public async Task PlanExport_SameNamedPdfsReordered_SuffixFollowsTheListOrder()
    {
        string first = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "a")).FullName, "report.pdf");
        string second = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "b")).FullName, "report.pdf");
        TestPdf.Write(first, (200, 300, 0));
        TestPdf.Write(second, (200, 300, 0));
        var viewModel = new MainViewModel();
        await viewModel.LoadAsync([first, second]);
        List<DocumentGroup> before = [.. viewModel.Documents];
        viewModel.Documents.Move(0, 1);
        viewModel.CommitReorder(before);
        var options = new ExportOptions { OutputLocation = OutputLocation.ChosenFolder, ChosenFolder = _folder };

        IReadOnlyList<PageExport> plan = viewModel.PlanExport(options);

        Assert.Equal(
            [(before[1].Renderer!, "report-p1.png"), (before[0].Renderer!, "report (2)-p1.png")],
            plan.Select(export => (export.Renderer, Path.GetRelativePath(_folder, export.OutputPath))));
    }

    [Fact]
    public async Task PlanExport_OnlyTheSecondOfTwoSameNamedPdfsChecked_KeepsItsPlainName()
    {
        string first = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "a")).FullName, "report.pdf");
        string second = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "b")).FullName, "report.pdf");
        TestPdf.Write(first, (200, 300, 0));
        TestPdf.Write(second, (200, 300, 0));
        var viewModel = new MainViewModel();
        await viewModel.LoadAsync([first, second]);
        viewModel.SetPageChecked(viewModel.Pages[0], false);
        var options = new ExportOptions { OutputLocation = OutputLocation.ChosenFolder, ChosenFolder = _folder };

        IReadOnlyList<PageExport> plan = viewModel.PlanExport(options);

        Assert.Equal([Path.Combine(_folder, "report-p1.png")], plan.Select(export => export.OutputPath));
    }

    [Fact]
    public async Task HasUnsavedChanges_OnlyOnceSomethingChanges_AndUntilAnExportFromThatState()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        Assert.False(viewModel.HasUnsavedChanges);

        viewModel.Rotate([viewModel.Pages[0]], clockwise: true);
        Assert.True(viewModel.HasUnsavedChanges);

        viewModel.MarkExported(viewModel.ChangeVersion);
        Assert.False(viewModel.HasUnsavedChanges);

        viewModel.Undo();
        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task HasUnsavedChanges_ChangeDuringTheExport_StillCounts()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();
        viewModel.CheckOddPages();
        int exportStartedFrom = viewModel.ChangeVersion;
        viewModel.Rotate([viewModel.Pages[0]], clockwise: true);

        viewModel.MarkExported(exportStartedFrom);

        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task HasUnsavedChanges_NothingChecked_IsFalse()
    {
        MainViewModel viewModel = await LoadLongAndShortAsync();

        viewModel.CheckNoPages();

        Assert.False(viewModel.HasUnsavedChanges);
    }

    private async Task<MainViewModel> LoadLongAndShortAsync()
    {
        var viewModel = new MainViewModel();
        await viewModel.LoadAsync([WritePdf("long.pdf", 3), WritePdf("short.pdf", 2)]);
        return viewModel;
    }

    // One page, password "secret": loading it waits on the view model's askPassword.
    private string WriteLockedPdf()
    {
        string path = Path.Combine(_folder, "locked.pdf");
        TestPdf.WriteEncrypted(path, "secret", "owner", (200, 300, 0));
        return path;
    }

    private string WritePdf(string name, int pageCount)
    {
        string path = Path.Combine(_folder, name);
        TestPdf.Write(path, Enumerable.Repeat((200, 300, 0), pageCount).ToArray());
        return path;
    }

    private static List<string> CheckedPages(MainViewModel viewModel) =>
        viewModel.Pages
            .Where(page => page.IsChecked)
            .Select(page => $"{page.Document.FileName} p{page.PageNumber}")
            .ToList();
}
