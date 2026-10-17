#!/usr/bin/env python3
"""Render the README screenshots from the demo repositories into docs/screenshots.

Each shot rebuilds the demo (scripts/make_demo.py), adjusts its state file, and runs the app with the
GITGRAPH_SNAPSHOT hook. Shots of the main window render at 1x: at higher scales RenderTargetBitmap draws the
content sheet at the wrong size. The diff window renders at 2x and is scaled down (needs Pillow).

Usage: screenshots.py [--out docs/screenshots] [--only name,...]
"""

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEMO = ROOT / ".demo"
WIDTH = 1600  # stored width of each image

# name -> (theme, extra state, snapshot environment); "scale" picks the render scale (default 1).
SHOTS = {
    "graph": ("Dark", {}, {"GITGRAPH_SNAPSHOT_SELECT": "6", "GITGRAPH_SNAPSHOT_TAB": "0"}),
    "diff-light": ("Light", {}, {"GITGRAPH_SNAPSHOT_SELECT": "0", "GITGRAPH_SNAPSHOT_TAB": "1"}),
    "full-diff": ("Dark", {}, {"GITGRAPH_SNAPSHOT_SELECT": "0", "GITGRAPH_SNAPSHOT_TAB": "1", "GITGRAPH_SNAPSHOT_FULLDIFF": "1",
                               "GITGRAPH_SNAPSHOT_SCALE": "2"}),
    "contributors": ("Dark", {}, {"GITGRAPH_SNAPSHOT_SELECT": "0", "GITGRAPH_SNAPSHOT_STATS": "1", "GITGRAPH_SNAPSHOT_DELAY": "5"}),
    "folder": ("Dark", {"folder": True}, {}),
}


def prepare_state(theme, extra):
    subprocess.run([sys.executable, str(ROOT / "scripts/make_demo.py"), "--out", str(DEMO), "--theme", theme],
                   check=True, stdout=subprocess.DEVNULL)
    state_file = DEMO / "state.json"
    state = json.loads(state_file.read_text(encoding="utf-8"))
    if extra.get("folder"):
        projects = str((DEMO / "projects").resolve())
        ws = state["workspaces"][0]
        ws["openTabs"].append("folder:" + projects)
        ws["activeTab"] = "folder:" + projects
        state["folderViews"] = {projects: {"expanded": ["atlas", "atlas/src"], "selected": "atlas/src/tiles.ts", "treeWidth": 300}}
        # Something worth highlighting in the editor.
        (DEMO / "projects/atlas/src/tiles.ts").write_text(TILES_TS, encoding="utf-8")
    state_file.write_text(json.dumps(state, indent=2), encoding="utf-8")
    return state_file


TILES_TS = """// Tile cache with retry and backoff.
import { fetchTile } from "./net";

export interface Tile {
  x: number;
  y: number;
  zoom: number;
  data: Uint8Array;
}

const cache = new Map<string, Tile>();

export async function loadTile(x: number, y: number, zoom: number): Promise<Tile> {
  const key = `${zoom}/${x}/${y}`;
  const hit = cache.get(key);
  if (hit) return hit;
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      const tile = await fetchTile(x, y, zoom);
      cache.set(key, tile);
      return tile;
    } catch {
      await new Promise((r) => setTimeout(r, 200 * 2 ** attempt));
    }
  }
  throw new Error(`Tile ${key} failed after 3 attempts`);
}
"""


def shoot(name, out):
    theme, extra, env = SHOTS[name]
    state_file = prepare_state(theme, extra)
    target = out / (name + ".png")
    run_env = dict(os.environ, GITGRAPH_STATE=str(state_file), GITGRAPH_SNAPSHOT=str(target),
                   GITGRAPH_SNAPSHOT_SCALE="1", GITGRAPH_BACKEND="x11")
    run_env.update(env)
    subprocess.run(["dotnet", "run", "--project", str(ROOT / "src/GitGraph.App")], env=run_env, check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        from PIL import Image
        with Image.open(target) as im:
            if im.width > WIDTH:
                im.resize((WIDTH, round(im.height * WIDTH / im.width)), Image.LANCZOS).save(target, optimize=True)
    except ImportError:
        pass
    print(target.relative_to(ROOT))


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default=str(ROOT / "docs/screenshots"))
    parser.add_argument("--only", help="comma-separated shot names: " + ", ".join(SHOTS))
    args = parser.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    names = args.only.split(",") if args.only else list(SHOTS)
    subprocess.run(["dotnet", "build", str(ROOT / "src/GitGraph.App")], check=True, stdout=subprocess.DEVNULL)
    for name in names:
        shoot(name, out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
