# ClickTo: Images — Release notes

## 1.0.0 — 21 November 2026

First release on the Microsoft Store.

- **Right-click to start.** Select PDFs, or a folder of them, in File Explorer, right-click, and
  choose **Export pages as images**. Or open the app from Start and use **Choose PDFs**.
- **Add more PDFs as you go.** Drag PDFs or folders onto the window, or use the round **+** button
  once some are open. They join the ones already there. A folder adds the PDFs directly inside it,
  as the right-click menu does. A PDF that's already open isn't added twice; the message bar says
  so.
- **Your PDFs in a list.** Open PDFs are listed down the left, each with its page count and how
  many pages are ticked. Click one to jump to its pages, drag it to change the order, or remove it
  with its ✕ (Undo brings it back as it was).
- **See every page.** Pages appear as thumbnails in a grid, under a heading for each PDF with
  its name and page count. The pages you're looking at load first, so even a 200-page scan is
  ready to work with in seconds.
- **Pick the pages you want.** Every page has a tick box and starts ticked. Tick all pages,
  none, or swap ticked and unticked, or just the odd or even pages for double-sided
  scans. Or type pages and ranges such as `1-4, 7, 12-`. With several PDFs open, page numbers
  count within each PDF.
- **Turn sideways pages.** Rotate any page 90° either way, from its tile, the preview or the R
  key (Shift+R turns the other way). Only the exported image turns; your PDF is never changed.
- **Look closer.** Double-click a page, or use its magnifier, for a full-size preview. Step
  through pages with the arrow keys, and tick or untick the page from there.
- **Undo** up to twenty steps with Ctrl+Z: ticks, rotations, and removing or reordering PDFs.
  Ticking all the odd pages at once counts as one step.
- **A PDF that can't be opened** says so under its own heading, with the reason when Windows
  gives one (such as "Access is denied."), and the others load as usual.
- **Save the ticked pages as images.** PNG, JPEG (with a quality slider) or TIFF, at Screen
  (96 DPI), Good (150 DPI, the default), Print (300 DPI) or Maximum (600 DPI). PNG can have a
  transparent background.
- **Your choice of where and what name.** Images go into a folder named after each PDF, beside
  it, unless you pick the PDF's own folder or one of your own. File names follow a pattern you
  can edit, such as `{name}-p{page}`, with an example of the result shown as you type. Two PDFs
  with the same name saved to one folder are told apart the way Explorer does it: `report-p1.png`
  and `report (2)-p1.png`. Your options are remembered.
- **Nothing half-done.** A progress bar shows how far a save has got, and Cancel stops it and
  removes every image it had written. Existing images are only replaced after you say so. The
  list, ticks, rotations and options can't be changed while a save runs.
- **Open or show the result** from the bar that appears when the save is done.
- **Copy one page** from its tile straight to the clipboard, to paste into an email or a
  document without saving a file.
- **Press Enter** to save, and get asked before closing a window whose ticked pages haven't been
  saved. Save waits until every PDF has finished opening; choosing Save as you close waits for
  them too, then saves and closes.
- **Password-protected PDFs** ask for their password once, showing which PDF it is for. The
  password is never saved. A PDF whose password is wrong, or that you skip, is listed as skipped
  and the others open as usual. PDFs with AES-256 encryption, which Windows can't open, are
  listed as skipped without asking.
- **Help built in.** Press F1, or the **?** button beside About, for short topics on every part
  of the app and a table of keyboard shortcuts. Help comes with the app and works offline.
- **One message bar** at the bottom of the window says what happened: saved, copied, skipped or
  went wrong. Each message has its own line, an icon for its type and its own ✕ to close it.
  Messages read naturally for one file or page as well as several: "Skipped notes.txt", "A ticked
  page couldn't be read and wasn't saved: report.pdf p4", never "file(s)" or "page(s)".
- **Stays on your PC.** The app never connects to the internet and sends nothing anywhere. Your
  PDFs are only read, never changed, moved or deleted.
