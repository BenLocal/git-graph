# Tech stack and conventions

> Stack, build and verification commands, and how to check the UI visually; read before writing code and extend the existing patterns.

Structure and design trade-offs are in `architecture.md`.

## 1. Overview

- What it is: an Avalonia desktop git history viewer modelled on the VS Code extension vscode-git-graph: commit graph, history, diffs, worktrees, multiple workspaces.
- Layout: `GitGraph.slnx` with four projects: `src/GitGraph.Core`, `src/GitGraph.App`, `tests/GitGraph.Core.Tests`, `tests/GitGraph.App.Tests`. View model logic (selection, refresh, staging) gets a test in `GitGraph.App.Tests` with `FakeHost`; screenshots only check how it looks.
- Remote: https://github.com/BenLocal/git-graph (public). Releases are built by `.github/workflows/release.yml` from `v*` tags (`make tag VERSION=x.y.z`).

## 2. Stack

| Area | Choice | Version | Notes |
|---|---|---|---|
| Runtime | .NET | net10.0 (SDK 10.0.401) | `Nullable=enable` in every project |
| UI | Avalonia + Themes.Fluent | 12.1.3 | `AvaloniaUseCompiledBindingsByDefault` is on |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 | `[ObservableProperty]` fields + `[RelayCommand]` |
| Serialization | System.Text.Json | BCL | source generator |
| Text diff | DiffPlex | 1.9.0 | line diff plus word diff inside modified lines (`Diff/TextDiff.cs`) |
| Embedded terminal | Iciclecreek.Avalonia.Terminal | 4.1.1 | ANSI rendering and keyboard input; its Porta.Pty dependency provides Unix PTYs and Windows ConPTY. Existing editors do not emulate a terminal. |
| Terminal UI tests | Avalonia.Headless | 12.1.3 | Real PTY integration tests dispatched on the headless UI thread, using the existing xunit v2 runner |
| Tests | xunit / Microsoft.NET.Test.Sdk | 2.9.3 / 17.14.1 | |
| Fonts | Segoe UI → Noto Sans → IBM Plex Sans (UI, as GitHub's system stack); Ubuntu Sans Mono → Noto Sans CJK SC → DejaVu Sans Mono → IBM Plex Mono (code) | Plex bundled in `Assets/Fonts` (OFL) | `UiFont` / `CodeFont` in `Tokens.axaml` |
| Icons | Lucide.Avalonia (`<LucideIcon Kind="GitBranch" Size="14" />`) | 0.2.24 | default size, stroke and colour set by the `LucideIcon` style in `Controls.axaml` |
| Code viewer | AvaloniaEdit | 12.0.0 | read-only file view in folder tabs and both sides of the full diff window |
| Syntax colours | TextMateSharp.Grammars | 2.0.3 | VS Code's grammars and Dark+/Light+ themes, in one registry (`Services/SyntaxHighlighter.cs`) for diffs, blame and the code viewer; editors take the colours through `Controls/SyntaxColours.cs` |

## 3. Conventions

- git is only called through `GitRunner` (`RunAsync` throws `GitException` on an unexpected exit code; `RunRawAsync` leaves the exit code to the caller), always with a `CancellationToken`.
- Async loads follow the pattern "cancel the old CTS; after each await, check `ct.IsCancellationRequested` before setting properties", so an old request can't overwrite a newer selection (see `DetailsViewModel.LoadAsync`).
- With compiled bindings, style setters that bind to the data context use `{ReflectionBinding}`. Properties a style should control must not get a local value in the template, because local values take precedence over styles.
- One control type per Avalonia style selector; multi-type selectors like `Grid.x, Border.x` trigger AVLN2000.
- Colors and fonts come only from resources in `Styles/Tokens.axaml`. A new color that differs between dark and light needs an entry in both ThemeDictionaries.
- Animations: style transitions (`BrushTransition`, `TransformOperationsTransition`) of 100–200 ms, or `Motion.FadeIn` from code. Never loop an animation or animate layout sizes; check `Motion.Enabled` in new code-driven animations.
- Code comments are in English, matching the existing code.

## 4. Build and verification

```bash
make lint       # dotnet format --verify-no-changes, as CI runs it
make build      # expect 0 errors and 0 warnings
make test       # expect all tests to pass
make run
```

For UI changes, check a snapshot. On GNOME Wayland `grim` doesn't work, so use the app's built-in snapshot switch:

```bash
make snapshot SELECT=3 COMPARE=6 THEME=Light SHOT=.demo/light.png
GITGRAPH_SNAPSHOT_TERMINAL=1 make snapshot SHOT=.demo/terminal.png
```

This recreates the demo repositories in `.demo/` (`scripts/make_demo.py`), starts the app on them, and once what is on screen has loaded (it waits for the loads, not for a set time) the window renders itself to a PNG and exits (`Views/MainWindow.Snapshot.cs`). More `GITGRAPH_SNAPSHOT_*` variables (listed at the top of that file) pick a changed file, a details tab, the panel and sidebar layout, or render the full diff, file history, keyboard shortcuts or About window instead.

Behaviour that a picture can't show (no flicker, a refresh that skips work, timings) is checked by temporarily adding code to the snapshot hook that drives the view models and prints what it measured, then removing it. Measure each stage with a Stopwatch before optimizing.

New make targets go in a topic file under `mk/` with a `## description` comment so `make help` lists them; scripts they call go in `scripts/`.

## 5. Other conventions

- Commit identity: `benshi <807629978@qq.com>` (repository-local git config).
- Before adding a NuGet package, confirm the existing dependencies can't do the job; don't add a second MVVM, JSON or git library.
