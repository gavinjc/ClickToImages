# ClickTo: Images — Technical Specification

**Project naming:** `ClickToImages.*`, following ClickTo: Slideshow. `Img2PDF` (now ClickToPDF) was the only app
to use an internal codename, and it stays the exception (decided 2026-10-02).
**Product name:** ClickTo: Images
**Target:** Windows 11 (22H2+), Windows 10 20H1+ as secondary
**Distribution:** Microsoft Store
**Status:** greenfield; Store name reserved in Partner Center. Shared app chrome (About dialog,
diagnostics, theme, title bar, rating prompt, cross-promo panel) comes from the `Bureau.Common`
library as a git submodule. App logic is ported at source level from the shipped siblings,
ClickTo: Slideshow (`ClickToSlideshow`, the newer and preferred source) and
ClickTo: PDF (`ClickToPDF`).
**Start here:** §0 — how this app sits on the shared repo, and the family behaviour it inherits.

---

## 0. Building on the family

This app joins two shipped siblings. Most of what is not in this spec is already decided in
**`Bureau.Common`**, the shared repo, and this spec deliberately does not repeat it.

### Read these first, in this order

| Document | What it settles |
|---|---|
| `external/Bureau.Common/docs/new-app-checklist.md` | Every step from empty repo to first working window, in order. Follow it literally |
| `external/Bureau.Common/docs/app-conventions.md` | Window layout, theme, About dialog, behaviour, shell extension, localization, release notes |
| `external/Bureau.Common/docs/lessons.md` | Build toolchain, MSIX, WinUI traps, **testing the running app safely**, Store submission |
| `external/Bureau.Common/docs/family-guide.md` | Store identities (§0), listing copy, icons, screenshots |
| `external/Bureau.Common/CLAUDE.md` | The shared library's own rules, before changing anything in it |

`family-context.md` is imported into this repo's `CLAUDE.md`, so every session already has the
one-page family overview.

**Where this spec and `app-conventions.md` disagree, the conventions win** — unless §0.4 below
says explicitly why this app differs. Raise a conflict rather than quietly diverging.

### 0.1 How the shared repo is consumed

- `Bureau.Common` is a **git submodule at `external/Bureau.Common`, pinned to one commit.** The
  pin is the point: an old Store build can be rebuilt exactly, chrome included.
- `git clone --recurse-submodules`. After a pull that moves the pin, run
  `git submodule update --init` — without it `external/Bureau.Common` is empty and the build
  fails on a missing project.
- Add the project to the `.sln`; only `<Name>.App` carries the `ProjectReference`.
- **Shared chrome changes happen in `Bureau.Common`:** commit and push there first, then bump
  this repo's pin. Never edit files under `external/` as if they belonged to this repo.
- Its rules that bite most often:
  - **no network calls, ever** — the cross-promo list is hardcoded for exactly that reason;
  - strings in that library use **its own** resource map (`Strings.Get`); a parameterless
    `ResourceLoader` would read the app's map instead;
  - its XAML `x:Uid` keys need the property suffix (`.Text`, `.Content`) or they render blank
    with no error;
  - `AppVersion` reads the **entry assembly**, so it reports this app's version, not the
    library's;
  - WindowsAppSDK stays at the family version (2.3.1), in step with the apps.

### 0.2 What comes from where

| Layer | Source | Note |
|---|---|---|
| About dialog, cross-promo panel, rating prompt, title-bar theme, `AppVersion`, `Palette.xaml`, `Styles.xaml` | `Bureau.Common` | Use them; do not build a second one |
| Shell extension, save flow, logging, settings, undo, sort comparers, data folders | **Written in this app**, ported from `ClickToSlideshow` | Deliberately *not* shared. `app-conventions.md` is what keeps them consistent |
| This app's engine (`.Core`) | New work | §4 |

Copy csproj, vcxproj and wapproj settings from **`ClickToSlideshow`** — the newest app — not
from `ClickToPDF`. Same for the shell extension: start from `ClickToSlideshow.ShellExtension`.

### 0.3 Standard behaviour required from day one

All of this is in `app-conventions.md`; it is listed here so nothing is missed. The starred
items exist **only in ClickTo: PDF today**, so they will not appear in a port from Slideshow:

- **Toolbar**: working tools left; status text, then the primary action, then the ⓘ About button
  on the right. The primary action is the only accent control on the surface.
- **Options panel** collapsed by default, hand-rolled (a `Button` header with chevron `E70D` and
  a `Border` body), not an `Expander`.
- **Empty state** centred: a 48 px glyph, one line of secondary text, one accent button.
- **Message bar** floating over the bottom of the content, so the content never shifts:
  Bureau.Common's `MessageBar` (since 0.7.2), one row per kind of message, each with its own
  close button. Kinds: skipped files or unreadable pages (Warning), saved with Open / Show in
  folder (Success), copied to the clipboard (Success), and errors (Error). A new message of a
  kind replaces the row showing; starting a save closes the saved, copied and error rows.
- ★ **Enter** anywhere in the window runs the primary action.
- ★ **Closing with unsaved changes** asks Save / Don't save / Cancel: handle `AppWindow.Closing`,
  cancel it, show the dialog, then close.
- ★ **Window title leads with the source folder** — "Holiday — ClickTo: Images" — because every
  right-click opens a new window.
- ★ **Saving writes a sibling temp file, then moves it over the target**, and deletes the empty
  placeholder the Save picker creates when a save fails.
- **Data folders are package-aware from day one** (`LocalState` / `TempState` when packaged),
  copied from Slideshow's `AppDataFolders`. Never raw `%LOCALAPPDATA%`: it is not redirected for
  a packaged full-trust app and survives uninstall. ClickTo: PDF had to ship a migration for
  precisely this.
- **Undo**: 20 steps; a sort, append or multi-item edit counts as one step.
- **Keyboard**: Ctrl+Z, Delete, R / Shift+R, Ctrl+←/→, Ctrl+A; ←/→ and Esc in the preview
  overlay. Wire with `AddHandler(KeyDownEvent, …, handledEventsToo: true)` — `GridView` eats
  some keys first.
- **Pickers**: a distinct `SettingsIdentifier` each, and never a suggested filename derived from
  a folder name (the Save dialog then opens that folder instead of saving).
- **Bad files** become error items with a reason, never a crash. A missing codec names what to
  install and links to `ms-windows-store://assoc/?FileExt=<ext>`.
- **Diagnostics**: a daily log, newest 14 kept, never sent anywhere; an unhandled-exception
  handler that keeps the window alive; About → Open logs folder.
- **Rating prompt**: `RatingPrompt.RequestOnceAsync` only after a save the user started, and
  never in a session with an error: a PDF that wouldn't open (or had no path on disk), a password
  or encryption skip, or a ticked page that couldn't be read. Turning away a file of an
  unsupported type is not an error, and neither is a PDF skipped as already added (Gavin,
  2026-10-31). Known gap (2026-10-31): a folder that can't be listed (rare, e.g. an unreachable
  network share) is skipped by name like a folder with no PDF in it, so it doesn't hold the
  prompt back. Telling the two apart would mean changing `PdfSelection` and the shell
  extension's folder expansion.
- **Localization from the first button**: every user-facing string — tooltips and
  `AutomationProperties.Name` included — in `Strings/en-us/Resources.resw`, with
  `<PRIResource Include="Strings\**\*.resw" />` in the csproj, which is not there by default.
- **`RELEASE_NOTES.md`** at the repo root, with the entry added **in the same commit as the
  change**, not at release time.
- **Extension-list sync test**: the C++ and C# supported-extension lists must match, enforced by
  a unit test (`SupportedExtensionsSyncTests` in ClickTo: PDF is the model).
- **Never force-kill the app while testing, and never screenshot the screen.** Graceful close and
  `PrintWindow(hwnd, hdc, 2)`; `lessons.md` → "Testing the running app safely" has the method.

### 0.4 Where this app deliberately differs

| Difference | Why |
|---|---|
| **No drag-reorder in the grid.** The grid selects; it does not order | Page order is fixed by the document (§4.3). PDFs can be reordered and removed in the PDF list (0.7.4). Undo covers rotation, selection, and removing or reordering a PDF |
| **Grouping headers per source document** | Neither sibling shows items from several sources in one grid. New work |
| **Cancel must delete partial output** | PDF and Slideshow each write a single file, so neither has multi-file cleanup code to port (§5) |
| **Thumbnails prioritised by visibility** | PDF only caps concurrent thumbnail loads at 4. With 200 pages that is not enough (§5) |
| **No audio, no player, no second window** | §2 non-goals |

### 0.5 Before the first Store release

- **`BureauApps`**: add this app to the list in `Bureau.Common` — **shipped apps only, so this
  happens at release, not during the build** — then move *every* app's submodule pin so all of
  them list it. It is a four-repo change. *Done: Bureau.Common `6bfb754` lists this app, and all
  four apps are pinned there (checked 2026-11-19).*
- **Privacy policy**: a folder in the public `privacy-policies` repo at
  `<App>/privacy-policy/index.html`, served at
  `https://the-bureau-of-stuff.github.io/privacy-policies/<App>/privacy-policy/`. That URL is
  submitted to Partner Center and **must never be renamed afterwards**.
- **Version** goes to `1.0.0` in **both** `Directory.Build.props` and the manifest; they must
  move together.
- **Store identity**: the name is reserved in Partner Center. Read the real Package Name,
  Publisher, PublisherDisplayName, Package Family Name and Store ID from there and copy them
  into `family-guide.md` §0 and this app's listing file — never construct them by hand.
- **`RELEASE_NOTES.md`**: a `1.0.0` "First release on the Microsoft Store" entry, worded to match
  the listing, dated once the app is live.

---

## 1. Problem statement

Getting images *out* of a PDF on Windows is not merely buried — it is **absent**. There is no
Explorer verb, no Photos-app path, no Print-to-something trick. The three things people
actually do:

- **Screenshot each page.** Lossy, wrong aspect, wrong resolution, tedious past page three.
- **Upload the PDF to a website.** "pdf to jpg" is an enormous search term and the results are
  almost entirely ad-supported upload services. People do this with bank statements, medical
  letters, contracts and passports without thinking about it.
- **Install Acrobat or GIMP.** A 900 MB install to export four pages.

Meanwhile Windows has shipped a competent PDF rasteriser since Windows 8.1 —
`Windows.Data.Pdf`, the engine behind the OS's own PDF rendering. It has simply never been
exposed to users.

**This is the exact inverse of ClickTo: PDF, and the same insight: the capability is already
on the machine; only the path is missing.**

## 2. Product definition

Select one or more PDFs in File Explorer → right-click → **Export pages as images** → a window
opens showing every page as a thumbnail → tick the pages you want, choose format and
resolution → Save.

**The page-selection grid is the product.** The single most common real task is "I need page 7
and page 12," and every existing tool makes you export all of them and then delete the rest.

### The pitch, and it is worth building the Store listing around it

**Nothing leaves your PC.** The dominant competitors are upload sites. For anyone exporting a
page of something private, "entirely offline, no account, no upload" is a genuine reason to
choose this — and it is simply true: the app contains no networking code at all. Don't cite the
manifest as proof. The app is `runFullTrust` like its siblings, and capability declarations
don't sandbox a full-trust app.

### Explicit non-goals for v1

- **Not a PDF editor, viewer, splitter, or page extractor to PDF.** Images out, nothing else.
  ClickTo: PDF's spec lists these as non-goals and the same line holds here.
- No OCR (ClickTo: PDF owns that; see §8)
- No annotation, redaction, or form handling
- No cropping, region selection, or per-page adjustment beyond rotation
- No cloud, no accounts, no telemetry, no auto-updater

If a feature request would add a second window, it is out of scope.

---

## 3. Architecture

Five projects in one MSIX package, mirroring the ClickTo: PDF solution layout.

```
ClickToImages.sln
├── ClickToImages.Core/             C# / .NET 8 — page enumeration, render, encode, naming
├── ClickToImages.Cli/              C# console — headless export, the M1 proving ground
├── ClickToImages.App/              C# / .NET 8 / WinUI 3 — page grid and save flow
├── ClickToImages.ShellExtension/   C++ (WRL) DLL — IExplorerCommand, launches the app
└── ClickToImages.Package/          Windows Application Packaging Project → MSIX
```

### 3.1 Technology choices

| Concern | Choice | Rationale |
|---|---|---|
| Shell extension | Native C++ (WRL), `IExplorerCommand` | Ports ~95% unchanged from ClickTo: Slideshow's |
| Shared chrome | `Bureau.Common` (git submodule) | About, diagnostics, theme, title bar, rating prompt, cross-promo — one copy across the family |
| App UI | WinUI 3 (WindowsAppSDK 2.3.1), .NET 8 | Same as ClickTo: PDF; `GridView` already tuned for thumbnail grids |
| PDF rendering | **`Windows.Data.Pdf`** | Built into Windows. The OS's own renderer. No dependency, no licence, no native binary |
| Image encoding | `Windows.Graphics.Imaging.BitmapEncoder` | Selected directly via `PdfPageRenderOptions.BitmapEncoderId` |
| Packaging | Windows Application Packaging Project | Standard MSIX for Partner Center |

**New third-party dependencies for the rendering itself: zero.** This app ships with a smaller
dependency footprint than ClickTo: PDF, which at least needs PDFsharp. The ported app layer still
brings CommunityToolkit.Mvvm and WIL, plus whatever `Bureau.Common` uses.

**Do not** add PDFsharp, PdfPig, PDFium, Ghostscript or ImageMagick. Every one of them is a
larger, slower, more legally complicated way to do what the OS already does.

---

## 4. Component specs

### 4.1 Shell extension (`ClickToImages.ShellExtension`)

Ported from `ClickToSlideshow.ShellExtension`, not `ClickToPDF.ShellExtension`. Slideshow's
version already has the fixed title and the bounded folder scan described below; PDF's has
neither. New CLSID, new exe name, new title, new extension table. Module-path resolution, the
packaged-vs-flat exe fallback, `ActivatePackagedApp` with `CoAllowSetForegroundWindow`,
`GetSelectedPaths`, `ExpandFolder` and `WriteLinesFile` carry over unchanged. They are the same
in both siblings.

**Supported extensions:** `.pdf` only. A one-entry table — simpler than either sibling app.

**Title** (`GetTitle`): **Export pages as images**, for any selection count.

**Visibility** (`GetState`): `ECS_ENABLED` when at least one selected item is a `.pdf`, or a
folder directly containing one. `ECS_HIDDEN` otherwise. Never throws. The folder check is
Slideshow's `FolderMayContainSupportedFile`, a bounded scan of the first 100 entries. ClickTo:
PDF shows its verb on *every* folder without looking inside.

**Invoke** — writes the `.list` / `.skipped` temp files and launches the app with
`--list "<path>" [--skipped "<path>"]`. Packaged builds pass these through AUMID activation;
the flat dev build launches `ClickToImages.App.exe`. Identical contract to both siblings, and
still non-negotiable: a folder of scanned PDFs will blow the command line.

### 4.2 Rendering engine (`ClickToImages.Core`)

Implemented in `Rendering/PdfPageRenderer.cs`. The shape, as M1 found it has to be:

```csharp
StorageFile file = await StorageFile.GetFileFromPathAsync(pdfPath);
PdfDocument doc = await PdfDocument.LoadFromFileAsync(file);          // + password overload

using PdfPage page = doc.GetPage(pageIndex);
(uint width, uint height) = (page.Size.Width * dpi / 96, page.Size.Height * dpi / 96);  // rounded
double scale = /* probe render at 10000x1, measure what comes back */;
var options = new PdfPageRenderOptions
{
    DestinationWidth  = (uint)Math.Ceiling(width  / scale),
    DestinationHeight = (uint)Math.Ceiling(height / scale),
};
await page.RenderToStreamAsync(inMemoryBmp, options);
// decode, crop to exactly width x height, set DpiX/DpiY, encode with BitmapEncoder
```

**Resolution.** `PdfPage.Size` is in 96-DPI view units and already reflects the crop box and
`/Rotate`, so the pixel size wanted is a pure multiplication. **But `RenderToStreamAsync` does
not render at the destination size it is given** (found in M1): it treats `DestinationWidth`/
`Height` as view units and multiplies them by a display scale of its own choosing, and writes
that scale into the file as its DPI. On a 140%-scaled machine that was 1.4x in one process and
1.5x in another, unaffected by the process's DPI awareness. Uncorrected, "150 DPI" came out at
210-225 DPI, and differently on every machine. Hence the probe-measure-divide-crop above; the
crop removes the rounding overshoot, at most a pixel or so off the right and bottom edges.

Offered as named presets rather than a raw number, because "DPI" means nothing to most users:

| Preset | DPI | Intent |
|---|---|---|
| Screen | 96 | Quick look, email, forum post |
| Good | 150 | Default. Readable, sane file size |
| Print | 300 | Reprinting, archiving a scan |
| Maximum | 600 | Fine detail, OCR elsewhere, large files |

96 DPI is the API default and is, as Raymond Chen puts it, "the resolution of a
first-generation fax machine." **Do not ship 96 as the default.** 150 is the default. M1
checked it against a real copier scan and a real A4 Word report: body text at 150 is clean and
readable at 1:1 (as long as it really is 150; see above).

**Formats:** PNG (default, lossless, transparent-capable), JPEG (with a quality slider,
smallest files), TIFF (archival). Encoded with the OS's own `BitmapEncoder`, not via
`PdfPageRenderOptions.BitmapEncoderId`: that route can't crop, writes the wrong DPI into the
file, and has no JPEG quality setting.

**Background.** `PdfPageRenderOptions.BackgroundColor` defaults to white. Expose a
**Transparent background** toggle for PNG only, greyed out for JPEG and TIFF where it is
meaningless.

**Password-protected PDFs.** `LoadFromFileAsync(file, password)` exists. On a load failure that
indicates encryption, prompt once per document with the filename shown, and never store the
password. A document whose password is refused is reported as skipped, not as an error.
`Windows.Data.Pdf` cannot open AES-256 (`/V 5 /R 6`) encryption at all, even when the user
password is empty (a permissions-only PDF that every viewer opens silently): `LoadFromFileAsync`
fails with HRESULT `0x80048040` whatever password is given. Don't prompt for that one, since no
password helps; report it as skipped with that reason. The prompt's trigger, measured in M4 on
RC4-128 (`/V 2 /R 3`) PDFs: no password, an empty one and a wrong one all fail with `0x8007052B`
(`ERROR_WRONG_PASSWORD`); the user or the owner password opens it, and a permissions-only one
(empty user password) opens with none. AES-128 (`/V 4`) is not measured; it is expected to behave
the same; if it didn't, it would show as an ordinary open failure under its own heading.

### 4.3 Main app (`ClickToImages.App`)

Single window, ported from `ClickToPDF.App`'s `MainWindow`. The About dialog, title-bar theming
and palette come from `Bureau.Common` rather than being ported.

**The grid is for *selection*, not ordering.** This is the one meaningful behavioural
difference from ClickTo: PDF and ClickTo: Slideshow — page order is fixed by the document, so
drag-reorder is removed. In its place:

- A checkbox on every tile, checked by default
- **Select all / none / invert**, and **odd pages / even pages** (the two most common real
  selections, for double-sided scans)
- **Range entry** — a text box accepting `1-4, 7, 12-` in the usual way
- Per-tile rotate ±90°, applied to the exported image only. Some scans arrive sideways.
- Per-tile preview at full size

**Layout (0.7.4).** Once anything is loaded, the content area has two columns, as in ClickTo:
Speech: the **PDF list** on the left (340 px, under a "PDFS" section header) and the page grid
on the right. The empty state is unchanged: the big Choose PDFs button and no list.
- Each list row has a drag handle, the file name, a detail line ("48 pages · 12 ticked", or
  "Couldn't open") that follows the ticks, and a remove ✕.
- **Clicking a row** (or choosing it by keyboard or screen reader) selects it and scrolls the grid
  so that PDF's section header is at the top; clicking it again scrolls again. A PDF that failed
  to open has no pages, so the grid doesn't move.
- **Dragging a row** reorders the PDFs. The grid's sections, the page indexes and the " (2)" naming
  all follow the list order, which is load order until the user drags. A reorder is one undo step.
- **✕** removes that PDF and its pages from the list, the grid and the counts, as one undo step.
  Undo puts it back in the same place with its ticks and rotations; if the same PDF has been added
  again since, the undo does nothing. Removing the last PDF shows the empty state.
  `PdfPageRenderer` has nothing to close: Windows.Data.Pdf releases the document with its last
  reference, which goes when the undo step falls off the 20-step stack.
- The list selects one row at a time; nothing in Images acts on several PDFs at once.

**Multi-document selections.** Pages from several PDFs appear in one grid, grouped under a
header per source document with its filename and page count, in the list's order. Selection
state is per page across the whole set.

**Loading more PDFs (0.7.3, 0.7.4).** PDFs come from the right-click launch, the empty state's
Choose PDFs button, Bureau.Common's round accent **+** (`Messages/AddButton`, bottom-right of the
PDF list), or drag-and-drop onto the window in either state.
- Every route **adds**: new PDFs join the end of the list, each batch in natural filename order,
  and existing checks and rotations are kept. Nothing replaces the grid.
- A PDF already loaded (same full path, ignoring case), or the same path twice in one batch, is
  not opened again; it is listed in the skipped message as "already added: report.pdf". A PDF
  skipped for its encryption isn't loaded, so adding it again asks again.
- A dropped **folder** adds the PDFs directly inside it, one level deep, exactly as the shell
  extension expands a right-clicked folder (`PdfSelection` mirrors `ExpandFolder`): its other
  files are skipped, subfolders are ignored, and a folder with no PDF directly inside is skipped
  by name. Other dropped non-PDFs are skipped as at launch.
- Batches load one at a time: a drop during a slow load waits for it.
- The window title keeps the folder of the first PDF the window was given (launch, picker or
  drop); later PDFs from other folders don't rename it.
- The + rises above the message bar whenever a message would cover it, and settles back when the
  bar closes.

**Options expander:** resolution preset, format, JPEG quality, transparent background, output
location, naming pattern. Collapsed by default.

**Output naming.** Default `<pdfname>-p001.png`, zero-padded to the document's page count.
The pattern is user-editable with tokens `{name} {page} {total} {dpi}`, with a live example
shown beneath the field so nobody has to guess.

Two loaded PDFs with the same file name (report.pdf from two folders) whose images go to the same
folder would clash under any pattern, so `{name}` gives the second ` (2)`, the third ` (3)` and so
on, as Explorer names copies: `report-p1.png`, then `report (2)-p1.png`. They are numbered in the PDF
list's order (load order until the user drags a row), counting only PDFs with a page in this export, so one saved on its own keeps its plain
name; a number a loaded PDF already has as its own name (a real `report (2).pdf`) is skipped.
PDFs whose images go to different folders (the subfolder and beside-the-PDF locations) keep their
plain names. A suffixed name that matches a file already on disk goes through the same Replace
prompt as any other. A clash the pattern itself causes is refused before anything is written, and
the message names the fix that would end it: add `{page}` (pages of one PDF), add `{name}` (pages
of two PDFs), or, when both are there but run together (`{name}{page}` gives `report12` for
report1's page 2 and report's page 12), reword the pattern.

**Output location.** Default is a subfolder named after the PDF, beside the PDF — exporting
120 pages directly into someone's Documents folder is hostile. Alternatives: same folder as
the PDF, or a chosen folder.

**Copy to clipboard.** A per-tile action putting that single page on the clipboard as a
bitmap. This is the app's sleeper feature: "I just need this one page in an email" is
extremely common, and it turns a save-flow into one click with no file at all.

**Skipped-file reporting, per-tile error state, save progress and cancel** all carry over from
ClickTo: PDF unchanged. One exception: PDF saves a single file, so it has no multi-file
partial-output cleanup. The §5 "delete partial output on cancel" rule is new work.

---

## 5. Performance requirements

Test set: **a 200-page scanned PDF, A4, ~40 MB**, plus a 12-page vector-heavy report.

| Metric | Target |
|---|---|
| Window visible after invoke | < 500 ms |
| First 30 page thumbnails rendered | < 3 s |
| All 200 thumbnails | < 12 s |
| Export 200 pages at 150 DPI PNG | < 60 s |
| Export 20 pages at 600 DPI PNG | < 30 s |
| Peak memory | < 600 MB |

Implementation notes:

- Thumbnails are just low-DPI renders (~200 px on the long edge). Prioritise visible tiles.
  This is new work: ClickTo: PDF's `MainViewModel` only caps concurrent thumbnail loads (a
  `SemaphoreSlim` of 4) and does not prioritise by visibility. With 200 pages, that difference
  matters.
- **`PdfDocument` is not safe to render from several threads at once.** Serialise renders per
  document, and parallelise across documents rather than within one. Getting this wrong
  produces intermittent corruption that is very hard to reproduce.
- At 600 DPI an A3 page is ~100 megapixels. Finish each page and release it before starting
  the next; never hold more than one full-resolution page. The §4.2 decode-crop-encode step
  holds one page in memory (about 450 MB peak for A4 at 600 DPI), and the `BitmapDecoder`
  frees its buffers only when finalized, so the renderer forces a GC after each page. Measured
  in M1: 20 A4 pages at 600 DPI peaked at 517 MB in 19.5 s; without that GC they reached
  1,052 MB. An A3 page at 600 DPI will likely exceed 600 MB on its own; not yet measured.
- Export must be cancellable between pages and must delete partial output on cancel.

**Known limitation: large scans (decided 2026-10-31).** The 600 MB peak applies to the test set
above. Peak memory grows with the PDF's file size: Windows.Data.Pdf keeps the file's pages it
has read in the working set, and its own per-page memory grows with them. Larger scans are not
covered by the budget. Measured 2026-10-28 on 0.7.0, the installed Debug build, before the
0.7.8 probe fix: a 90 MB 200-page scan peaked at 687 MB, and a 350 MB one at 1.19 GB, with
tiles 187-200 still loading at 12 s. 0.7.8 takes about 70 MB off every scan's peak by
narrowing the thumbnails' render-scale probe, which leaves the 90 MB scan at an estimated
~615 MB (not re-measured). Not pursued further for 1.0.0.

---

## 6. Build milestones

Each milestone independently runnable.

**Before M1:** the repo, the five projects, the submodule and an empty working window with a
functioning About dialog come from `new-app-checklist.md` §1–§3 (see §0). Every milestone below
assumes that is done and that `Strings/en-us/Resources.resw` already exists.

**M1 — Core render engine (`ClickToImages.Cli`).**
Load a PDF, enumerate pages, render a page range at a given DPI and format to disk. No UI.
*Validate output quality against a real scanned document and a real vector report before
building any UI* — text sharpness at 150 DPI is the thing most likely to disappoint, and it is
cheaper to discover now.

**M2 — WinUI 3 shell.**
Port `MainWindow` and the grid half of `MainViewModel`. Page thumbnails, checkbox selection,
select all/none/invert/odd/even, range entry, rotate, per-tile preview. Multi-document
grouping. Launched manually with a file path. Meets §5 thumbnail targets.

**M3 — Save flow.**
Options expander, output location and naming pattern with live example, progress,
cancellation, success InfoBar with Open / Show in folder. Copy-to-clipboard action.

**M4 — Password-protected documents.**
Prompt-once-per-document flow, skipped reporting, no password retention.

**M5 — Shell extension.**
Port from `ClickToSlideshow.ShellExtension` (§4.1): new CLSID, new title, `.pdf` only. HKCU `regsvr32`
registration for dev testing. *Expect "Show more options" placement until M6.*

**M6 — MSIX packaging.**
Packaging project, manifest with the context-menu extension, assets at all scales, self-signed
cert for local sideload. **Declare no network capability.** That is hygiene, not proof: the
privacy claim rests on the code containing no networking APIs.

**M7 — Store readiness.**
Listing copy leading on privacy, screenshots, privacy policy, age rating, Windows App
Certification Kit.

---

## 7. Acceptance criteria

- [ ] Selecting one or more PDFs shows **Export pages as images** in the Win11 main context menu
- [ ] Menu entry hidden when the selection contains no PDF
- [x] A folder selection expands non-recursively to the PDFs inside it (0.7.0 MSIX's DLL, Invoke
  called directly: folder with a.pdf, B.PDF, .txt, .png and Sub\deep.pdf listed a.pdf and B.PDF,
  skipped the .txt and .png, left out deep.pdf; folder + file mixes merged; 2026-10-28)
- [x] A 200-page document loads and displays thumbnails within the §5 budget (installed 0.7.10
  Release, 2026-11-04, generated 200-page A4 300-DPI scan at 52 MB, 9 runs: peak working set
  499-506 MB, first 30 by 2.35-2.65 s, all 200 by 10.2-11.6 s, window visible 447-517 ms with 8 of
  9 under 500 and a median of ~462; Gavin passed it on these numbers. Earlier, installed 0.7.0
  Debug, 2026-10-28: peak 607-616 MB and window 626-637 ms. Larger scans are the §5 known
  limitation: 80 MB peaked at 573-584 MB, 307 MB at ~1,027 MB with all 200 by 12.2-18.0 s)
- [x] Odd / even / range selection produce exactly the expected page sets (0.7.0 view model,
  15- and 4-page PDFs: odd, even, invert, `1-4, 7, 12-`, `14-` exact; 7 bad ranges rejected
  unchanged; `2, 5-6` exported exactly those 4 files, 2026-10-28)
- [x] Exported page dimensions match the requested DPI to within one pixel (0.7.0 Core: 6 page
  sizes incl. /Rotate 90 and 270, at 96/150/300/600 PNG and 150 JPEG/TIFF, 36 files all exact,
  file DPI matches, 2026-10-28)
- [x] 150 DPI output of a text page is comfortably readable at 100% zoom (0.7.0 Core export of an
  A4 page of 11 pt Helvetica with an 8 pt footnote, 1240x1754 PNG; Gavin read it at 100% and
  confirmed it reads comfortably, 2026-10-28)
- [x] Transparent-background PNG has genuine alpha, and the toggle is disabled for JPEG/TIFF
- [x] Rotation applies to output and never modifies the source PDF (0.7.0 view model + exporter:
  90/180/270 gave swapped sizes and the corner mark where expected; source SHA-256 unchanged,
  reopens upright, 2026-10-28)
- [x] A password-protected PDF prompts once, and a refused password skips that document only
- [x] Multi-document export names files without collision across source documents (failed in
  0.7.0: X\report.pdf and Y\report.pdf to one chosen folder were refused, "Two pages would both
  be saved as …\report-p1.png"; subfolder and beside-PDF locations named all 17 pages apart.
  Fixed in 0.7.1 with ` (2)` suffixes: the 0.7.1 build, run unpackaged, saved X's 3 pages and Y's 2
  to one chosen folder as report-p1..p3 and report (2)-p1..p2, 2026-10-28. Not yet re-checked in
  the installed package)
- [x] Copy-to-clipboard pastes into Word, Paint and Outlook
- [x] Export is cancellable and leaves no partial files
- [x] Source PDFs are byte-identical after every operation (0.7.0 code: 20 PDFs SHA-256 and
  mtime unchanged after open, thumbnails, export at all DPIs/formats, rotation, corrupt-set load
  and shell-extension GetState/Invoke; and after an RC4 PDF was opened with no, empty, wrong,
  user and owner passwords and pages were rendered through the clipboard's `RenderToStreamAsync`
  path, 2026-10-28)
- [x] A corrupt PDF produces a per-document error, not a crash (installed 0.7.0, 2026-10-28:
  empty, random, HTML and truncated PDFs each showed "Couldn't open this PDF." under their own
  header beside the good one's thumbnails; a broken-xref one was repaired and opened; closed
  normally. 0.7.0 view model: the good pages still exported)
- [x] Package declares no network capability, and the code calls no networking API (0.7.0 MSIX:
  manifest capabilities `runFullTrust` only; metadata of App, Core, Bureau.Common and
  CommunityToolkit.Mvvm has no System.Net, Windows.Web or Windows.Networking reference, P/Invoke
  only kernel32, shlwapi, dwmapi; shell extension and app exe import no socket/HTTP DLL; only
  WindowsAppSDK's unreferenced OAuth projection touches Windows.Web.Http; `LaunchUriAsync` hands
  Store and mailto links to other apps, 2026-10-28)
- [x] Passes Windows App Certification Kit (overall PASS on the 1.0.0 Release package, 2026-11-13;
  only the optional Blocked executables test failed, on advisory findings)

---

## 8. Deliberately deferred

1. **Export selected pages as a new PDF.** The single most-requested thing this app will not
   do. It is deferred rather than refused because it breaks ClickTo: PDF's "no splitter"
   non-goal, and that boundary is doing real work in keeping both products small. If it is ever
   built, it belongs in ClickTo: PDF, not here.
2. **OCR the exported pages.** ClickTo: PDF already owns `PageOcr`. If text extraction from
   PDFs is wanted, it belongs there as a second verb.
3. **Region crop / area export.** Needs a second window or a modal editor.
4. **Contact sheet output** — all pages onto one sheet. Small, and PDFsharp-shaped, so it fits
   ClickTo: PDF better.
5. **SVG or vector export.** `Windows.Data.Pdf` is a rasteriser; this needs a different engine
   entirely.

---

## 9. Store submission notes

- Use ClickTo: Slideshow's `docs/store/` as the template: listing, submission checklist, hero
  art source. Privacy policies live in the separate `privacy-policies` repo, not in `docs/store/`.
  Shared conventions and the family identity table are in `Bureau.Common/docs/family-guide.md`.
- **Lead the listing with privacy.** The competitive set is upload websites; "your document
  never leaves your PC" is the differentiator. It is true because the app contains no
  networking code. Don't claim the package's capabilities prove it (§2).
- Screenshots should show the page grid mid-selection with several pages unticked — that
  communicates the core value faster than a finished export
- The About box's third-party list is short but **not empty**: CommunityToolkit.Mvvm and WIL
  come with the ported app layer, plus anything `Bureau.Common` brings. Credit what the build
  actually contains. Don't claim "no third-party code".
- Age rating answers carry over unchanged

---

## 10. Constraints and cautions

- **Never modify source files.** The source PDF is opened read-only, always.
- **Explorer stability.** Any unhandled exception in the shell extension degrades Explorer.
  Wrap everything; fail to no-menu-entry.
- **No telemetry, no network calls, no auto-update.** Here it is also the product's main claim.
- **Do not render one `PdfDocument` from multiple threads.** Serialise per document.
- **Do not default to 96 DPI.** It is the API default and it looks terrible.
- **Passwords are never persisted**, not to disk, not to settings, not to the log.
- **Watch memory at 600 DPI.** One page at a time, streamed to disk.
- **Resist the PDF editor.** Every feature request will be a step toward one.
