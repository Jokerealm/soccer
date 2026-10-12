#!/usr/bin/env python3
"""Deterministic UI display variants; preserve the original HD art and alpha."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "UnityProject/Assets/Soccer/Resources/FrontEnd"
names = ("stim", "bomb", "drive", "thruster", "kick", "tackle", "rifle", "cannons", "trigger")
records = []
for name in names:
    src = ART / f"skill-{name}-v2.png"
    dst = src.with_stem(src.stem + "-gray")
    with Image.open(src).convert("RGBA") as original:
        gray = ImageOps.grayscale(original)
        result = Image.merge("RGBA", (gray, gray, gray, original.getchannel("A")))
        result.save(dst)
        records.append({"source": str(src.relative_to(ROOT)), "display_variant": str(dst.relative_to(ROOT)),
                        "size": result.size, "sha256": hashlib.sha256(dst.read_bytes()).hexdigest()})
(ROOT / "assets/cooldown-icon-provenance.json").write_text(json.dumps({
    "operation": "UI luminance desaturation; RGB channels equal, original resolution and alpha retained",
    "generator": "tools/prepare_cooldown_icons.py", "assets": records}, ensure_ascii=False, indent=2) + "\n")
print(f"Prepared {len(records)} grayscale UI variants")
