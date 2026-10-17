# Git Graph

A desktop git history viewer built with Avalonia and modelled on the
[Git Graph](https://github.com/mhutchie/vscode-git-graph) extension for VS Code.

- **Commit graph**: every branch, remote branch and tag on a colored lane, with uncommitted changes as a dashed row on top.
- **History**: filter by branch, show or hide remotes, find commits by message, author, hash or branch name, and load more on demand.
- **Diffs**: files changed by a commit, the working tree, or between any two commits (Ctrl+click the second one). Diffs are computed with [DiffPlex](https://github.com/mmanela/diffplex), with changed words highlighted inside modified lines. View them unified or side by side, or double-click a file (or right-click it) to open its diff side by side in a full window.
- **Worktrees**: each repository lists its linked worktrees in the sidebar. Add one from any commit or branch, open it in its own tab, or remove it. Commits checked out in another worktree are marked in the graph.
- **Workspaces**: named sets of folders. Repositories inside them, up to three levels deep, are found automatically. Each workspace keeps its own open tabs.
- **Layout**: as in VS Code, a menu bar (File, View, Repository) with sidebar (Ctrl+B) and commit details panel (Ctrl+J) toggles on its right; the panel can also be maximized over the graph.

## Run

```sh
dotnet run --project src/GitGraph.App
```

Needs the .NET 10 SDK and `git` on `PATH`. State is saved to `~/.config/GitGraph/state.json`
(`%APPDATA%\GitGraph\state.json` on Windows). Set `GITGRAPH_STATE` to use a different file.

## Layout

| Project | What it holds |
| --- | --- |
| `src/GitGraph.Core` | git CLI wrapper and output parsers, graph lane layout, unified diff parsing, workspace persistence and repository discovery |
| `src/GitGraph.App` | Avalonia UI (MVVM with CommunityToolkit.Mvvm) |
| `tests/GitGraph.Core.Tests` | unit tests, plus integration tests against throwaway repositories |

```sh
dotnet test
```

## Make targets

`make` lists every target. The targets are defined in `mk/*.mk` and helper scripts live in `scripts/`.

| Target | What it does |
| --- | --- |
| `make build` / `make test` / `make run` | Build, test or run (`CONFIG=Release` for a release build) |
| `make publish RID=linux-x64` | Self-contained build in `artifacts/publish/<rid>` |
| `make install` | Install or update the app in `~/.local` (`PREFIX=...` to change): files in `opt/gitgraph`, a `gitgraph` command in `bin`, an app-menu entry and a desktop shortcut on Linux (`SHORTCUT=0` skips the shortcut) |
| `make uninstall` | Remove what `make install` added |
| `make demo-run` | Create demo repositories in `.demo/` and open the app on them |
| `make snapshot SELECT=3 COMPARE=6` | Render the demo window to `.demo/snapshot.png` |
| `make icons` | Re-render the PNG and ICO app icons from the drawing in `scripts/render_icon.py` (needs Pillow) |
| `make reindex` | Rebuild the generated indexes in `AGENTS.md` and `docs/` |

Fonts: IBM Plex Sans and IBM Plex Mono, bundled under the SIL Open Font License (`src/GitGraph.App/Assets/Fonts/OFL.txt`).
