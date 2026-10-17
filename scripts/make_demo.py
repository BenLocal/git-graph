#!/usr/bin/env python3
"""Create demo repositories and a matching app state file.

Builds <out>/projects/atlas (branches, merges, tags, two linked worktrees and
uncommitted changes) and <out>/projects/beacon, then writes <out>/state.json
with two workspaces that point at them. Run the app against it with
GITGRAPH_STATE=<out>/state.json.

Usage: make_demo.py [--out DIR] [--theme System|Dark|Light]
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

MEI = "Mei Lin <mei@example.com>"
JONAS = "Jonas Weber <jonas@example.com>"
ANA = "Ana Souza <ana@example.com>"


def git(cwd, *args, date=None):
    env = dict(os.environ, GIT_CONFIG_NOSYSTEM="1")
    if date:
        env["GIT_AUTHOR_DATE"] = env["GIT_COMMITTER_DATE"] = date
    subprocess.run(["git", *args], cwd=cwd, env=env, check=True)


def commit(repo, path, line, date, message, author):
    f = repo / path
    f.parent.mkdir(parents=True, exist_ok=True)
    with f.open("a", encoding="utf-8") as out:
        out.write(line + "\n")
    git(repo, "add", path)
    git(repo, "commit", "-q", "-m", message, "--author=" + author, date=date)


def init(repo, name, email):
    repo.mkdir(parents=True)
    git(repo, "init", "-q", "-b", "main")
    git(repo, "config", "user.name", name)
    git(repo, "config", "user.email", email)
    git(repo, "config", "commit.gpgsign", "false")


def make_atlas(projects):
    atlas = projects / "atlas"
    init(atlas, "Mei Lin", "mei@example.com")
    commit(atlas, "README.md", "# Atlas", "2026-09-01T09:00", "Initial project layout", MEI)
    commit(atlas, "src/app.ts", "export {}", "2026-09-02T10:00", "Add application entry point", MEI)
    git(atlas, "tag", "v0.1.0")

    git(atlas, "checkout", "-q", "-b", "feature/tiles")
    commit(atlas, "src/tiles.ts", "tiles v1", "2026-09-03T11:00", "Render vector tiles from the cache", JONAS)
    commit(atlas, "src/tiles.ts", "tiles v2", "2026-09-05T15:00", "Retry tile fetches with backoff", JONAS)
    git(atlas, "checkout", "-q", "main")
    commit(atlas, "src/app.ts", "routing", "2026-09-04T09:30", "Add hash-based routing", MEI)

    git(atlas, "checkout", "-q", "-b", "fix/zoom")
    commit(atlas, "src/zoom.ts", "zoom fix", "2026-09-06T08:10", "Clamp zoom level to the supported range", ANA)
    git(atlas, "checkout", "-q", "main")
    git(atlas, "merge", "-q", "--no-ff", "-m", "Merge branch 'feature/tiles'", "feature/tiles", date="2026-09-07T12:00")
    commit(atlas, "src/style.css", "body{}", "2026-09-08T16:20", "Tighten the sidebar spacing", MEI)
    git(atlas, "merge", "-q", "--no-ff", "-m", "Merge branch 'fix/zoom'", "fix/zoom", date="2026-09-09T10:00")
    git(atlas, "tag", "-a", "v0.2.0", "-m", "Release 0.2.0", date="2026-09-09T10:00")

    git(atlas, "checkout", "-q", "-b", "feature/search")
    commit(atlas, "src/search.ts", "search", "2026-09-10T09:00", "Search places by name", ANA)
    commit(atlas, "src/search.ts", "fuzzy", "2026-09-12T14:00", "Rank search results with fuzzy matching", ANA)
    git(atlas, "checkout", "-q", "main")
    commit(atlas, "src/app.ts", "perf", "2026-09-11T11:11", "Cache parsed style sheets", JONAS)
    commit(atlas, "CHANGELOG.md", "0.2.1", "2026-09-13T17:45", "Prepare 0.2.1 changelog", MEI)
    git(atlas, "checkout", "-q", "-b", "release/0.3")
    commit(atlas, "package.json", "0.3.0", "2026-09-14T09:00", "Bump version to 0.3.0", MEI)
    git(atlas, "checkout", "-q", "main")

    git(atlas, "worktree", "add", "-q", "../atlas-search", "feature/search")
    git(atlas, "worktree", "add", "-q", "-b", "hotfix/crash", "../atlas-hotfix", "v0.2.0")
    commit(projects / "atlas-hotfix", "src/app.ts", "null check", "2026-09-15T08:30",
           "Guard against a missing map container", ANA)

    # Leave uncommitted changes so the graph shows its dashed working-tree row.
    app = atlas / "src/app.ts"
    app.write_text(app.read_text(encoding="utf-8").replace("routing\n", "hash routing with fallback\n")
                   + "export const debug = true;\n", encoding="utf-8")
    (atlas / "TODO.md").write_text("notes\n", encoding="utf-8")
    return atlas


def make_beacon(projects):
    beacon = projects / "beacon"
    init(beacon, "Jonas Weber", "jonas@example.com")
    commit(beacon, "main.go", "package main", "2026-08-20T10:00", "Start the beacon service", JONAS)
    commit(beacon, "main.go", "health", "2026-08-22T10:00", "Expose a health endpoint", JONAS)
    return beacon


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default=".demo", help="output directory; its projects/ folder is recreated (default: .demo)")
    parser.add_argument("--theme", default="Dark", choices=["System", "Dark", "Light"])
    args = parser.parse_args()

    out = Path(args.out).resolve()
    projects = out / "projects"
    # Only the repositories are recreated; other files in <out>, such as snapshots, are kept.
    if projects.exists():
        shutil.rmtree(projects)
    projects.mkdir(parents=True)

    atlas = make_atlas(projects)
    beacon = make_beacon(projects)

    state = {
        "workspaces": [
            {"id": "maps", "name": "Maps team", "folders": [str(projects)],
             "openTabs": [str(atlas), str(projects / "atlas-hotfix")], "activeTab": str(atlas)},
            {"id": "svc", "name": "Services", "folders": [str(beacon)], "openTabs": [], "activeTab": None},
        ],
        "activeWorkspaceId": "maps",
        "theme": args.theme,
        "showRemoteBranches": True,
        "splitDiff": False,
    }
    (out / "state.json").write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
    print("demo ready: %s" % (out / "state.json"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
