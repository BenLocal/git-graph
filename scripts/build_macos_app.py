#!/usr/bin/env python3
"""Build "Git Graph.app" and a drag-to-Applications .dmg from an osx-* `dotnet publish` folder.

The bundle gets an Info.plist with the version, an .icns icon made from the PNG icons, and an
ad-hoc code signature (Apple Silicon refuses unsigned code). There is no Developer ID signature or
notarization, so the first launch needs right-click → Open.

iconutil, codesign and hdiutil only exist on macOS; elsewhere --no-dmg still builds the bundle
(without the icon and signature) to check its layout.

Usage: build_macos_app.py --from <publish folder> --version 1.2.3 --rid osx-arm64 [--out artifacts] [--no-dmg]
"""

import argparse
import os
import plistlib
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ICONS = ROOT / "src/GitGraph.App/Assets/Icons"
APP_NAME = "Git Graph"
EXECUTABLE = "GitGraph"
BUNDLE_ID = "io.github.benlocal.gitgraph"

# iconutil's .iconset names: icon_<size>x<size>[@2x].png from the PNG sizes we render.
ICONSET = {
    "icon_16x16.png": 16, "icon_16x16@2x.png": 32,
    "icon_32x32.png": 32, "icon_32x32@2x.png": 64,
    "icon_128x128.png": 128, "icon_128x128@2x.png": 256,
    "icon_256x256.png": 256, "icon_256x256@2x.png": 512,
    "icon_512x512.png": 512,
}


def build_bundle(source, version, out):
    app = out / (APP_NAME + ".app")
    if app.exists():
        shutil.rmtree(app)
    contents = app / "Contents"
    shutil.copytree(source, contents / "MacOS", symlinks=True)
    os.chmod(contents / "MacOS" / EXECUTABLE, 0o755)
    resources = contents / "Resources"
    resources.mkdir(parents=True)

    # CFBundleShortVersionString must be numbers only; a pre-release keeps its full name in CFBundleVersion.
    short = re.match(r"\d+(\.\d+){0,2}", version).group(0)
    info = {
        "CFBundleName": APP_NAME,
        "CFBundleDisplayName": APP_NAME,
        "CFBundleIdentifier": BUNDLE_ID,
        "CFBundleExecutable": EXECUTABLE,
        "CFBundlePackageType": "APPL",
        "CFBundleShortVersionString": short,
        "CFBundleVersion": version,
        "CFBundleIconFile": "gitgraph.icns",
        "CFBundleInfoDictionaryVersion": "6.0",
        "LSMinimumSystemVersion": "12.0",
        "LSApplicationCategoryType": "public.app-category.developer-tools",
        "NSHighResolutionCapable": True,
        "NSHumanReadableCopyright": "Copyright © benshi",
    }
    with open(contents / "Info.plist", "wb") as f:
        plistlib.dump(info, f)

    if shutil.which("iconutil"):
        with tempfile.TemporaryDirectory() as tmp:
            iconset = Path(tmp) / "gitgraph.iconset"
            iconset.mkdir()
            for name, size in ICONSET.items():
                shutil.copyfile(ICONS / ("gitgraph-%d.png" % size), iconset / name)
            subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o", str(resources / "gitgraph.icns")], check=True)
    if shutil.which("codesign"):
        subprocess.run(["codesign", "--force", "--deep", "--sign", "-", str(app)], check=True)
    return app


def build_dmg(app, version, rid, out):
    dmg = out / ("GitGraph-%s-%s.dmg" % (version, rid))
    with tempfile.TemporaryDirectory() as tmp:
        staging = Path(tmp) / "dmg"
        staging.mkdir()
        shutil.copytree(app, staging / app.name, symlinks=True)
        (staging / "Applications").symlink_to("/Applications")
        if dmg.exists():
            dmg.unlink()
        subprocess.run(["hdiutil", "create", "-volname", APP_NAME, "-srcfolder", str(staging),
                        "-ov", "-format", "UDZO", str(dmg)], check=True)
    return dmg


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--from", dest="source", required=True, help="osx-arm64 or osx-x64 publish folder")
    parser.add_argument("--version", required=True)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--out", default="artifacts")
    parser.add_argument("--no-dmg", action="store_true", help="only build the .app (for checking off macOS)")
    args = parser.parse_args()

    source = Path(args.source).resolve()
    if not (source / EXECUTABLE).is_file():
        print("build_macos_app: %s has no %s executable; publish for %s first" % (source, EXECUTABLE, args.rid), file=sys.stderr)
        return 1
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    app = build_bundle(source, args.version, out)
    print(build_dmg(app, args.version, args.rid, out) if not args.no_dmg else app)
    return 0


if __name__ == "__main__":
    sys.exit(main())
