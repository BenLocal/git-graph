# Development & Releases

## Running from Source

```sh
dotnet run --project src/GitGraph.App
```

Requires the **.NET 10 SDK** and `git` on `PATH`.

### Configuration & Environment Variables

- **State file**: Saved to `~/.config/GitGraph/state.json` (`%APPDATA%\GitGraph\state.json` on Windows).
- **`GITGRAPH_STATE`**: Override the path to `state.json`.
- **`GITGRAPH_REDUCED_MOTION=1`**: Disable UI animations.
- **`GITGRAPH_BACKEND=wayland`**: On Linux, try Avalonia's experimental native Wayland backend instead of the default X11/XWayland backend.

## Project Layout & Tests

| Project | What it holds |
| --- | --- |
| `src/GitGraph.Core` | Git CLI wrapper and output parsers, graph lane layout, unified diff parsing, symbol indexing, and workspace persistence |
| `src/GitGraph.App` | Avalonia desktop UI (MVVM with CommunityToolkit.Mvvm) |
| `tests/GitGraph.Core.Tests` | Unit tests and integration tests against temporary git repositories |
| `tests/GitGraph.App.Tests` | View model and headless UI/terminal integration tests |

Run all tests:

```sh
dotnet test
```

## Make Targets

Run `make` (or `make help`) to list all available targets. Target definitions live in `mk/*.mk` and helper scripts live in `scripts/`.

| Target | What it does |
| --- | --- |
| `make build` / `make test` / `make run` | Build, test, or run (`CONFIG=Release` for a release build) |
| `make lint` | Check formatting and analyzers (`dotnet format --verify-no-changes`) |
| `make publish` | Self-contained build for this machine in `artifacts/publish/<rid>` (`RID=linux-x64` etc. to override) |
| `make install` | Install or update the app in `~/.local` (`PREFIX=...` to change): files in `opt/gitgraph`, a `gitgraph` command in `bin`, and a desktop entry/shortcut on Linux (`SHORTCUT=0` skips the shortcut) |
| `make uninstall` | Remove what `make install` added |
| `make demo-run` | Create demo repositories in `.demo/` and open the app on them |
| `make snapshot SELECT=3 COMPARE=6` | Render the demo window to `.demo/snapshot.png` |
| `make icons` | Re-render the PNG and ICO app icons from `scripts/render_icon.py` (needs Pillow) |
| `make screenshots` | Render the README screenshots from the demo repositories into `docs/screenshots` |
| `make deb` | Build the `.deb` package locally |
| `make tag VERSION=x.y.z` | Run lint and tests, then create annotated tag `vx.y.z` |
| `make reindex` | Rebuild the generated indexes in `AGENTS.md` and `docs/` |

## CI & Releases

Every push runs CI (`.github/workflows/ci.yml`): formatting and analyzers (`dotnet format --verify-no-changes`, also `make lint`), a Release build, and the test suite.

To create a release, tag a version and push the tag:

```sh
make tag VERSION=0.1.0      # lint and test, then create the annotated tag v0.1.0
git push origin v0.1.0
```

The Release workflow (`.github/workflows/release.yml`) builds each platform artifact and publishes a GitHub release with notes generated from commits. Versions with a prerelease suffix (such as `v0.2.0-beta.1`) are published as pre-releases.

| Platform | Files |
| --- | --- |
| Linux x64 | `GitGraph-<version>-linux-x64.tar.gz`, `gitgraph_<version>_amd64.deb` |
| Windows x64 | `GitGraph-<version>-win-x64.zip`, `GitGraph-<version>-win-x64-setup.exe` (Inno Setup; per-user or all users) |
| macOS (Apple Silicon) | `GitGraph-<version>-osx-arm64.dmg` |

The macOS app is ad-hoc signed rather than notarized, so the first launch requires right-click → **Open**. **Help → About** shows the app version and source commit (local builds show `0.0.0-dev`), and **Check for updates** compares it against the latest GitHub release.

## Third-Party Credits & Licenses

- **Fonts**: The UI uses the system sans stack (Segoe UI on Windows, Noto Sans on Linux), falling back to IBM Plex Sans. Code uses Ubuntu Sans Mono, Noto Sans CJK SC, and DejaVu Sans Mono when installed, falling back to IBM Plex Mono. Both IBM Plex fonts are bundled under the SIL Open Font License (`src/GitGraph.App/Assets/Fonts/OFL.txt`).
- **Icons**: [Lucide](https://lucide.dev) (ISC license) via `Lucide.Avalonia`.
- **Code navigation & syntax**: [tree-sitter](https://tree-sitter.github.io) and its C#, Go, Rust, Java, C, C++, Python, JavaScript, and TypeScript grammars (MIT) via `TreeSitter.DotNet` (MIT); [tree-sitter-zig](https://github.com/tree-sitter-grammars/tree-sitter-zig) (MIT, `native/tree-sitter-zig/LICENSE`); SQLite (public domain) via `Microsoft.Data.Sqlite` (MIT); and the Zig TextMate grammar from [vscode-zig](https://github.com/ziglang/vscode-zig) (MIT, `src/GitGraph.App/Syntaxes/zig.LICENSE.txt`).
