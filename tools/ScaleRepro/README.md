# ScaleRepro

A bare WinUI 3 window for the scale-change bug (`docs/PROGRESS.md` M7, upstream report in
`docs/scale-bug-upstream-report.md`). Moved to a monitor with a different display scale, the
window keeps a stale frame, and any interaction redraws text and icons at the wrong size over it.
It garbles on Windows App SDK 2.3.1 (what all four ClickTo apps ship) and 2.6.4, but not on
2.5.4-experimental. The apps stay on 2.3.1 until a stable SDK passes this test.

It isn't part of `ClickToImages.sln`, so the normal build and tests never touch it. With no
switches it builds the same way as `src/ClickToImages.App`: same SDK, target framework,
deployment mode and `app.manifest`.

## Test a new Windows App SDK release

From the repo root, with the release's version number from nuget.org:

```
powershell -File tools\ScaleRepro\run.ps1 -SdkVersion 2.7.0
```

That builds it self-contained, so nothing needs installing, and opens one window. The two screens
must use different scale settings in Display settings.

1. On screen 1, set the Format box to TIFF.
2. Drag the window to screen 2. Check that the second line's RasterizationScale changes.
3. Use any control: open the Format box, type in the Pages box.
4. Drag it back to screen 1.

**Fixed:** on screen 2 the Format box shows TIFF and every control redraws cleanly at the new
size. **Still broken:** screen 2 shows PNG (the old frame), then after step 3 text and icons are
drawn at the wrong size, overlapping, or as the wrong glyphs.

If a version won't start, `ScaleRepro-crash.log` beside its exe records any .NET exception.

The baseline (no switches) reads its SDK version from `src/ClickToImages.App`'s csproj, so when
the app moves to a new release the baseline moves with it, nothing to bump here. The real
Bureau.Common chrome, and the round-1 switches that need it, are built in only when the version
is the app's; on any other `-SdkVersion` they are left out.

## Other variants

`run.ps1`'s header lists the other build-time variants (`-Variant`) and switches used to bisect
the bug: manifest, deployment, backdrop and title-bar changes, and two forced-redraw workarounds.
None of them changed the result.
