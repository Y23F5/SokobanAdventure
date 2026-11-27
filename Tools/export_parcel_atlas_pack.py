"""Copy the PlayTest3 Parcel-Atlas GLBs into a Unity import pack with box metadata."""

import argparse
import json
import shutil
from pathlib import Path


# Authored metadata for this source pack. Unity classifies by the exported category.
BOX_APPEARANCES = {
    "wooden-crate-and-pallet.glb": "warehouse",
    "mechanical-crate.glb": "factory",
    "log-bundle.glb": "forest",
    "regrown-tree.glb": "forest",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="PlayTest3 ground-centered manifest.json")
    parser.add_argument("output", type=Path, help="new directory for the Unity import pack")
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    output = args.output.resolve()
    if output.is_relative_to(source.parent) or any(part.lower() == "playtest3" for part in output.parts):
        parser.error("choose an output directory outside the source pack and PlayTest3")

    manifest = json.loads(source.read_text(encoding="utf-8"))
    assets = manifest["assets"]
    files = {}
    for entry in assets:
        file = entry["file"]
        if ("\\" in file or ":" in file or any(part in ("", ".", "..") for part in file.split("/"))
                or not file.lower().endswith(".glb")):
            parser.error(f"invalid GLB path: {file}")
        path = (source.parent / file).resolve(strict=True)
        if not path.is_relative_to(source.parent) or not path.is_file():
            parser.error(f"GLB is outside the source pack or is not a file: {file}")
        files[file] = path
    if len({file.lower() for file in files}) != len(assets):
        parser.error("source manifest contains duplicate paths")
    if not BOX_APPEARANCES.keys() <= files.keys():
        parser.error("source pack is missing an authored box appearance")

    output.mkdir(parents=True, exist_ok=False)
    for entry in assets:
        file = entry["file"]
        if file in BOX_APPEARANCES:
            entry.setdefault("category", "box")
            entry.setdefault("theme", BOX_APPEARANCES[file])
        destination = output / file
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(files[file], destination)
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
