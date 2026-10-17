#!/usr/bin/env python3
"""Build a Debian package from a linux-x64 `dotnet publish` folder.

Installs the app to /opt/gitgraph, a `gitgraph` command in /usr/bin, a desktop entry and the app
icons. Needs dpkg-deb (Debian, Ubuntu).

Usage: build_deb.py --from artifacts/publish/linux-x64 --version 1.2.3 [--out artifacts]
"""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ICONS = ROOT / "src/GitGraph.App/Assets/Icons"
ICON_SIZES = [16, 24, 32, 48, 64, 128, 256, 512]

DESKTOP_ENTRY = """[Desktop Entry]
Type=Application
Name=Git Graph
Comment=Browse git history, diffs and worktrees
Exec=gitgraph
Icon=gitgraph
Terminal=false
Categories=Development;RevisionControl;
StartupWMClass=GitGraph
"""

CONTROL = """Package: gitgraph
Version: {version}
Architecture: amd64
Maintainer: benshi <807629978@qq.com>
Installed-Size: {size_kb}
Depends: git, libfontconfig1, libx11-6, libice6, libsm6
Section: devel
Priority: optional
Homepage: https://github.com/BenLocal/git-graph
Description: Git history, worktrees and diffs across workspaces
 A desktop git graph in the spirit of VS Code's Git Graph: commit graph and
 history, diffs, worktrees, contributors and several workspaces.
"""


def debian_version(version):
    """1.2.0-beta.1 sorts before 1.2.0 in semver; Debian needs ~ for that."""
    return version.replace("-", "~", 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--from", dest="source", required=True, help="linux-x64 publish folder")
    parser.add_argument("--version", required=True)
    parser.add_argument("--out", default="artifacts")
    args = parser.parse_args()

    source = Path(args.source).resolve()
    if not (source / "GitGraph").is_file():
        print("build_deb: %s has no GitGraph executable; publish for linux-x64 first" % source, file=sys.stderr)
        return 1
    version = debian_version(args.version)
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory() as tmp:
        pkg = Path(tmp) / "gitgraph"
        shutil.copytree(source, pkg / "opt/gitgraph", symlinks=True)
        os.chmod(pkg / "opt/gitgraph/GitGraph", 0o755)

        bin_dir = pkg / "usr/bin"
        bin_dir.mkdir(parents=True)
        (bin_dir / "gitgraph").symlink_to("/opt/gitgraph/GitGraph")

        apps = pkg / "usr/share/applications"
        apps.mkdir(parents=True)
        (apps / "gitgraph.desktop").write_text(DESKTOP_ENTRY, encoding="utf-8")
        for size in ICON_SIZES:
            target = pkg / ("usr/share/icons/hicolor/%dx%d/apps" % (size, size))
            target.mkdir(parents=True)
            shutil.copyfile(ICONS / ("gitgraph-%d.png" % size), target / "gitgraph.png")
        scalable = pkg / "usr/share/icons/hicolor/scalable/apps"
        scalable.mkdir(parents=True)
        shutil.copyfile(ROOT / "src/GitGraph.App/Assets/gitgraph.svg", scalable / "gitgraph.svg")

        # Debian expects 755 folders and executables, 644 for everything else, whatever the local umask.
        for path in [pkg, *pkg.rglob("*")]:
            if path.is_symlink():
                continue
            if path.is_dir():
                os.chmod(path, 0o755)
            else:
                os.chmod(path, 0o755 if os.stat(path).st_mode & 0o111 else 0o644)

        size_kb = sum(f.stat().st_size for f in pkg.rglob("*") if f.is_file() and not f.is_symlink()) // 1024
        debian = pkg / "DEBIAN"
        debian.mkdir()
        (debian / "control").write_text(CONTROL.format(version=version, size_kb=size_kb), encoding="utf-8")

        deb = out / ("gitgraph_%s_amd64.deb" % version)
        subprocess.run(["dpkg-deb", "--root-owner-group", "--build", str(pkg), str(deb)], check=True,
                       stdout=subprocess.DEVNULL)
    print(deb)
    return 0


if __name__ == "__main__":
    sys.exit(main())
