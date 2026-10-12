#!/usr/bin/env python3
"""Package actual 0.13 native minimap captures, without recreating the game UI."""
import argparse
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--report-dir", type=Path, default=ROOT / "reports/runtime-minimap-013-final")
args = parser.parse_args()
report = args.report_dir.resolve()
validation = json.loads((report / "runtime-smoke.json").read_text())
if not validation["passed"] or validation["errors"]:
    raise SystemExit("Native validation must pass before publishing the preview")
evidence = json.loads((report / "40-minimap-3v3-bounds.json").read_text())
x, y, w, h = evidence["boundsPixels"]
crop = (round(x - 6), round(y - 6), round(x + w + 6), round(y + h + 6))
preview = ROOT / "previews"
with Image.open(report / evidence["screenshot"]) as original:
    if original.size != (evidence["screenWidth"], evidence["screenHeight"]):
        raise ValueError("Native screenshot and UI geometry disagree")
    original.save(preview / "minimap-013-match.png")
    detail = original.crop(crop)
    detail.resize((detail.width * 3, detail.height * 3), Image.Resampling.LANCZOS).save(preview / "minimap-013-detail.png")
sequence = ["40-minimap-3v3.png"] + [f"minimap-motion-frames/{i:03}.png" for i in range(12)]
frames = []
for filename in sequence:
    with Image.open(report / filename) as source:
        detail = source.convert("RGB").crop(crop)
        frames.append(detail.resize((detail.width * 3, detail.height * 3), Image.Resampling.LANCZOS)
                      .convert("P", palette=Image.Palette.ADAPTIVE, colors=256))
frames[0].save(preview / "minimap-013-motion.gif", save_all=True, append_images=frames[1:],
               duration=[1000] + [130] * 11 + [1500], loop=0, disposal=2, optimize=True)
metadata = {"source_report": str(report.relative_to(ROOT)), "native_screenshot": evidence,
            "crop_pixels": list(crop), "motion_frames": sequence,
            "outputs": [{"path": str(p.relative_to(ROOT)), "bytes": p.stat().st_size}
                        for p in [preview / "minimap-013-match.png", preview / "minimap-013-detail.png", preview / "minimap-013-motion.gif"]],
            "timing_note": "Actual native UI and Core-controlled movement; GIF timings are edited. Detail is cropped and enlarged."}
(ROOT / "reports/minimap-013-preview.json").write_text(json.dumps(metadata, indent=2) + "\n")
print(json.dumps({k: v for k, v in metadata.items() if k != "motion_frames"}, indent=2))
