# Git Graph

A desktop git history viewer built with Avalonia and modelled on the
[Git Graph](https://github.com/mhutchie/vscode-git-graph) extension for VS Code.

- **Commit graph**: every branch, remote branch and tag on a colored lane, with uncommitted changes as a dashed row on top.
- **History**: filter by branch, show or hide remotes, find commits by message, author, hash or branch name, and load more on demand.
- **Diffs**: files changed by a commit, the working tree, or between any two commits (Ctrl+click the second one). View them unified or side by side.
- **Worktrees**: each repository lists its linked worktrees in the sidebar. Add one from any commit or branch, open it in its own tab, or remove it. Commits checked out in another worktree are marked in the graph.
- **Workspaces**: named sets of folders. Repositories inside them, up to three levels deep, are found automatically. Each workspace keeps its own open tabs.

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

Fonts: IBM Plex Sans and IBM Plex Mono, bundled under the SIL Open Font License (`src/GitGraph.App/Assets/Fonts/OFL.txt`).
