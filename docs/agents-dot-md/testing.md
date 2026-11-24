# Testing Conventions

> Where tests live, how test data and environments are set up, what to regress when changing an area, and which scenarios require manual verification.

Build and test commands live in `tech-stack.md` (`Build and verification`); general principles (write failing reproduction tests first, test behavior not implementation, verify locally) live in `coding-guidelines.md`. This file covers project-specific testing practices.

## 1. Test layers and locations

| Layer | Location / Naming | What it covers | Dependencies |
|---|---|---|---|
| Core unit & git integration | `tests/GitGraph.Core.Tests/*Tests.cs` (xunit) | Git output parsers, graph lane layout, DiffPlex diffs, tree-sitter parsing, SQLite symbol index, and real git operations via `TempRepo` | `git` CLI on `PATH`, temporary directories |
| ViewModel & headless UI/PTY | `tests/GitGraph.App.Tests/*Tests.cs` (xunit + Avalonia.Headless) | ViewModel state, selection, refresh, staging, syntax highlighting, and real PTY terminal sessions (`TerminalTests`) | `FakeHost` / fake `IDialogService`, real temp repos, Avalonia headless dispatcher |
| Visual snapshots | `make snapshot` (`Views/MainWindow.Snapshot.cs` + `scripts/make_demo.py`) | Rendered UI layout, themes, graph drawing, and popup/dialog visuals | Demo repos generated in `.demo/` |

- Minimum bar: new git features land with Core tests first; ViewModel selection, refresh, and staging logic gets App tests; UI layout or styling changes are checked with `make snapshot`.

## 2. Test data and environment

- Tests create isolated temporary git repositories on disk and set local `git config user.name` and `user.email` per repository. Never rely on the developer's global git config (reproduce CI isolation locally with `GIT_CONFIG_GLOBAL` pointing to `[user] useConfigOnly = true` and `GIT_CONFIG_NOSYSTEM=1`).
- No external servers, shared test environments, or user credentials are needed. Network-dependent features (avatar downloads, update checks) are not exercised by automated tests.

## 3. Scenarios requiring manual verification

Automated tests and offscreen snapshots cannot verify everything; check these manually in the running app when touched:
- **Real-screen font sharpness and fractional scaling** (e.g., GNOME 125% scaling on X11 vs Wayland): `RenderTargetBitmap` snapshots cannot judge subpixel/hinting sharpness on a physical display.
- **Native terminal rendering and IME composition**: mixed-style/fallback glyphs (such as Braille animations) and live CJK IME input via Fcitx or macOS input methods.
- **Desktop shell integration**: window icon (`_NET_WM_ICON`), Wayland `app_id`, and native file/folder picker dialogs.

## 4. Regression testing

- **Automated regression**: bug fixes add a reproduction test in the layer where the bug reproduces (`GitGraph.Core.Tests` or `GitGraph.App.Tests`).
- **Regression scope by changed area**:

| Changed area | What to verify |
|---|---|
| `GitGraph.Core/Git`, `Diff`, `Graph` | `GitGraph.Core.Tests` + `GitGraph.App.Tests` |
| `GitGraph.Core/Navigation`, `FolderTabViewModel` | `CodeNavigatorTests`, `IndexSyncTests`, `SourceParserTests`, `WorkspaceSearchTests`, `FolderNavigationTests` |
| `RepoTabViewModel`, `DetailsViewModel`, `FullDiffViewModel` | `GitGraph.App.Tests` + `make snapshot` for UI layout changes |
| `TerminalSession`, `ImeTerminalView`, `TerminalPanelView` | `TerminalTests` (real PTY) + `GITGRAPH_SNAPSHOT_TERMINAL=1 make snapshot` |
| `Views/*.axaml`, `Styles/*.axaml`, `Controls/*` | `make build` (0 warnings) + `make test` + `make snapshot` |

- **Pre-release / shared-module check**: run `make lint && make build && make test`.

## 5. Known platform quirks in tests

- **macOS case-insensitive APFS**: test fixtures must not create files whose names differ only by case (e.g., `a.txt` and `A.txt`).
- **macOS `/var` symlink**: temp directories under `/var/folders/...` resolve to `/private/var/folders/...` in child processes; assert identity via a marker file rather than comparing raw path strings.
- **Avalonia.Headless dispatcher deadlocks**: never call `.GetAwaiter().GetResult()` on async git calls inside `TerminalTestApp.Ui.Dispatch`—use `await GitRunner.RunAsync`. Run headless tests with `--blame-hang-timeout` when investigating hangs.
