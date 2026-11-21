using System.Collections.ObjectModel;
using ClickToImages.App.State;
using ClickToImages.Core;
using ClickToImages.Core.Export;
using ClickToImages.Core.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClickToImages.App.ViewModels;

// The page grid's state: which documents are open, which pages are checked for export, how each
// is rotated, and the undo history over all of that. Thumbnails are ThumbnailLoader's job. Holds no
// ResourceLoader, so the tests can construct it; MainWindow turns the counts into text.
public partial class MainViewModel : ObservableObject
{
    // Asked for a password-protected PDF's password, by file name; null when the user declines.
    private readonly Func<string, Task<string?>>? _askPassword;

    // Told of each PDF that fails to open, by path, once, as its error group is made. MainWindow
    // logs it; the view model has no log of its own, so its tests don't write to the real log folder.
    private readonly Action<string, Exception>? _openFailed;
    private readonly UndoStack<Action> _undoStack = new(capacity: 20);
    private readonly List<PageItem> _pages = [];
    private readonly SemaphoreSlim _loadTurn = new(1, 1);

    // Bumped by every check, rotation, removal or reorder, undo included; compared with the
    // version the last export started from.
    private int _changeVersion;
    private int _exportedVersion;
    private int _nextDocumentId;
    private bool _isSaving;

    // LoadAsync calls not yet finished, counting those still waiting their turn: a flag set and
    // cleared by each call would read false in the gap between one batch and the next.
    private int _pendingLoads;

    // Completed when _pendingLoads falls to zero; a fresh one each time it rises from zero.
    private TaskCompletionSource _loadsEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Completed when the running save ends, for a load that arrives during it. Continuations run
    // asynchronously so the load doesn't run inside RunSaveAsync's finally, ahead of its return.
    private TaskCompletionSource _saveEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Without askPassword, a password-protected PDF is skipped without asking.
    public MainViewModel(Func<string, Task<string?>>? askPassword = null, Action<string, Exception>? openFailed = null)
    {
        _askPassword = askPassword;
        _openFailed = openFailed;
    }

    // In the PDF list's order, which the grid's sections and the " (2)" naming follow: load order
    // until the user drags a row.
    public ObservableCollection<DocumentGroup> Documents { get; } = [];

    // PDFs left out of the grid on purpose, by encryption or as already added, as opposed to
    // Documents' failed ones, which are errors (spec §4.2). MainWindow lists them with the launch's
    // skipped items.
    public List<SkippedDocument> SkippedDocuments { get; } = [];

    // Every page of every document, in grid order; PageItem.Index is the position in this list.
    public IReadOnlyList<PageItem> Pages => _pages;

    // After pages join, leave or move in the grid, with every PageItem.Index already current.
    public event EventHandler? PagesChanged;

    [ObservableProperty]
    private bool _showEmptyState = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private int _checkedPageCount;

    [ObservableProperty]
    private int _totalPageCount;

    // A selection the user has worked on and not exported since. Just opening PDFs and closing
    // again is not unsaved work, so nothing is until the first change; and with nothing checked
    // there is nothing to save either.
    public bool HasUnsavedChanges => CheckedPageCount > 0 && _changeVersion != _exportedVersion;

    public int ChangeVersion => _changeVersion;

    // Editing is locked for the whole of a save in every ClickTo app (Gavin, 2026-11-04): every
    // edit below returns early while this is set, and the window binds its editing controls to
    // CanEdit. Scrolling, the preview, Copy and Cancel stay usable.
    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanEdit => !IsSaving;

    // While any PDF is still opening, so a save never starts part-way through a load and the list
    // can't change under it, as in ClickTo: PDF (Gavin, 2026-11-04).
    public bool IsLoadingDocuments => _pendingLoads > 0;

    public bool CanSave => CheckedPageCount > 0 && !IsSaving && !IsLoadingDocuments;

    /// <summary>
    /// Runs <paramref name="save"/> with editing locked, and unlocks however it ends: done,
    /// cancelled or failed. False, without running it, while PDFs are still loading or another save
    /// holds the lock: a second Enter (one that also dismissed the Replace dialog, say) mustn't start
    /// a save alongside it. Nothing checked is left to <paramref name="save"/>, which says so.
    /// </summary>
    public async Task<bool> RunSaveAsync(Func<Task<bool>> save)
    {
        if (IsSaving || IsLoadingDocuments)
        {
            return false;
        }

        _saveEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IsSaving = true;
        try
        {
            return await save();
        }
        finally
        {
            IsSaving = false;
            _saveEnded.TrySetResult();
        }
    }

    // version is the ChangeVersion the export started from, so an edit made while it ran would still
    // count. Editing is locked during a save now, so this is only a second line of defence.
    public void MarkExported(int version) => _exportedVersion = version;

    /// <summary>
    /// One image per checked page, in grid order, named and placed by <paramref name="options"/>,
    /// which must have no naming problem. A page whose thumbnail failed would only fail again, so
    /// it is left out, and reported through <see cref="CheckedUnreadablePages"/>, rather than
    /// failing the whole export. Same-named PDFs going to one folder get " (2)" and so on, in the
    /// list's order, counting only PDFs with a page in this export, so one saved on its own keeps
    /// its name.
    /// </summary>
    public IReadOnlyList<PageExport> PlanExport(ExportOptions options)
    {
        List<PageItem> pages = _pages.Where(page => page.IsChecked && !page.HasError).ToList();

        // _pages is in the list's order, so these are too.
        List<(DocumentGroup Document, string Folder)> documents = pages
            .Select(page => page.Document)
            .Distinct()
            .Select(document => (document, options.OutputFolder(document.Path)))
            .ToList();
        IReadOnlyList<string> names = DocumentNames.Distinguish(
            documents.Select(entry => (entry.Document.Path, entry.Folder)).ToList());
        Dictionary<DocumentGroup, (string Folder, string Name)> targets = documents
            .Select((entry, i) => (entry.Document, entry.Folder, Name: names[i]))
            .ToDictionary(entry => entry.Document, entry => (entry.Folder, entry.Name));

        return pages
            .Select(page =>
            {
                (string folder, string name) = targets[page.Document];
                return new PageExport(
                    page.Document.Renderer!, page.PageNumber, page.RotationDegrees,
                    Path.Combine(folder, options.FileName(name, page.PageNumber, page.Document.PageCount)));
            })
            .ToList();
    }

    /// <summary>
    /// Whether loading or rendering let the user down: a PDF skipped for its password or
    /// encryption, one that wouldn't open, or a ticked page that couldn't be read. The rating
    /// prompt stays away then (Gavin, 2026-10-31). Turning away a file of a type the app doesn't
    /// take is no failure, nor is a PDF skipped as already added. <paramref name="skippedNames"/>
    /// is the window's skipped-by-name list: a PDF in it had no path on disk (inside a zip,
    /// say), so it couldn't be opened and counts.
    /// </summary>
    public bool HasLoadProblems(IEnumerable<string> skippedNames) =>
        skippedNames.Any(SupportedExtensions.Includes)
        || SkippedDocuments.Any(skipped => skipped.Reason != SkipReason.AlreadyAdded)
        || Documents.Any(document => document.HasFailed)
        || CheckedUnreadablePages().Count > 0;

    public IReadOnlyList<PageItem> CheckedUnreadablePages() =>
        _pages.Where(page => page.IsChecked && page.HasError).ToList();

    // Documents open one at a time in natural filename order, each joining the grid as soon as it
    // has opened, so the first one's thumbnails can start while later ones are still opening.
    // Adds to whatever is already loaded, never replacing it; a PDF already in the list (same full
    // path, any case) is listed as skipped rather than opened twice. A second call, from a drop
    // during a slow load, waits for the first, so indexes read what is already loaded. The
    // duplicate check reads Documents as each PDF comes up, not as the batch started: an undo
    // during the batch (of a removal, say) can put a PDF back that the batch also holds. The
    // batch's own set catches a repeat within it, even of a PDF that was skipped and never listed.
    // A save can't start while a batch loads (CanSave). One that reaches here during a save (a
    // folder dropped just before Save, still being listed off the UI thread) waits for the save to
    // end, then loads, so the list holds still meanwhile and the files aren't lost (Gavin,
    // 2026-11-04). It waits holding its turn, so later loads still go in the order they came.
    public async Task LoadAsync(IEnumerable<string> pdfPaths)
    {
        SetPendingLoads(_pendingLoads + 1);
        await _loadTurn.WaitAsync();
        while (IsSaving)
        {
            await _saveEnded.Task;
        }

        ShowEmptyState = false;
        try
        {
            HashSet<string> batch = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in pdfPaths.Order(NaturalSortComparer.Instance))
            {
                if (!batch.Add(Path.GetFullPath(path)) || Documents.Any(loaded => SamePath(loaded.Path, path)))
                {
                    Skip(path, SkipReason.AlreadyAdded);
                    continue;
                }

                if (await OpenDocumentAsync(path) is not { } group)
                {
                    continue;
                }

                _pages.AddRange(group.Pages);
                Documents.Add(group);
                UpdatePageCounts();
                PagesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            ShowEmptyState = Documents.Count == 0;
            _loadTurn.Release();
            SetPendingLoads(_pendingLoads - 1);
        }
    }

    private void SetPendingLoads(int count)
    {
        bool wasLoading = IsLoadingDocuments;
        _pendingLoads = count;
        if (IsLoadingDocuments == wasLoading)
        {
            return;
        }

        if (IsLoadingDocuments)
        {
            _loadsEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        else
        {
            _loadsEnded.TrySetResult();
        }

        OnPropertyChanged(nameof(IsLoadingDocuments));
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// Waits until no PDF is loading, then runs <paramref name="save"/> as
    /// <see cref="RunSaveAsync"/> does. Only the close dialog's Save waits like this: the user has
    /// already chosen to leave, so a "not yet" would strand them (Gavin, 2026-11-04). The Save
    /// button and Enter are refused while loading instead. A load that starts while this waits is
    /// waited for too. Cancelling the wait throws OperationCanceledException without saving.
    /// </summary>
    public async Task<bool> RunSaveAfterLoadingAsync(Func<Task<bool>> save, CancellationToken cancellationToken)
    {
        while (IsLoadingDocuments)
        {
            await _loadsEnded.Task.WaitAsync(cancellationToken);
        }

        return await RunSaveAsync(save);
    }

    // Null when the PDF is skipped rather than shown. A password-protected one gets exactly one
    // prompt (spec §7): a refused or declined password skips it. The password is passed straight
    // to the renderer and dropped; it is never stored or logged (spec §10).
    private async Task<DocumentGroup?> OpenDocumentAsync(string path)
    {
        Exception failure;
        try
        {
            return await OpenAsync(path, password: null);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        switch (PdfOpenFailures.Classify(failure))
        {
            case PdfOpenProblem.UnsupportedEncryption:
                return Skip(path, SkipReason.UnsupportedEncryption);

            case PdfOpenProblem.PasswordRefused:
                string? password = _askPassword is null ? null : await _askPassword(Path.GetFileName(path));
                if (password is null)
                {
                    return Skip(path, SkipReason.NoPasswordGiven);
                }

                try
                {
                    return await OpenAsync(path, password);
                }
                catch (Exception ex) when (PdfOpenFailures.Classify(ex) == PdfOpenProblem.PasswordRefused)
                {
                    return Skip(path, SkipReason.WrongPassword);
                }
                catch (Exception ex)
                {
                    return Fail(path, ex);
                }

            default:
                // A corrupt or unreadable PDF is that document's error, never the whole load's (spec §7).
                return Fail(path, failure);
        }
    }

    private DocumentGroup Fail(string path, Exception failure)
    {
        _openFailed?.Invoke(path, failure);
        return DocumentGroup.Failed(_nextDocumentId++, path, failure);
    }

    private async Task<DocumentGroup> OpenAsync(string path, string? password)
    {
        // Off the UI thread: LoadFromFileAsync reads the whole cross-reference table first.
        PdfPageRenderer renderer = await Task.Run(() => PdfPageRenderer.OpenAsync(path, password));
        return DocumentGroup.Opened(_nextDocumentId++, path, renderer, _pages.Count);
    }

    /// <summary>
    /// Takes a PDF and its pages out of the list, the grid and the counts, as one undo step. The
    /// undo step holds the document, its renderer and its pages' ticks and rotations, so undoing
    /// puts it back where it was exactly as it was. PdfPageRenderer has nothing to close:
    /// Windows.Data.Pdf's PdfDocument is released when the last reference goes, which is when this
    /// step (and any reorder step listing the PDF) falls off the undo stack, and the GC runs.
    /// </summary>
    public void RemoveDocument(DocumentGroup document)
    {
        int position = Documents.IndexOf(document);
        if (position < 0 || IsSaving)
        {
            return;
        }

        Documents.RemoveAt(position);
        SyncPagesToDocuments();
        _changeVersion++;
        _undoStack.Push(() =>
        {
            // Added again since: a second copy would be a duplicate, and the new one is the user's.
            if (Documents.Any(loaded => SamePath(loaded.Path, document.Path)))
            {
                return;
            }

            Documents.Insert(Math.Min(position, Documents.Count), document);
            SyncPagesToDocuments();
        });
    }

    /// <summary>
    /// Records a reorder the PDF list has already made in <see cref="Documents"/> (ListView's drag
    /// moves the items itself), as one undo step; <paramref name="previousOrder"/> is the list as it
    /// was before the drag. Nothing is recorded if the order didn't change. ListView raises the
    /// move as a Remove then an Insert, never a Move, so this compares orders rather than watching
    /// for one. Not refused while saving, unlike the other edits: the window turns row dragging off
    /// then, and a move the list had already made must still reach the grid.
    /// </summary>
    public void CommitReorder(IReadOnlyList<DocumentGroup> previousOrder)
    {
        if (previousOrder.SequenceEqual(Documents))
        {
            return;
        }

        SyncPagesToDocuments();
        _changeVersion++;
        _undoStack.Push(() =>
        {
            // PDFs loaded since the drag aren't in previousOrder; they keep their place at the end.
            List<DocumentGroup> restored =
                [.. previousOrder.Where(Documents.Contains), .. Documents.Except(previousOrder)];
            for (int i = 0; i < restored.Count; i++)
            {
                Documents.Move(Documents.IndexOf(restored[i]), i);
            }

            SyncPagesToDocuments();
        });
    }

    private static bool SamePath(string path, string other) =>
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase);

    // Rebuilds the grid's pages, their indexes, the counts and the empty state from the list's
    // documents, then says so.
    private void SyncPagesToDocuments()
    {
        _pages.Clear();
        foreach (PageItem page in Documents.SelectMany(document => document.Pages))
        {
            page.Index = _pages.Count;
            _pages.Add(page);
        }

        UpdatePageCounts();
        ShowEmptyState = Documents.Count == 0;
        PagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private DocumentGroup? Skip(string path, SkipReason reason)
    {
        SkippedDocuments.Add(new SkippedDocument(Path.GetFileName(path), reason));
        return null;
    }

    // The nearest page from fromIndex, stepping by step (±1), that the preview can show: a page
    // whose thumbnail failed would only fail again. Null past either end of the grid.
    public PageItem? FindPreviewablePage(int fromIndex, int step)
    {
        for (int index = fromIndex + step; index >= 0 && index < _pages.Count; index += step)
        {
            if (!_pages[index].HasError)
            {
                return _pages[index];
            }
        }

        return null;
    }

    public void CheckAllPages() => ApplyChecks(_pages, _ => true);

    public void CheckNoPages() => ApplyChecks(_pages, _ => false);

    public void InvertChecks() => ApplyChecks(_pages, page => !page.IsChecked);

    // Page numbers count within each document, so with several documents "odd" means odd pages of
    // each: the double-sided-scan case (spec §4.3).
    public void CheckOddPages() => ApplyChecks(_pages, page => page.PageNumber % 2 == 1);

    public void CheckEvenPages() => ApplyChecks(_pages, page => page.PageNumber % 2 == 0);

    public void SetPageChecked(PageItem page, bool isChecked) => ApplyChecks([page], _ => isChecked);

    /// <summary>
    /// Checks exactly the pages <paramref name="rangeText"/> names (<c>1-4, 7, 12-</c>), in every
    /// document, and unchecks the rest. It is validated against the longest document, so a page
    /// past the end of a shorter one simply isn't there. False, changing nothing, when the text
    /// isn't a valid range, or while a save runs.
    /// </summary>
    public bool TryCheckRange(string rangeText)
    {
        if (_pages.Count == 0 || IsSaving)
        {
            return false;
        }

        HashSet<int> pageNumbers;
        try
        {
            pageNumbers = [.. PageRange.Parse(rangeText, Documents.Max(document => document.PageCount))];
        }
        catch (FormatException)
        {
            return false;
        }

        ApplyChecks(_pages, page => pageNumbers.Contains(page.PageNumber));
        return true;
    }

    public void Rotate(IReadOnlyList<PageItem> pages, bool clockwise)
    {
        if (pages.Count == 0 || IsSaving)
        {
            return;
        }

        Dictionary<PageItem, int> previous = pages.ToDictionary(page => page, page => page.RotationDegrees);
        foreach (PageItem page in pages)
        {
            if (clockwise)
            {
                page.RotateClockwise();
            }
            else
            {
                page.RotateCounterClockwise();
            }
        }

        _changeVersion++;
        _undoStack.Push(() =>
        {
            foreach ((PageItem page, int degrees) in previous)
            {
                page.RotationDegrees = degrees;
            }
        });
    }

    public void Undo()
    {
        if (!IsSaving && _undoStack.TryPop(out Action? action))
        {
            _changeVersion++;
            action?.Invoke();
        }
    }

    // One undo step however many pages change, and none when nothing does, so a repeated "Select
    // all" doesn't bury real edits under empty steps. Every changed page flipped exactly once, so
    // flipping the same pages back is the undo.
    private void ApplyChecks(IEnumerable<PageItem> pages, Func<PageItem, bool> shouldBeChecked)
    {
        if (IsSaving)
        {
            return;
        }

        List<PageItem> changed = pages.Where(page => page.IsChecked != shouldBeChecked(page)).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        FlipChecks(changed);
        _changeVersion++;
        _undoStack.Push(() => FlipChecks(changed));
    }

    private void FlipChecks(List<PageItem> pages)
    {
        foreach (PageItem page in pages)
        {
            page.IsChecked = !page.IsChecked;
        }

        UpdatePageCounts();
    }

    private void UpdatePageCounts()
    {
        foreach (DocumentGroup document in Documents)
        {
            document.CheckedPageCount = document.Pages.Count(page => page.IsChecked);
        }

        TotalPageCount = _pages.Count;
        CheckedPageCount = _pages.Count(page => page.IsChecked);
    }
}
