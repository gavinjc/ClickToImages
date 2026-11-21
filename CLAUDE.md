# ClickToImages

## Project Overview
A single-purpose Windows utility: select PDFs in File Explorer, right-click **Export pages as
images**, tick the pages you want in a thumbnail grid, choose format and resolution, Save. The
page-selection grid is the product. Not a PDF editor, viewer or splitter — images out, nothing
else. Rendering is the OS's own `Windows.Data.Pdf`; no third-party PDF library, ever (spec §3.1).
See `docs/clickto-images-spec.md` (authoritative spec), starting with §0.

## The ClickTo family
This app is one of a family. The family context below is loaded from the submodule, as of its
pinned commit. It says where the shared conventions and lessons live; family-wide lessons go
there, not into this repo's notes.

(The shared family-context file is internal and not part of this snapshot.)

**Siblings, heavy reuse:** `ClickToSlideshow` (ClickTo: Slideshow, the newer
and preferred source) and `ClickToPDF` (ClickTo: PDF). The shell extension, app
shell, `AppLog`, settings, undo, sort comparers, data folders and save flow port at source level
from them (spec §0.2). Copy csproj/vcxproj/wapproj settings from ClickToSlideshow, not ClickToPDF.

**Naming:** repo, solution (`ClickToImages.sln`), all projects
(`ClickToImages.App`/`.Core`/`.Cli`/`.ShellExtension`/`.Package`) and namespaces are
`ClickToImages.*`. The product/store-facing name is **ClickTo: Images**: window title, MSIX
manifest `DisplayName`, and all `docs/store/*` copy.

## Shared code: Bureau.Common (git submodule)
The About dialog, cross-promo panel, rating prompt, title-bar theming, surface palette and
`IconButtonStyle` come from the shared
[Bureau.Common](https://github.com/gavinjc/Bureau.Common) repo, mounted
at `external/Bureau.Common` and pinned to one commit.
- Clone with `git clone --recurse-submodules`. After a pull that moves the pin, run
  `git submodule update --init`. Without it, `external/Bureau.Common` is empty and the build
  fails on the missing project.
- Change shared chrome **in Bureau.Common**: commit and push there first, then bump the pin
  here (`git -C external/Bureau.Common pull`, then commit the submodule change). Don't edit
  the files under `external/` as if they were this repo's.
- Read `external/Bureau.Common/CLAUDE.md` before touching it. Its rules include: no network
  calls, its own resource map for strings, and the WindowsAppSDK version kept in step with
  this app's.

## Build
```
ClickToImages.sln
├── ClickToImages.Core/             C# / .NET 8 — page enumeration, render, encode, naming
├── ClickToImages.Cli/              C# console — headless export, the M1 proving ground
├── ClickToImages.App/              C# / .NET 8 / WinUI 3 — page grid and save flow
├── ClickToImages.ShellExtension/   C++ (WRL) DLL — IExplorerCommand, launches the app
└── ClickToImages.Package/          Windows Application Packaging Project → MSIX
```

- `.Core` / `.Cli` / `.App` (csproj): `dotnet build` / `dotnet run --project src/ClickToImages.App`.
- `.ShellExtension` (vcxproj) and `.Package` (wapproj) need VS2022's own MSBuild.exe:
  `powershell -ExecutionPolicy Bypass -File ./build.ps1 -Target ShellExtension` builds the DLL;
  `-Target Package` (the default) builds the MSIX. Never `-t:Restore` the wapproj.
- Signing: the wapproj signs with `src/ClickToImages.Package/ClickToImages.Package_TemporaryKey.pfx`
  when it exists (git-ignored, never committed), or `build.ps1 -CertificateThumbprint <hash>`.

**Status:** checklist §1–§4 done — the five projects, the submodule, an empty window with a
working About dialog, and a signed 0.1.0.0 MSIX installed locally whose right-click verb opens the
app in front with the selection passed through. M1 done: `ClickToImages.Cli <file.pdf> [--pages]
[--dpi] [--format] [--quality] [--transparent] [--output]` over `Core/Rendering`; read spec §4.2 on
the render-scale quirk before touching rendering. M2 done (page grid):
`ViewModels/` (`MainViewModel` holds checks, rotation and undo; `ThumbnailLoader` renders through
`PdfPageRenderer.RenderScaledPngAsync`, run by `State/ThumbnailScheduler` in `State/ThumbnailQueue`'s
order, fed by `VisibleTileTracker`). M3 done (save flow): `Core/Export` (`FileNamePattern`,
`OutputLocation`, `PageExporter`, which renders to sibling temps and moves them only once all have
rendered, so a cancel leaves nothing), `State/ExportOptions` persisted by `State/AppSettings`, and
`MainWindow.Export.cs` for the options panel, save flow and copy-to-clipboard. M4 done (passwords):
`Rendering/PdfOpenFailures` classifies the HRESULTs; `MainViewModel` prompts once through the
`askPassword` callback and records `SkippedDocuments`. Tests encrypt their own RC4 fixtures
(`TestPdf.WriteEncrypted`). Next: M5 (spec §6).
- Local signing: `build.ps1 -CertificateThumbprint <thumbprint>` (a
  `CurrentUser\My` cert already in `LocalMachine\TrustedPeople`). The `.pfx` beside the wapproj
  is password-protected, so a plain `build.ps1` can't sign with it unattended.

**Lint / duplication:**
```
npx jscpd --min-lines 8 --min-tokens 60 --reporters console,json ./src
lizard -C 12 -L 80 -a 5 --warnings_only .
```
C# analyzer severities live in `.editorconfig`; C++ checks in `.clang-tidy`.

## Tests
`dotnet test` over the xunit projects under `tests/` (`ClickToImages.Core.Tests`,
`ClickToImages.App.Tests`).

## Progress

Milestones and tasks are tracked in `docs/PROGRESS.md` (format:
`~/.claude/init-shared/progress-format.md`). Keep it current as you work: mark the task you
start `status: active`, tick finished tasks and add `done:` with today's date, and add tasks
you discover.

## Code standards

General code standards load from user scope (`~/.claude/CLAUDE.md`). What follows is specific to this repo.

**Search before you write.** Grep for an existing helper, type, constant, or utility that
already does this before adding a new one — including in the sibling `ClickToSlideshow` and
`ClickToPDF` repos. Adding a near-duplicate is a defect, not a neutral choice. If you looked
and found nothing, that is worth one line in your summary.

**Duplication check.** For copy-paste duplication specifically, `npx jscpd .` uses this
repo's `.jscpd.json`.

## Notes
- Never modify the source PDFs — opened read-only, always (spec §10).
- No telemetry, no network calls, no cloud, no accounts, no auto-updater. Here it is also the
  product's main claim; it rests on the code, not on the manifest (spec §2).
- Never render one `PdfDocument` from several threads at once; serialise per document (spec §5).
- Never default to 96 DPI; the default is 150 (spec §4.2).
- Passwords are never persisted — not to disk, settings or the log (spec §10).
- Logs (and settings and app temp files, once they exist) go through `AppDataFolders`. The shell
  extension's `.list`/`.skipped` launch files don't: they go to a fixed
  `%LOCALAPPDATA%\Temp\ClickToImages`, which survives an uninstall. The app deletes each after
  reading it; nothing else sweeps that folder.
- Full acceptance criteria: `docs/clickto-images-spec.md` §7.

## GUI tests

Anything that shows a window on the desktop (the app under test, a GUI test run) must be launched through
`powershell -NoProfile -File "run-gui-test.ps1" -Project ClickToImages -Command "<command>"`.
It holds the desktop lock while the command runs, and waits if a hub worker is using the desktop.
