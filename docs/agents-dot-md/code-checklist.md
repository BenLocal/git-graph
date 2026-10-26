# Code checklist (mandatory self-check)

> Hard rules to check item by item after writing or changing code; add new items to this file.

Each item gives the rule, a counter-example and the correct form. Sources, in order of weight: pitfalls actually hit > comments that keep coming up in review > verbal agreements. Softer advice belongs in `coding-guidelines.md`.

## 1. Build and tests pass before handing over

`dotnet build GitGraph.slnx` has no errors and `dotnet test GitGraph.slnx` passes. For UI changes, also take a snapshot as described in `tech-stack.md` and look at it.

- Wrong: build only the App project and report done without running the tests.
- Right: run both commands and report the result as it is; if something fails, include the output.

## 2. Core stays UI-free; git features land in Core first, with tests

- Wrong: `Process.Start("git", ...)` in a view model, or an Avalonia type referenced from Core.
- Right: add the method to `GitRepository` and the parser to `GitParsers`, test it in `GitGraph.Core.Tests` against a temporary repository, then call it from the view model.

## 3. Parse only machine-readable git output

- Wrong: parsing `git log --oneline` or default `git status` output, which breaks on file names with spaces or non-ASCII characters.
- Right: separate fields with `%x00` / `%x1e` in `--format`, use `-z` for file lists, and pass arguments one by one instead of building a command string.

## 4. Async loads must not let stale results overwrite newer state

- Wrong: `var d = await repo.Get...(ct); Heading = d.Message;`. The user has already selected another commit and the old result writes the heading back (this happened in compare mode).
- Right: cancel the previous CTS before a new request; after every `await`, check `ct.IsCancellationRequested` before setting properties; swallow `OperationCanceledException` and send `GitException` to `host.ReportError`.

## 5. Avalonia styles and bindings

- One control type per style selector (`Grid.diffline.add, Border.cell.add` triggers AVLN2000).
- Properties that a style should control (`Background`, `BorderBrush` and so on) must not get a local value in the template, because local values beat styles (this broke badge colors and the compare-row highlight).
- With compiled bindings, a style setter that binds to the data context uses `{ReflectionBinding}`.
- Colors come only from resources in `Tokens.axaml`, defined for both dark and light. No hard-coded hex colors.
- Use `PlaceholderText`, not the obsolete `Watermark`.

## 6. Confirm destructive git operations

Deleting a branch, tag or worktree, or any forced operation, goes through `IDialogService.ConfirmAsync` first. Unmerged branches and worktrees with changes get a second, explicit force confirmation; never default to `-D` / `--force`.

## 7. No secrets in the repository

Tokens and passwords never go into committed files or commit messages. Review `git diff --cached` before committing.

## 8. No second library for the same job

MVVM is CommunityToolkit.Mvvm, JSON is System.Text.Json, git goes through the CLI. Before adding a NuGet package, explain why the existing ones aren't enough.

## 9. Wait for the condition, not for a guessed time

- Wrong: `await Task.Delay(400)` (or a one-shot timer) to wait until rows exist, a load finishes or layout settles.
- Right: wait for the event or the work itself: `LayoutUpdated` or container events, `PendingLoads.WhenIdleAsync`, a `Dispatcher.UIThread.Post` priority. A delay is only fine when the delay is the behaviour (how long a note stays, debounce), as a named constant.

## 10. Desktop-specific tweaks run only where they apply

Releases ship for Linux, Windows and macOS. Settings tuned for GNOME, X11, Wayland, Mesa or libc go behind `OperatingSystem.IsLinux()` (plus a backend check where it matters), with a comment saying why the other platforms keep the defaults.

## 11. Embedded terminals own their input and process lifecycle

Shell input bypasses window shortcuts. Hiding or switching tabs preserves the PTY and screen; closing the terminal, tab or window releases them, including startup that is still pending. Verify directory, interactive input, resize and cleanup with a real PTY, not redirected process streams.

## 12. Syntax detection and caching include extensionless filenames

Resolve known build filenames as well as extensions. Key coloured-text caches by the resolved grammar, not just the extension: Dockerfile and Makefile both have an empty extension. Cover both file-viewer highlighting and diff-cache isolation when adding a filename rule.

## 13. Terminal composition keys belong to the input method

Leave Key.ImeProcessed unhandled even when KeySymbol is present; only committed TextInput may enter the PTY. Verify the key-event path as well as Unicode transport: SendInputAsync alone cannot test IME support.

## 14. Initial PTY dimensions match the visible character grid

Spawn with the laid-out terminal columns and rows, then synchronise again before attaching because layout may change during startup. Verify `stty size` before any window resize; a resize-only test misses a stale initial size. Validate renderer updates with native screenshots of mixed-style text and fallback symbols, not only emulator-buffer assertions.
