#!/usr/bin/env python3
"""Builds the tree-sitter Zig grammar for code navigation.

TreeSitter.DotNet ships no Zig grammar, so this script fetches tree-sitter-zig at a pinned commit and cross-compiles
it with `zig cc` for every runtime the package supports. The libraries go to native/tree-sitter-zig/runtimes/<rid>/native/,
which is committed: building the app needs neither zig nor the network. Run it again after changing COMMIT.

Usage: python3 scripts/build_tree_sitter_zig.py [--zig zig]
"""

import argparse
import shutil
import subprocess
import tempfile
from pathlib import Path

UPSTREAM = "https://github.com/tree-sitter-grammars/tree-sitter-zig.git"
COMMIT = "6479aa13f32f701c383083d8b28360ebd682fb7d"

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "native" / "tree-sitter-zig"

# .NET runtime identifier → zig target and library file name (the names TreeSitter.DotNet's loader probes).
TARGETS = {
    "linux-x64": ("x86_64-linux-gnu", "libtree-sitter-zig.so"),
    "linux-x86": ("x86-linux-gnu", "libtree-sitter-zig.so"),
    "linux-arm64": ("aarch64-linux-gnu", "libtree-sitter-zig.so"),
    "linux-arm": ("arm-linux-gnueabihf", "libtree-sitter-zig.so"),
    "osx-x64": ("x86_64-macos.11.0", "libtree-sitter-zig.dylib"),
    "osx-arm64": ("aarch64-macos.11.0", "libtree-sitter-zig.dylib"),
    "win-x64": ("x86_64-windows-gnu", "tree-sitter-zig.dll"),
    "win-x86": ("x86-windows-gnu", "tree-sitter-zig.dll"),
    "win-arm64": ("aarch64-windows-gnu", "tree-sitter-zig.dll"),
}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--zig", default="zig", help="the zig executable (default: zig on PATH)")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory() as tmp:
        src = Path(tmp) / "tree-sitter-zig"
        subprocess.run(["git", "init", "-q", str(src)], check=True)
        subprocess.run(["git", "-C", str(src), "fetch", "-q", "--depth", "1", UPSTREAM, COMMIT], check=True)
        subprocess.run(["git", "-C", str(src), "checkout", "-q", "FETCH_HEAD"], check=True)

        sources = [str(p) for p in [src / "src" / "parser.c", src / "src" / "scanner.c"] if p.exists()]
        shutil.rmtree(OUT / "runtimes", ignore_errors=True)
        for rid, (target, name) in TARGETS.items():
            dest = OUT / "runtimes" / rid / "native" / name
            dest.parent.mkdir(parents=True, exist_ok=True)
            # A dylib records its install name, by default the absolute output path on this machine.
            install_name = [f"-Wl,-install_name,@rpath/{name}"] if name.endswith(".dylib") else []
            subprocess.run([args.zig, "cc", "-target", target, "-O2", "-shared", "-fPIC", "-s", *install_name,
                            "-I", str(src / "src"), *sources, "-o", str(dest)], check=True)
            # The linker leaves an import library and debug symbols next to a DLL; only the DLL is loaded.
            for extra in dest.parent.iterdir():
                if extra != dest:
                    extra.unlink()
            print(f"{rid}: {dest.relative_to(ROOT)} ({dest.stat().st_size // 1024} KB)")
        shutil.copy(src / "LICENSE", OUT / "LICENSE")


if __name__ == "__main__":
    main()
