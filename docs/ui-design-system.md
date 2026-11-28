# Git Graph UI Design System

> Warm editorial restraint inspired by Claude Code, paired with a flat, single-surface developer workspace modeled after `claudecodeui` and `Winc`.

---

## 1. Design Philosophy

1. **Flat, Edge-to-Edge Panes**: No floating rounded outer frames or heavy drop shadows around primary workspaces. Panes meet directly along crisp `1 px` hairline rules (`RuleBrush`).
2. **Warm Editorial Contrast**: Instead of cold zinc/slate blues, surfaces use warm charcoal inks in Dark mode and warm paper tones in Light mode, anchored by a restrained terracotta accent (`#D97757` / `#C96442`).
3. **Uniform Control Geometry**: Every single-line input (`TextBox`) and dropdown (`ComboBox`) shares an exact `30 px` height and `6 px` corner radius. Toolbars stay single-row without wrapping or folding.
4. **Legible Typographic Restraint**: Body text, menus, tree rows, and list items default strictly to `FontWeight="Normal"`. `Medium` and `SemiBold` are reserved for section titles, active tabs, and primary headings.
5. **Render-Thread Motion**: Panel reveals and tab transitions run on Avalonia's render thread (`ElementComposition` in `Controls/Motion.cs`), while virtualized commit/diff rows avoid per-row `BrushTransition` animations for 60/120 fps scrolling.

---

## 2. Color System

All colors are defined in `src/GitGraph.App/Styles/Tokens.axaml` under `Dark` and `Light` `ThemeDictionaries`. Views never hard-code hex values.

### 2.1 Core Surfaces & Text

| Token | Dark (Claude Charcoal) | Light (Claude Warm Paper) | Role |
|---|---|---|---|
| `FrameColor` | `#141413` | `#F0EDE6` | Window title bar & chrome |
| `SurfaceColor` | `#1A1917` | `#F5F3EE` | Sidebar & secondary detail panes |
| `CanvasColor` | `#1E1D1B` | `#FAF9F5` | Primary workspace, commit graph & code canvas |
| `RaisedColor` | `#282623` | `#ECE8DF` | Keycaps, elevated tags, inline badges |
| `RuleColor` | `#2E2C28` | `#E2DDD3` | `1 px` hairline dividers and input borders |
| `TextColor` | `#ECEAE4` | `#1A1917` | Primary foreground text |
| `MutedColor` | `#9C988F` | `#6E6A61` | Secondary labels, inactive tabs, metadata |

### 2.2 Brand Accent & Interactive States

| Token | Dark | Light | Role |
|---|---|---|---|
| `SignalColor` | `#D97757` | `#C96442` | Active indicator bar, focus border, links |
| `SignalWashColor` | `#28D97757` | `#1FC96442` | Selected commit/tree/file row wash |
| `PrimaryColor` | `#D97757` | `#C96442` | Primary CTA fill (`Commit`, `Continue`, `Close`) |
| `PrimaryForegroundColor` | `#FFFFFF` | `#FFFFFF` | Primary CTA text |
| `PrimaryHoverColor` | `#E28567` | `#B85636` | Primary CTA hover fill |
| `TabFillColor` | `#2A2723` | `#EAE5DB` | Active pill tab & active sidebar item fill |
| `HoverWashColor` | `#18ECEAE4` | `#121A1917` | Pointer-over row/button tint |
| `OverlayColor` | `#1F1E1B` | `#FAF9F5` | Context menus & flyout surfaces |

### 2.3 Git & Diff Semantic Tokens

| Token | Dark | Light | Role |
|---|---|---|---|
| `AddFgColor` / `GitAddedColor` | `#4EA868` | `#1E7A40` | Added lines & `A`/`U` status markers |
| `DelFgColor` / `GitDeletedColor` | `#E06C65` | `#C43B33` | Deleted lines & `D` status markers |
| `GitModifiedColor` | `#D99B38` | `#9A6700` | Modified files (`M`) |
| `GitConflictColor` | `#D97757` | `#C96442` | Conflicted files (`!`) |
| `FolderIconColor` | `#D99B56` | `#C96442` | Folder icons in file explorer |

---

## 3. Typography

- **UI Sans**: `Segoe UI, Noto Sans, IBM Plex Sans, Noto Sans CJK SC` (`UiFont`)
- **Code Mono**: `Ubuntu Sans Mono, Noto Sans CJK SC, DejaVu Sans Mono, IBM Plex Mono` (`CodeFont`)
- **Type Scale**:
  - `FontSizeSmall`: `11.5` (captions, status letters, keycaps)
  - `FontSizeCaption`: `12` (section headers, ref pills)
  - `FontSizeBody`: `13` (default UI, commit subjects, menus, tree items)
  - `FontSizeTitle`: `15` (pane headings, repository title)
  - `FontSizeLarge`: `17` (large keycaps)
- **Weight Baseline**: `Window`, `TextBlock`, `MenuFlyoutPresenter`, `ContextMenu`, `AccessText`, `MenuItem`, `ListBoxItem`, `TreeViewItem`, and `Button.nav` explicitly set `FontWeight="Normal"`.

---

## 4. Layout Architecture

```text
+-------------------------------------------------------------------------+
| [Logo]  File  View  Theme  Repository  Tabs  Help          [Sidebar][B] |
+---------------+---------------------------------------------------------+
| Repositories  | [git] atlas main x | [wt] atlas-hotfix              [v] |
| [>][I] atlas  |---------------------------------------------------------|
|   [wt] hotfix | atlas [main]  ^v  [O]   [All branches v] [x] [Find...]  |
|   [wt] search |---------------------------------------------------------|
| [ ] beacon    | Graph | Description                  | Date  | Author   |
|               |  o    | Guard against missing map    | 09-15 | Ana      |
|               |  | o  | Document the tile cache      | 09-13 | Ana      |
|               |---------------------------------------------------------|
|               | [Commit]  [Changes 1]                            [^][x] |
|               |---------------------------------------------------------|
|               | cbebff0 compared with 2c1cdc7                           |
| [Theme]       | From "Cache parsed style sheets" to ...                 |
+---------------+---------------------------------------------------------+
```

- **No Outer Rounding**: `Border.sheet`, `ListBox.tabs`, `ListBox.dense`, `TreeView.files`, and `shadui:Sidebar` all use `CornerRadius="0"` and `BorderThickness="0"` (aside from `1 px` `RuleBrush` separators between panes).
- **Non-Folding Toolbars**: Primary action toolbars keep fixed single-row horizontal alignment with compact minimum widths (`ComboBox MinWidth="140"`, `FindBox Width="200"`).

---

## 5. Component Specifications

### 5.1 Pill Tabs (`ListBox.tabs` & `TabControl.pane`)
- **Geometry**: `26 px` height, `8 px` corner radius, `2–4 px` gap.
- **Anatomy**: `[Leading Icon] [Title + Muted Subtitle] [16×16 Circular Close Button]`.
- **Dividers**: `1 × 14 px` `TabDividerBrush` vertical rule centered before inactive tabs (hidden on first item, active tab, and hovered tab).
- **Overflow**: Dynamic `OpacityMask` edge fade (`FadeLeftMask` / `FadeRightMask` / `FadeBothMask`) plus a right-side `TabOverflowButton` (`ChevronDown`) listing all open tabs.

### 5.2 Sidebar Navigation (`Button.nav`)
- **Default**: Transparent background, `MutedBrush` foreground.
- **Hover**: `HoverWashBrush` (`6 px` radius).
- **Active (`.active`)**: `TabFillBrush` background + `2.5 × 14 px` vertical `SignalBrush` indicator bar on the left edge + `TextBrush` foreground.

### 5.3 Flat File Explorer (`TreeView.files`)
- **Zero Chrome**: `BorderThickness="0"`, `Background="Transparent"`, custom flat `TreeViewItem` template (`PART_LayoutRoot` with `6 px` radius, `26 px` min-height, `14 px` child indent).
- **States**: `HoverWashBrush` on hover, `SignalWashBrush` on selection.

### 5.4 Inputs & Dropdowns (`TextBox` & `ComboBox`)
- **Height**: Locked to `30 px` (`Height="30"`, `MinHeight="30"`, `shadui:ControlAssist.Height="30"`, `shadui:ControlAssist.MinHeight="30"`) for single-line `TextBox[AcceptsReturn=False]` and `ComboBox`. Multi-line `TextBox` (`AcceptsReturn="True"`) grows freely from `MinHeight="30"`.

### 5.5 Context Menus (`MenuFlyoutPresenter` & `ContextMenu`)
- `OverlayBrush` fill, `1 px` `RuleBrush` border, `8 px` corner radius, `4 px` padding, and `FontWeight="Normal"` across all `MenuItem` and `AccessText` templates.

---

## 6. Motion & Verification

- **Composition Animations** (`src/GitGraph.App/Controls/Motion.cs`): `FadeIn` and `SlideIn` run on the render thread (`160 ms`, `CubicEaseOut`) via `ElementComposition.GetElementVisual` and respect `GITGRAPH_REDUCED_MOTION=1`.
- **Snapshot Checks**:
  ```bash
  make snapshot SELECT=3 COMPARE=6 THEME=Dark SHOT=.demo/claude-dark.png
  make snapshot SELECT=3 COMPARE=6 THEME=Light SHOT=.demo/claude-light.png
  GITGRAPH_SNAPSHOT_FOLDERWINDOW=.demo/atlas make snapshot THEME=Dark SHOT=.demo/claude-folder-dark.png
  ```
