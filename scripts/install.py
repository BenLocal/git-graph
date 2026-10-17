#!/usr/bin/env python3
"""Install, update or uninstall a published Git Graph build for the current user.

install copies a `dotnet publish` output folder to <prefix>/opt/gitgraph,
replacing any previous version, links <prefix>/bin/gitgraph to it and, on
Linux, adds a desktop entry and icon so the app shows up in the launcher, plus
a shortcut on the desktop unless --no-shortcut is given.

Usage:
  install.py install --from artifacts/publish/linux-x64 [--prefix ~/.local] [--no-shortcut]
  install.py uninstall [--prefix ~/.local]
"""

import argparse
import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path

APP_ID = "gitgraph"
EXE = "GitGraph.exe" if os.name == "nt" else "GitGraph"
ASSETS = Path(__file__).resolve().parent.parent / "src/GitGraph.App/Assets"
ICON_SVG = ASSETS / "gitgraph.svg"
ICON_PNG_DIR = ASSETS / "Icons"  # gitgraph-<size>.png, from scripts/render_icon.py
ICON_SIZES = [16, 24, 32, 48, 64, 128, 256, 512]

DESKTOP_ENTRY = """[Desktop Entry]
Type=Application
Name=Git Graph
Comment=Browse git history, diffs and worktrees
Exec={exe}
Icon={icon}
Terminal=false
Categories=Development;RevisionControl;
StartupWMClass=GitGraph
"""


def paths(prefix):
    return {
        "app": prefix / "opt" / APP_ID,
        "bin": prefix / "bin" / APP_ID,
        "desktop": prefix / "share/applications" / (APP_ID + ".desktop"),
        "icon": prefix / "share/icons/hicolor/scalable/apps" / (APP_ID + ".svg"),
        "pngs": [prefix / ("share/icons/hicolor/%dx%d/apps" % (s, s)) / (APP_ID + ".png") for s in ICON_SIZES],
    }


def desktop_shortcut():
    """The shortcut on the user's desktop folder (its name may be localized), or None without one."""
    folder = None
    if shutil.which("xdg-user-dir"):
        out = subprocess.run(["xdg-user-dir", "DESKTOP"], capture_output=True, text=True, check=False)
        folder = out.stdout.strip() or None
    folder = Path(folder) if folder else Path.home() / "Desktop"
    if not folder.is_dir() or folder == Path.home():
        return None
    return folder / (APP_ID + ".desktop")


def add_shortcut(entry):
    shortcut = desktop_shortcut()
    if shortcut is None:
        print("note: no desktop folder found, skipped the desktop shortcut")
        return
    shortcut.write_text(entry, encoding="utf-8")
    shortcut.chmod(0o755)
    # GNOME's desktop icons only launch entries that are executable and marked trusted.
    if shutil.which("gio"):
        subprocess.run(["gio", "set", str(shortcut), "metadata::trusted", "true"], check=False)
    print("shortcut: %s" % shortcut)


def refresh_icon_cache(prefix):
    # Optional: without a cache GTK scans the folders itself.
    tool = shutil.which("gtk-update-icon-cache")
    theme = prefix / "share/icons/hicolor"
    if tool and (theme / "index.theme").exists():
        subprocess.run([tool, "-q", "-t", str(theme)], check=False)


def refresh_desktop_db(prefix):
    # Optional: the launcher also notices new entries on its own, just more slowly.
    tool = shutil.which("update-desktop-database")
    if tool:
        subprocess.run([tool, str(prefix / "share/applications")], check=False)


def install(source, prefix, shortcut):
    if not (source / EXE).is_file():
        print("install: %s not found; run `make publish` first" % (source / EXE), file=sys.stderr)
        return 1
    p = paths(prefix)
    app = p["app"]
    updating = app.exists()

    # Copy next to the target first, then swap, so a failed copy leaves the old version intact.
    staging = app.with_name(APP_ID + ".new")
    shutil.rmtree(staging, ignore_errors=True)
    shutil.copytree(source, staging, symlinks=True)
    old = app.with_name(APP_ID + ".old")
    shutil.rmtree(old, ignore_errors=True)
    if updating:
        app.rename(old)
    staging.rename(app)
    shutil.rmtree(old, ignore_errors=True)

    p["bin"].parent.mkdir(parents=True, exist_ok=True)
    if p["bin"].is_symlink() or p["bin"].exists():
        p["bin"].unlink()
    p["bin"].symlink_to(app / EXE)

    if platform.system() == "Linux":
        p["icon"].parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ICON_SVG, p["icon"])
        for size, png in zip(ICON_SIZES, p["pngs"]):
            png.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ICON_PNG_DIR / ("gitgraph-%d.png" % size), png)
        refresh_icon_cache(prefix)
        p["desktop"].parent.mkdir(parents=True, exist_ok=True)
        # An absolute icon path works even where the launcher or desktop doesn't search ~/.local icons.
        entry = DESKTOP_ENTRY.format(exe=app / EXE, icon=p["pngs"][ICON_SIZES.index(256)])
        p["desktop"].write_text(entry, encoding="utf-8")
        refresh_desktop_db(prefix)
        if shortcut:
            add_shortcut(entry)

    print("%s %s" % ("updated" if updating else "installed", app))
    print("command: %s" % p["bin"])
    if str(p["bin"].parent) not in os.environ.get("PATH", "").split(os.pathsep):
        print("note: %s is not on PATH" % p["bin"].parent)
    return 0


def uninstall(prefix):
    p = paths(prefix)
    shutil.rmtree(p["app"], ignore_errors=True)
    for f in [p["bin"], p["desktop"], p["icon"], *p["pngs"]]:
        if f.is_symlink() or f.exists():
            f.unlink()
    if platform.system() == "Linux":
        refresh_desktop_db(prefix)
        # Only remove a shortcut that points at this prefix's install.
        shortcut = desktop_shortcut()
        if shortcut and shortcut.is_file() and str(p["app"] / EXE) in shortcut.read_text(encoding="utf-8"):
            shortcut.unlink()
    print("removed %s" % p["app"])
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("action", choices=["install", "uninstall"])
    parser.add_argument("--from", dest="source", help="publish output folder (install only)")
    parser.add_argument("--prefix", default="~/.local", help="install prefix (default: ~/.local)")
    parser.add_argument("--no-shortcut", action="store_true", help="don't put a shortcut on the desktop")
    args = parser.parse_args()

    prefix = Path(args.prefix).expanduser().resolve()
    if args.action == "uninstall":
        return uninstall(prefix)
    if not args.source:
        parser.error("install needs --from")
    return install(Path(args.source).resolve(), prefix, not args.no_shortcut)


if __name__ == "__main__":
    sys.exit(main())
