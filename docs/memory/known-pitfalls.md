# Known pitfalls

> Non-obvious problems hit while building this project, and what fixed them.

- 2026-10-17 Snapshots: `grim` can't capture the screen on GNOME Wayland → the window renders itself with `RenderTargetBitmap` when `GITGRAPH_SNAPSHOT` is set (`src/GitGraph.App/Views/MainWindow.Snapshot.cs`). Separate windows such as dialogs are not captured.
- 2026-10-17 Fonts: the IBM Plex packages on npm / jsdelivr contain no TTF files (404) → take Mono from google/fonts and the static Sans weights from `cdn.jsdelivr.net/gh/IBM/plex@v6.4.0/IBM-Plex-Sans/fonts/complete/ttf/`.
- 2026-10-17 Repaint on theme change: Avalonia 12 `Control` has no `ActualThemeVariantProperty` to observe → custom-drawn controls subscribe to the `ActualThemeVariantChanged` event and call `InvalidateVisual()` (`Controls/GraphCell.cs`).
- 2026-10-17 In `LanePalette` the method name `Color` clashes with the `Color` type → use the full name `Avalonia.Media.Color.Parse`.
