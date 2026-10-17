# Architecture

> Component boundaries, key flows, data and design decisions; read this before changing structure, adding a project or taking on an external dependency.

This file covers structure and trade-offs. What is used and how to build it is in `tech-stack.md`.

## 1. Overall structure

```
Views (axaml) ──binding──> ViewModels ──> GitGraph.Core ──Process──> git CLI
     │                         │              ├─ Git/        command wrapper + output parsers
     └─ Controls/GraphCell     │              ├─ Graph/      lane layout (pure computation)
        (draws one row)        │              ├─ Diff/       unified diff parsing
                               │              └─ Workspaces/ state persistence + repository discovery
                               └─ IAppHost / IDialogService (implemented by MainWindow)
```

- A single-process desktop app. Entry point: `src/GitGraph.App/Program.cs` → `App.axaml.cs`.
- No database and no network service. The only external dependency is `git` on `PATH`; the only persisted file is `state.json`.

## 2. Projects and dependency direction

| Project | Responsibility | May depend on | Must not depend on |
|---|---|---|---|
| `GitGraph.Core` | Run git, parse output, graph layout, diff parsing, workspace persistence and repository discovery | BCL | Avalonia or any UI type |
| `GitGraph.App` | Avalonia UI, MVVM | Core, Avalonia, CommunityToolkit.Mvvm | — |
| `GitGraph.Core.Tests` | Unit tests for Core, plus integration tests on real temporary repositories | Core | App |

- Core must stay UI-free so the test project can cover it directly. A new git feature gets its Core method, parser and tests first, then is wired into a view model.
- View models never open dialogs or touch the clipboard or folder picker directly; they go through `IAppHost.Dialogs` (`Services/IDialogService.cs`), which `MainWindow` implements.

## 3. Layers inside the App

| Layer | Files | Responsibility |
|---|---|---|
| Top-level state | `MainWindowViewModel` | Workspace list, theme, global error bar; implements `IAppHost` |
| Workspace | `WorkspaceViewModel` / `RepoNodeViewModel` / `WorktreeNodeViewModel` | Folders, discovered repositories and worktrees, open tabs |
| Tabs | `TabViewModel` (base) | A tab is a repository (`RepoTabViewModel`) or a folder (`FolderTabViewModel`, tree + file viewer, state in `AppState.FolderViews`); saved in the workspace by `StateKey` (`folder:` prefix for folders) |
| Repository tab | `RepoTabViewModel` / `CommitRowViewModel` | Load commits, refs and worktrees; build rows and badges; find, compare, context-menu commands; watch `.git` |
| Details | `DetailsViewModel` | Two tabs: Commit (full message and metadata) and Changes (file list and per-file diff, unified or split) |
| Views | `Views/*.axaml` | Bindings and a little keyboard and pointer handling only |

## 4. Key flows

**Opening a repository tab and drawing the graph**
1. `WorkspaceViewModel.OpenTab` creates a `RepoTabViewModel`. `RefreshAsync` loads head, refs, worktrees, the uncommitted count and commits in parallel (`GitRepository.GetCommitsAsync`, `--date-order`, 400 per page).
2. `GraphLayout.Build` computes a `GraphRow` per commit (node lane plus Pass/In/Out segments). Each row is self-contained, so the list can virtualize.
3. `Build` assembles `CommitRowViewModel`s and badges (local, remote, tag, checked out in another worktree).
4. `GraphCell.Render` draws only the top and bottom halves of its own row; adjacent rows join into continuous lines.

**Selecting a commit to see its diff**: `RepoTabViewModel` → `DetailsViewModel.ShowCommit` → `GetChangesAsync` (`--name-status -z` merged with `--numstat -z`) → select a file → `GetFileVersionsAsync` (`git show <rev>:<path>`, or the file on disk for the working tree) → `TextDiff.Build` (DiffPlex) → unified lines and split rows, collapsed to 3 lines of context per hunk. Double-click, Enter or the context menu opens the same diff in `DiffWindow`.

**Automatic refresh**: a `FileSystemWatcher` on the git dir and common dir, debounced to 700 ms, ignoring `objects/`, `logs/` and `*.lock`. The window also refreshes on activation, throttled to 2 s.

## 5. Data and persistence

- git itself is always the source of truth; the app does not cache commit history.
- `state.json` (`WorkspaceStore`, source-generated System.Text.Json) holds the workspace list, each workspace's folders, open tabs and active tab, the theme, whether remote branches are shown, and the diff mode. Writes are atomic (write a tmp file, then replace). The path is `ApplicationData/GitGraph/state.json`; `GITGRAPH_STATE` overrides it.

## 6. Key design decisions

- **Call the git CLI instead of libgit2 / LibGit2Sharp**
  - Trade-off: behavior matches the user's command line exactly (worktrees, config, hooks, credentials); the cost is parsing output ourselves and starting a process per call.
  - Parsing always uses NUL / `0x1e`-separated format strings and `-z`, never human-readable output. The environment is fixed to `LC_ALL=C`, `GIT_OPTIONAL_LOCKS=0` and `GIT_TERMINAL_PROMPT=0`.
- **Diff with DiffPlex instead of parsing `git diff`**: both file versions come from git and DiffPlex computes the diff in-process, which gives word-level highlights inside modified lines. Change lists and +/− counts still come from git (`--numstat`), so rename detection and binary detection match git. Line-ending-only changes show as no text changes.
- **Self-contained graph rows**: each row records its own segments and doesn't depend on its neighbours being rendered, which allows list virtualization. Lanes keep a fixed column and free slots are reused; gaps are not compacted the way vscode-git-graph does.
- **Worktrees grouped by `--git-common-dir`**: multiple working trees of one repository hang under a single repository node in the sidebar.
- **Visual concept: transit map**: lane colors are the only strong color; the rest of the UI is quiet slate. The amber Signal color only means "where you are" (HEAD, selection, active tab). Tokens live in `Styles/Tokens.axaml`; don't hard-code colors in views. The chrome (menu bar, sidebar, folder tabs) sits on a darker `Frame`; the open repository is one rounded sheet (`Border.sheet`) in it. Radii follow hierarchy: sheet 10, inputs 8, list pills 6, badges 3.
- **Motion answers actions**: hover washes and chevrons ease, the tab's signal bar grows in, the details pane and diff fade when their content is replaced (`Controls/Motion.cs`). The only unprompted motion is a one-time ripple around the HEAD station when a repository loads. All of it is short and redraws little; `GITGRAPH_REDUCED_MOTION=1` turns the code-driven animations off.

## 7. Extension points

- New git operation: add a method to `GitRepository` → add a `[RelayCommand]` to `RepoTabViewModel` / `CommitRowViewModel` → add it to the context menu in `RepoTabView.axaml`. Use `IDialogService.PromptAsync` when input is needed.
- New keyboard-shortcut command: add it to `ShortcutService.Commands` (default gesture matching VS Code if VS Code has the command) and its action to the table in `Views/MainWindow.Shortcuts.cs`. Show it in menus with `InputGesture="{Binding Keys[Id], Source={x:Static svc:ShortcutService.Instance}}"` and in tooltips with `Tips[Id]`; never hard-code a key in a tooltip or `KeyBinding`.
- New icon: use a Lucide icon (`<LucideIcon Kind="…" />`, names at lucide.dev/icons); don't draw icons by hand.
- Not allowed: starting git with `new Process` in a view model; referencing Avalonia from Core; hard-coding colors in axaml.
