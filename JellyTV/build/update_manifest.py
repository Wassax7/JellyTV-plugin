#!/usr/bin/env python3
"""Update dist/manifest.json with latest build metadata."""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path


def env(name: str) -> str:
    value = os.environ.get(name)
    if value is None:
        print(f"Environment variable {name} is required", file=sys.stderr)
        sys.exit(6)
    return value


def load_manifest(path: Path) -> list[dict]:
    if not path.exists() or path.stat().st_size == 0:
        return []
    try:
        with path.open("r", encoding="utf-8") as fh:
            data = json.load(fh)
    except json.JSONDecodeError:
        return []
    if isinstance(data, list):
        return data
    return [data]


def main() -> None:
    path = Path(env("MANIFEST_PATH"))

    plugin = {
        "name": env("NAME"),
        "guid": env("GUID"),
        "overview": env("OVERVIEW"),
        "description": env("DESCRIPTION"),
        "category": env("CATEGORY"),
        "owner": env("OWNER"),
        "imageUrl": env("IMAGE_URL"),
    }

    version_entry = {
        "version": env("VERSION"),
        "targetAbi": env("TARGET_ABI"),
        "sourceUrl": env("DOWNLOAD_URL"),
        "checksum": env("CHECKSUM"),
        "timestamp": env("TIMESTAMP"),
    }

    manifest = load_manifest(path)

    for entry in manifest:
        if entry.get("guid") == plugin["guid"]:
            entry.update(plugin)
            existing = [
                v
                for v in entry.get("versions", [])
                if v.get("version") != version_entry["version"]
            ]
            entry["versions"] = [version_entry] + existing
            break
    else:
        plugin["versions"] = [version_entry]
        manifest.append(plugin)

    tmp_path = path.parent / f"{path.name}.tmp"
    tmp_path.parent.mkdir(parents=True, exist_ok=True)
    with tmp_path.open("w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2)
        fh.write("\n")
    os.replace(tmp_path, path)


if __name__ == "__main__":
    main()
