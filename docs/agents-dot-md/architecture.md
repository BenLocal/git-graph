# Architecture

> Component boundaries, key flows, data and design decisions; read this before changing structure, adding a project or taking on an external dependency.

This file covers structure and trade-offs. What is used and how to build it is in `tech-stack.md`.

## 1. Overall structure

```
Views (axaml) ──binding──> ViewModels ──> GitGraph.Core ──Process──> git CLI
     │                         │              ├─ Git/        command wrapper + output parsers
     └─ Controls/GraphCell     │              ├─ Graph/      lane layout (pure computation)
        (draws one row)        │              ├─ Diff/       DiffPlex diffs, hunk patches
                               │              └─ Workspaces/ state persistence + repository discovery
                               └─ IAppHost / IDialogService (implemented by MainWindow)
```

- A single-process desktop app. Entry point: `src/GitGraph.App/Program.cs` → `App.axaml.cs`.
- No database and no server. The only external dependency is `git` on `PATH`. Persisted: `state.json`, plus caches (avatars, contributor line counts). The network is used only for avatars and, when asked, the update check in Help → About.

## 2. Projects and dependency direction

| Project | Responsibility | May depend on | Must not depend on |
|---|---|---|---|
| `GitGraph.Core` | Run git, parse output, graph layout, diff parsing, workspace persistence and repository discovery | BCL | Avalonia or any UI type |
| `GitGraph.App` | Avalonia UI, MVVM | Core, Avalonia, CommunityToolkit.Mvvm | — |
| `GitGraph.Core.Tests` | Unit tests for Core, plus integration tests on real temporary repositories | Core | App |
| `GitGraph.App.Tests` | View model tests (no window): a fake `IAppHost` / `IDialogService` and real temporary repositories | App, Core | — |

- Core must stay UI-free so the test project can cover it directly. A new git feature gets its Core method, parser and tests first, then is wired into a view model.
- View models never open dialogs or touch the clipboard or folder picker directly; they go through `IAppHost.Dialogs` (`Services/IDialogService.cs`), which `MainWindow` implements.

## 3. Layers inside the App

| Layer | Files | Responsibility |
|---|---|---|
| Top-level state | `MainWindowViewModel` | Workspace list, theme, global error bar; implements `IAppHost` |
| Workspace | `WorkspaceViewModel` / `RepoNodeViewModel` / `WorktreeNodeViewModel` | Folders, discovered repositories and worktrees, open tabs |
| Tabs | `TabViewModel` (base) | A tab is a repository (`RepoTabViewModel`) or a folder (`FolderTabViewModel`, tree + file viewer, state in `AppState.FolderViews`); saved in the workspace by `StateKey` (`folder:` prefix for folders) |
| Repository tab | `RepoTabViewModel` / `CommitRowViewModel` | Load commits, refs and worktrees; build rows and badges; find, compare, context-menu commands; watch `.git` |
| Details | `DetailsViewModel` | Two tabs: Commit (message and metadata; for uncommitted changes, the commit form) and Changes (file list and diff). For uncommitted changes the list is grouped into conflicts, staged and unstaged, with stage / unstage / discard per file, group and hunk |
| History window | `FileHistoryViewModel` / `FileHistoryWindow` | One file's commits (`log --follow`) with their diffs, and its blame |
| Embedded terminal | `TerminalViewModel` / `TerminalPanelView` / `TerminalSession` | A PTY shell per repository or folder tab; the view renders ANSI output with Iciclecreek's terminal control, and the session owns the process |
| Services | `SyntaxHighlighter`, `AvatarService`, `UpdateCheck`, `ShortcutService` | Diff colours (TextMate, Dark+/Light+), avatars, the release check, keyboard shortcuts |
| Views | `Views/*.axaml` | Bindings and a little keyboard and pointer handling only |

## 4. Key flows

**Opening a repository tab and drawing the graph**
1. `WorkspaceViewModel.OpenTab` creates a `RepoTabViewModel`. `RefreshAsync` loads head, refs, worktrees, the uncommitted count and commits in parallel (`GitRepository.GetCommitsAsync`, `--date-order`, 400 per page).
2. `GraphLayout.Build` computes a `GraphRow` per commit (node lane plus Pass/In/Out segments). Each row is self-contained, so the list can virtualize.
3. `Build` assembles `CommitRowViewModel`s and badges (local, remote, tag, checked out in another worktree).
4. `GraphCell.Render` draws only the top and bottom halves of its own row; adjacent rows join into continuous lines.

**Selecting a commit to see its diff**: `RepoTabViewModel` → `DetailsViewModel.ShowCommit` → `GetChangesAsync` (`--name-status -z` merged with `--numstat -z`) → select a file → `GetFileVersionsAsync` (`git show <rev>:<path>`, or the file on disk for the working tree) → `TextDiff.Build` (DiffPlex) → unified lines and split rows, collapsed to 3 lines of context per hunk. Double-click, Enter or the context menu opens the same diff in `DiffWindow`: `FullDiffViewModel` splits `AllRows` into each side's lines and `TextDiff.Blocks`, and `CompareView` shows the two files in read-only AvaloniaEdit editors (`Controls/DiffPane` paints changes and syntax colours, `DiffConnector` joins each change across the middle, `DiffOverview` maps them). The sides scroll together by mapping the line at the middle of one through the blocks to the other.

**Showing a diff quickly**: the diff appears as soon as DiffPlex is done; syntax colours follow (`DetailsViewModel.ColourAsync` → `DiffLineViewModel.ApplySyntax`), because TextMate colouring both versions of a large file takes most of a second. Picking another file cancels the colouring; `SyntaxHighlighter` caches recently coloured texts.

**Staging and committing**: selecting the uncommitted row shows `GetWorkingChangesAsync` (staged `diff --cached`, unstaged `diff`, untracked, conflicts). A file moves with `add -A` / `restore --staged` / `restore --worktree` (`clean` for untracked). A hunk moves with `TextDiff.HunkPatch` + `git apply --recount [--cached] [-R]`; the patch takes its lines from the original texts, not the displayed ones (tabs are expanded there). Commit and amend are on the Commit tab. Every change calls `RepoChanged`, the tab's `RefreshAsync`.

**Operations that can stop part-way**: merge, cherry-pick, revert and rebase run through `RunOperationAsync`. If git stops on conflicts, `GetOperationInProgressAsync` (from `MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`, `rebase-*`) makes a bar above the graph offer Continue and Abort, and the conflicted files show under the uncommitted changes.

**Stashes** come from `stash list` and are drawn as dashed rows just above their base commit; a stash's untracked files live in its third parent.

**Automatic refresh**, kept cheap because it runs often:
- Triggers: a `FileSystemWatcher` on the git dir and common dir (debounced 700 ms, ignoring `objects/`, `logs/`, `*.lock`); window activation (skipped within 3 s of the last refresh); the periodic fetch.
- A tab that isn't on screen (another tab or workspace shown) only notes it is stale and refreshes when shown (`RefreshWhenShownAsync`).
- `RefreshAsync` compares a signature of what it read (commits, refs, HEAD, uncommitted count, stashes, worktrees, filters) with the last build and skips rebuilding the rows when they match; the uncommitted details still re-check their own list.
- The refresh's `git status` also returns the untracked files, which the uncommitted view reuses instead of scanning again (the untracked scan is most of status's time in a large repository). Picking the row by hand scans, since new files don't touch `.git`.

## 5. Data and persistence

- Terminal sessions are transient. Opening a terminal pins its tab. Hiding the panel or switching tabs preserves the shell and its scrollback; closing the terminal, tab or window disposes the PTY. The shared terminal panel keeps inactive controls attached because detaching a terminal view stops its reader. Terminal input bypasses the main window's shortcuts. The shell starts in the tab's directory, using `SHELL` on Unix (with a platform fallback) or Windows PowerShell.

- git itself is always the source of truth; the app does not cache commit history.
- `state.json` (`WorkspaceStore`, source-generated System.Text.Json) holds the workspace list, each workspace's folders, open tabs and active tab, the theme, whether remote branches are shown, the diff mode and diff options, the fetch interval, the pull mode, avatars, shortcuts and folder tab state. Writes are atomic (write a tmp file, then replace). The path is `ApplicationData/GitGraph/state.json`; `GITGRAPH_STATE` overrides it.

## 6. Key design decisions

- **Call the git CLI instead of libgit2 / LibGit2Sharp**
  - Trade-off: behavior matches the user's command line exactly (worktrees, config, hooks, credentials); the cost is parsing output ourselves and starting a process per call.
  - Parsing always uses NUL / `0x1e`-separated format strings and `-z`, never human-readable output. The environment is fixed to `LC_ALL=C`, `GIT_OPTIONAL_LOCKS=0` and `GIT_TERMINAL_PROMPT=0`.
- **Diff with DiffPlex instead of parsing `git diff`**: both file versions come from git and DiffPlex computes the diff in-process, which gives word-level highlights inside modified lines. Change lists and +/− counts still come from git (`--numstat`), so rename detection and binary detection match git. Line-ending-only changes show as no text changes.
- **Hunk staging with `git apply`** rather than editing the index directly: one patch format serves stage, unstage (reverse, cached) and discard (reverse, working tree), and `--recount` lets a single hunk of a larger diff apply. Not offered while whitespace is ignored, since those hunks hide changes.
- **Self-contained graph rows**: each row records its own segments and doesn't depend on its neighbours being rendered, which allows list virtualization. Lanes keep a fixed column and free slots are reused; gaps are not compacted the way vscode-git-graph does.
- **Worktrees grouped by `--git-common-dir`**: multiple working trees of one repository hang under a single repository node in the sidebar.
- **Visual concept: GitHub's Primer around a transit map**: lane colors are the only strong color; the rest of the UI uses Primer's greys, 1 px borders and 6 px radii (badges are pills). Blue Signal means "where you are" (HEAD, selection, focus), green Primary is the main action, and a coral line marks the selected tab. Tokens live in `Styles/Tokens.axaml`; don't hard-code colors in views. Fluent's accent is set through `FluentTheme.Palettes` in `App.axaml`, or the light theme takes the desktop's accent. The chrome (menu bar, sidebar, tabs) sits on a darker `Frame`; the open repository is one bordered sheet (`Border.sheet`) in it.
- **Motion answers actions**: hover washes and chevrons ease, the selected tab's underline grows in, the details pane and diff fade when their content is replaced (`Controls/Motion.cs`). The only unprompted motion is a one-time ripple around the HEAD station when a repository loads. All of it is short and redraws little; `GITGRAPH_REDUCED_MOTION=1` turns the code-driven animations off.

## 7. Extension points

- New git operation: add a method to `GitRepository` → add a `[RelayCommand]` to `RepoTabViewModel` / `CommitRowViewModel` → add it to the context menu in `RepoTabView.axaml`. Use `IDialogService.PromptAsync` when input is needed.
- New keyboard-shortcut command: add it to `ShortcutService.Commands` (default gesture matching VS Code if VS Code has the command) and its action to the table in `Views/MainWindow.Shortcuts.cs`. Show it in menus with `InputGesture="{Binding Keys[Id], Source={x:Static svc:ShortcutService.Instance}}"` and in tooltips with `Tips[Id]`; never hard-code a key in a tooltip or `KeyBinding`.
- New icon: use a Lucide icon (`<LucideIcon Kind="…" />`, names at lucide.dev/icons); don't draw icons by hand.
- Not allowed: starting git with `new Process` in a view model; referencing Avalonia from Core; hard-coding colors in axaml.
