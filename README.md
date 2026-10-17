# Git Graph

A desktop git history viewer built with Avalonia and modelled on the
[Git Graph](https://github.com/mhutchie/vscode-git-graph) extension for VS Code.

- **Commit graph**: every branch, remote branch and tag on a colored lane, with uncommitted changes as a dashed row on top.
- **History**: filter by branch, show or hide remotes, find commits by message, author, hash or branch name, and scroll down to load older commits.
- **Diffs**: files changed by a commit, the working tree, or between any two commits (Ctrl+click the second one). Diffs are computed with [DiffPlex](https://github.com/mmanela/diffplex), with changed words highlighted inside modified lines. View them unified or side by side, or double-click a file (or right-click it) to open the whole file side by side in a full window, where unchanged stretches fold and can be opened or closed.
- **Branch and tag labels**: each ref is a label filled with its lane colour, with a branch, tag or cloud icon; a local branch and its remote copies on the same commit share one label (`main │ origin`), and the checked-out branch is bold. The target button next to the branch jumps to HEAD.
- **Contributors**: the chart button shows who contributed to the current branch, like GitHub's Insights → Contributors: commits per week, then each person's commits, share, lines added and removed and weekly activity, over a chosen period and sorted by commits, additions or deletions (merge commits excluded, .mailmap respected).
- **Graph actions**: right-click a commit or branch to merge it into the current branch or cherry-pick it; if either stops on conflicts you can abort it from the prompt. Copy a commit's hash, or a changed file's path, from the context menus or the copy buttons.
- **Commit messages**: the body renders as Markdown, and links open in the browser. Author avatars come from GitHub for GitHub noreply addresses and from Gravatar otherwise (robohash for people without one), cached for a week; or choose initials under View → Author avatars, which sends nothing. Avatar requests use the proxy from the environment, or GNOME's system proxy when the app is started from the desktop.
- **Pull and push**: like VS Code's status bar, the toolbar and sidebar show commits to pull (↓) and push (↑) against each branch's upstream. Click it to sync (pull, then push) or to publish a branch that has no upstream; Pull and Push are also in the Repository menu. Every repository in the open workspace is fetched quietly every 3 minutes (turn it off under View).
- **Keyboard shortcuts**: defaults follow VS Code where it has the same command (Ctrl+B sidebar, Ctrl+J panel, Ctrl+W close tab, Ctrl+PageDown/PageUp and Alt+1…9 for tabs, Ctrl+K chords). Menus and button tooltips show them; File → Keyboard shortcuts (Ctrl+K Ctrl+S) lists every command and lets you record, reset or remove shortcuts, warning about clashes.
- **File states**: folder trees colour files and folders by their git state, as VS Code's explorer does: modified (M), added (A), untracked (U), renamed (R), conflicted (!), ignored dimmed, and a dot on folders with changes inside. Under Folders, linked worktrees are nested under the workspace folder that contains them (foldable, remembered). Right-click any repository or worktree under Repositories to browse its files or open it in VS Code, wherever it lives.
- **Folders**: click a workspace folder in the sidebar to browse its files in a tab: a tree on the left, the selected file on the right (read-only in AvaloniaEdit, with VS Code's syntax highlighting and Ctrl+F search, or images). Folders that only contain one folder show as one row, like `src/main/java/com/example` (View → Compact folders, as in VS Code). Each folder remembers which subfolders were open, which file was selected and how wide its tree was dragged. Right-click a folder, in the sidebar or the tree, to open it in VS Code or copy its path.
- **Worktrees**: each repository lists its linked worktrees in the sidebar. Add one from any commit or branch, open it in its own tab, or remove it. Commits checked out in another worktree are marked in the graph.
- **Workspaces**: named sets of folders. Repositories inside them, up to three levels deep, are found automatically. Each workspace keeps its own open tabs.
- **Layout**: as in VS Code, a menu bar (File, View, Repository) with sidebar (Ctrl+B) and commit details panel (Ctrl+J) toggles on its right; the panel can also be maximized over the graph. As in VS Code, a single click in the sidebar opens a preview tab (in italics) that the next one replaces; double-click it, or the tab, to keep it open.

## Run

```sh
dotnet run --project src/GitGraph.App
```

Needs the .NET 10 SDK and `git` on `PATH`. State is saved to `~/.config/GitGraph/state.json`
(`%APPDATA%\GitGraph\state.json` on Windows). Set `GITGRAPH_STATE` to use a different file, and `GITGRAPH_REDUCED_MOTION=1` to turn off animations. On a Wayland desktop the app uses Avalonia's native Wayland backend, which renders sharply at fractional scales such as 125%; it is still experimental, so `GITGRAPH_BACKEND=x11` switches back to XWayland.

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

Fonts: the interface uses IBM Plex Sans. Code uses Ubuntu Sans Mono, Noto Sans CJK SC and DejaVu Sans Mono when installed (the same chain as the author's Ghostty terminal), falling back to IBM Plex Mono. Both Plex fonts are bundled under the SIL Open Font License (`src/GitGraph.App/Assets/Fonts/OFL.txt`).
