#!/usr/bin/env python3
"""Copy selected source assets and produce an import plan without changing original art."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "UnityProject/Assets/Soccer"
IDS = ["wind-shuttle", "iron-guard", "arc-light"]


def copy(source, relative):
    target = DEST / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / source, target)
    return {"source": source, "target": str(target.relative_to(ROOT)), "sha256": hashlib.sha256(target.read_bytes()).hexdigest()}


def main():
    manifest = json.loads((ROOT / "assets/animation-manifest-v2.json").read_text())
    plan, provenance = {"characters": []}, []
    for character in manifest["characters"]:
        if character["id"] not in IDS:
            continue
        clips = []
        for action in ["run", "turn", "kick"]:
            clip = character["clips"][action]
            source = clip["frames"][0]["texture"]
            texture_path = f"Art/Characters/{Path(source).name}"
            provenance.append(copy(source, texture_path))
            clips.append({
                "action": action, "texture": f"Assets/Soccer/{texture_path}",
                "ppu": clip["nominal_body_height_px"] / (character["display_body_height_px_proposal"] / 48),
                "fps": clip["fps_proposal"],
                "frames": [{"name": f"{action}_{frame['index']:02}", "rect": frame["unity_rect_px"],
                            "pivot": frame["pivot_normalized_bottom_left"]} for frame in clip["frames"]],
            })
        plan["characters"].append({"id": character["id"], "clips": clips})
        provenance.append(copy(f"assets/original/characters/{character['id']}-idle-v2.png", f"Art/Characters/{character['id']}-idle-v2.png"))
    provenance.append(copy("assets/original/arenas/standard-floor-v2.png", "Art/Arena/standard-floor-v2.png"))
    provenance.append(copy("assets/third_party/noto-sans-cjk/NotoSansCJKsc-Regular.otf", "Art/Fonts/NotoSansCJKsc-Regular.otf"))
    provenance.append(copy("assets/third_party/noto-sans-cjk/OFL.txt", "Art/Fonts/OFL.txt"))
    for audio in ["click-a.ogg", "tap-a.ogg", "switch-a.ogg"]:
        provenance.append(copy(f"assets/third_party/kenney/ui-pack/Sounds/{audio}", f"Audio/{audio}"))
    provenance.append(copy("assets/licenses/kenney-ui-pack-license.txt", "Audio/LICENSE.txt"))
    (DEST / "Editor/animation-import.json").parent.mkdir(parents=True, exist_ok=True)
    (DEST / "Editor/animation-import.json").write_text(json.dumps(plan, ensure_ascii=False, indent=2) + "\n")
    node = os.environ.get("SOCCER_NODE_BIN", str(Path.home() / ".cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node"))
    if not Path(node).is_file():
        node = shutil.which("node")
    if not node:
        raise SystemExit("SVG 栅格化需要 Node.js + sharp，可通过 SOCCER_NODE_BIN / SOCCER_NODE_MODULES 指定。")
    subprocess.run([node, str(ROOT / "tools/rasterize_unity_vectors.cjs")], check=True)
    for source in sorted((ROOT / "assets/original/vector").glob("*-v2.svg")):
        provenance.append({"source": str(source.relative_to(ROOT)), "derivative": f"UnityProject/Assets/Soccer/Art/Arena/{source.stem}.png", "conversion": "SVG rasterization, original retained"})
    (DEST / "Art/provenance.json").write_text(json.dumps({"animation_source": "assets/animation-manifest-v2.json", "assets": provenance}, ensure_ascii=False, indent=2) + "\n")
    print(f"Prepared {len(IDS)} characters / 90 animation frames, arena, font, and licensed UI sounds.")


if __name__ == "__main__":
    main()
