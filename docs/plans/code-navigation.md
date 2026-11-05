# Plan: code navigation in the folder tab's code viewer

> Go to definition, find references, go to implementation and same-file variable jumps in the read-only code viewer, using an embedded tree-sitter parser and a per-repository SQLite index. No language server processes.

Status: implemented (2026-11-05). Differences from the plan below are listed in section 6.

## 1. Goal

In the folder tab's code viewer (`Views/FolderTabView.axaml`, a read-only AvaloniaEdit `TextEditor`):

| Action | Default gesture (as in VS Code) | Result |
|---|---|---|
| Go to definition | F12, Ctrl/Cmd+click | Jump to the definition; a list when there are several |
| Find references | Shift+F12 | A list of every use; pick one to jump |
| Go to implementation | Ctrl/Cmd+F12 | Types that implement or inherit the type or interface under the caret |
| Same-file occurrences | Caret on an identifier | Uses of the same declaration are highlighted, scope-aware |
| Back / forward | Alt+← / Alt+→ | Navigation history across files |

First languages: C#, Go, Rust, Python, JavaScript, TypeScript/TSX. Other files fall back to name-based search with `git grep -w`.

Non-goals: type-checked resolution (overloads, generics, extension methods), rename and editing. Results are syntax-based and name-based, so a list may contain unrelated symbols with the same name. They are ranked, not hidden.

## 2. Decisions taken

- **Embedded tree-sitter through NuGet (`TreeSitter.DotNet` 1.3.0, MIT)**, not language servers: no external processes, no index wait per language, nothing for the user to install.
- **SQLite index (`Microsoft.Data.Sqlite`)** per repository, synced incrementally on events, not on a timer.
- **Live parsing remains** for the current file and for exact reference positions; the index stores definitions and a name-to-file map only.

Spike results (2026-11-05, macOS arm64, throwaway console app):
- The package ships natives for osx-arm64, linux-x64 and win-x64 (and more). Package total 640 MB, about 62 MB per RID; the 11 grammars we want (plus the runtime) are 18 MB per RID.
- `FolderTabViewModel.cs` (620 lines): parse 15 ms, a definitions query 17 ms; first `new Language("C-Sharp")` 450 ms (native load + JIT).
- Scope walk resolved 48 of 50 `node` identifiers to the correct one of 12 declarations; the 2 misses were not examined (probably pattern variables `is T node`, not in the spike query).
- The package contains no `.scm` query files; we write and maintain our own.

## 3. Architecture

```
FolderTabView (AvaloniaEdit) ── gestures, highlight, result list
        │
FolderTabViewModel ── NavigationHistory, OpenAt(path, line, col)
        │
GitGraph.Core/Navigation/
  ├─ LanguageRegistry   extension → tree-sitter language + embedded queries
  ├─ SourceParser       parse a text; caches Language/Parser/Query per language
  ├─ LocalResolver      scope-aware same-file resolution (locals query)
  ├─ SymbolExtractor    definitions, supertypes, identifier names of one file
  ├─ SymbolIndex        SQLite store + queries
  ├─ IndexSync          incremental sync (git ls-files -s / git status / file walk)
  └─ CodeNavigator      definition / references / implementations; index first, git grep fallback
```

- Everything except the view lives in Core and is tested in `GitGraph.Core.Tests`. Core gains two NuGet dependencies, so `architecture.md` §2 ("Core may depend on BCL") changes to "BCL, DiffPlex, TreeSitter.DotNet, Microsoft.Data.Sqlite", with the reason. Checklist item 8: neither library duplicates an existing one.
- git calls go through `GitRunner` with machine-readable output (`ls-files -s -z`, `status -z`, `grep -z`).

### 3.1 Index schema

Stored at `LocalApplicationData/GitGraph/index/<md5 of common dir>.db`, keyed like `StatsCache.cs:25` so worktrees share one database. WAL mode, one writer per repository.

```sql
meta(key TEXT PRIMARY KEY, value TEXT)            -- schema_version, queries_hash
files(id INTEGER PRIMARY KEY, path TEXT UNIQUE,   -- path relative to the work tree
      blob TEXT, mtime INTEGER, size INTEGER, lang TEXT)
symbols(file_id, name, kind, line, col, end_line, end_col, container)  -- definitions only
supers(file_id, type_name, super_name, line, col)                      -- for implementations
names(name, file_id, PRIMARY KEY(name, file_id)) WITHOUT ROWID         -- which files mention a name
CREATE INDEX symbols_name ON symbols(name);
CREATE INDEX supers_super ON supers(super_name);
```

- Reference positions are not stored (millions of rows in large repositories). References = `names` → candidate files → parse them → exact positions.
- `queries_hash` is a hash of all embedded `.scm` files plus the package version; a mismatch drops and rebuilds the index.

### 3.2 Sync

- Tracked files: `git ls-files -s -z` gives each file's blob OID. Files whose OID differs from `files.blob` are re-parsed; files gone from the list are deleted. Identical blobs across branches and worktrees are cheap, because only changed paths re-parse.
- Modified and untracked files: from `git status -z` (already run by the app); compare mtime + size, re-parse on change. Ignored files are never indexed.
- A folder that is not inside a git repository: walk the directory (skipping hidden directories), mtime + size only. Size cap: the viewer's `MaxFileBytes`.
- Triggers: folder tab opened or activated, window activation (reuse the existing 3 s throttle), and before a navigation request only the involved files are refreshed. No timer and no working-tree `FileSystemWatcher` (noise from `node_modules`, inotify limits on Linux).
- First build runs in the background with a parallel parse (one parser per worker; tree-sitter parsers are not thread-safe) and batched transactions. Until it finishes, navigation uses the fallback in §3.3.
- Closing the tab cancels sync; check the token after every await and inside the parse loop (pitfall 2026-11-01, statistics lifetime).

### 3.3 Query flow

1. Find the identifier node at the caret with `GetNamedDescendantForPosition`; ignore comments and strings.
2. Locals query: if it resolves to a local or parameter in the current file, return that (definition) or its uses (references). Done.
3. Otherwise, definition: `symbols WHERE name = ?`, ranked by same file > same directory > same language > others.
4. References: `names WHERE name = ?` → parse those files → identifier nodes with that name that are not definitions.
5. Implementations: `supers WHERE super_name = ?`.
6. Fallback while the index is incomplete or for unsupported languages: `git grep -n -w --column -z -I -F -e <name>`, parse the hit files whose language is supported, raw hits for the rest.

## 4. Steps

Each step ends green on `make lint`, `make build` (0 warnings) and `make test`.

### Step 1: parser and per-language queries (Core)
- Add `TreeSitter.DotNet` to Core. Add an MSBuild target that keeps only the allow-listed grammars (`libtree-sitter`, `-c-sharp`, `-go`, `-rust`, `-python`, `-javascript`, `-typescript`, `-tsx`) in build and publish output.
- `LanguageRegistry`, `SourceParser`, embedded queries `Navigation/Queries/<lang>/{definitions,locals}.scm`.
- Tests (written first): for each language a small fixture file with expected definitions (name, kind, line); comments and strings never produce identifiers.
- Check: `make publish RID=osx-arm64` grows by about 18 MB, not 62 MB; the app starts from the published folder.

### Step 2: same-file resolution (Core)
- `LocalResolver`: identifier at position → declaration → all uses in its scope.
- Tests: shadowing (same name in two methods, a nested block, a lambda parameter), pattern variables (`is T x`), `foreach` variables, Go short declarations, Rust `let` shadowing, Python function scope.

### Step 3: SQLite index and sync (Core)
- Add `Microsoft.Data.Sqlite`. `SymbolIndex` (schema, upsert per file in one transaction, queries) and `IndexSync`.
- Tests against real temporary repositories:
  - first build indexes every supported file; unsupported and ignored files are skipped;
  - committing a change to one file re-parses only that file (count parses through a test hook);
  - switching branches re-parses only the changed paths; a deleted file disappears from the index;
  - untracked and modified-but-unstaged files are indexed and updated;
  - a changed `queries_hash` rebuilds the index;
  - cancellation stops the sync and leaves a consistent database;
  - a non-git folder is indexed by the walk.
- Check: index a real large repository (this one and a big open-source one), measure first build and no-op sync time with a Stopwatch, and record the numbers in this plan.

### Step 4: `CodeNavigator` (Core)
- Definition, references and implementations through the index, with the `git grep` fallback.
- Tests: cross-file definition, references across files, implementations of an interface (C# `base_list`, Go is out: structural), Rust `impl Trait for`, TypeScript `implements` / `extends`, Python base classes; ranking order; fallback when the index is empty.

### Step 5: viewer UI (App)
- Commands in `ShortcutService.Commands` (gestures in §1) and actions in `Views/MainWindow.Shortcuts.cs`; context menu entries on the editor; Ctrl/Cmd+click with an underline on hover.
- `FolderTabViewModel`: `OpenAtAsync(path, line, col)` (selects the node in the tree, loads the file, scrolls and places the caret), a navigation history, and a results list (one `MenuFlyout`-free popup or panel; no per-row `ContextMenu`, pitfall 2026-10-24).
- Same-file highlight as an AvaloniaEdit background renderer with colours from `Tokens.axaml` (dark and light).
- Index progress in the folder tab's status area while the first build runs.
- Tests in `GitGraph.App.Tests` with `FakeHost`: jump opens the right file and line, history back and forward, a stale navigation result does not overwrite a newer one (checklist item 4).
- Check: snapshots in dark and light with a highlighted identifier and an open results list.

### Step 6: docs
- `architecture.md`: Core dependencies, the navigation flow, the index location and sync triggers.
- `tech-stack.md`: the two packages and the grammar allow-list.
- `README.md`: the feature and third-party notices for the grammars and any query files derived from upstream (MIT / Apache-2.0).
- `docs/memory/known-pitfalls.md`: anything non-obvious found on the way.

## 5. Risks and open questions

- **Native loading in published apps**: the spike ran with `dotnet run`. Verify the self-contained publish on all three RIDs in CI (the macOS `.app` bundle layout in particular) before building on it.
- **ABI**: the package's runtime and grammars must match (spike: ABI 15). Pin the package version; upgrade only with the fixture tests.
- **Name-based cross-file results** can include unrelated symbols with the same name. Ranking helps; type-aware resolution is out of scope.
- **Very large repositories**: the first build may take minutes. It must stay in the background, be cancellable, and resume where it stopped (files already in the database are skipped).
- **Folder tabs that contain several repositories**: one index per repository found above each opened file; confirm this matches how users use folder tabs.
- **Memory**: one parser and tree per worker during the build; trees are disposed after extraction. Measure with the same method as the 2026-10-23 memory notes.

## 6. As built

- **Index layout**: parse results are keyed by blob hash (`blobs`), and `files(root, path)` maps each work tree's paths to them, so a repository's worktrees share one database and identical content is parsed once. Content no file points at is dropped after each sync, so switching back to a branch re-parses the files that differ.
- **Locals**: besides scope, a local is visible in its own scope only after its declaration (C#, Go, Rust, JavaScript, TypeScript; not Python, whose comprehensions use names before their `for`); in Rust a `let` takes effect after the whole statement, so `let x = x + 1` reads the old `x`.
- **References** skip identifiers that resolve to a local of the same name; **definitions** drop a class's constructors when the class itself is found; **implementations** follow subtypes transitively and, on an interface member, list the members of that name in the subtypes.
- **Results** are relative to the folder tab; a result outside the folder (the tab is a subfolder of a repository) opens without a tree row.
- **Numbers** (M-series Mac): this repository, 120 files: first sync 2.0 s (mostly grammar loading and JIT), no-op 32 ms. A 2,655-file TypeScript repository: first sync 3.8 s, no-op 54 to 71 ms, a definition query 6 ms, index 16 MB with its WAL. Publish for osx-arm64 grows by 13 MB; the published app loads the grammars and runs lookups.
- **Results list**: picking a result (click or arrow keys) previews its file beside the list with the result washed; Enter or a double-click jumps and records history. Holding Ctrl/Cmd underlines the name under the pointer (`CodeNavigator.NameAtAsync`).
