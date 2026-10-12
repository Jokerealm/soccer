#!/usr/bin/env python3
"""Export the 0.12 native effect screenshots to a compact, reproducible GIF."""
import argparse
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--report-dir", type=Path, default=ROOT / "reports/runtime-football-fx-012-release")
args = parser.parse_args()
report = args.report_dir
checks = json.loads((report / "runtime-smoke.json").read_text())
if not checks["passed"] or checks["errors"]:
    raise SystemExit("Native validation must pass before publishing its preview")
sequence = [("34-football-charge.png", 1100), ("35-football-kick-contact.png", 250)]
sequence += [(f"kick-effects-frames/{i:03}.png", 110) for i in range(9)]
sequence += [("36-goal-cyan.png", 900)]
sequence += [(f"goal-effects-frames/{i:03}.png", 150) for i in range(8)]
sequence += [("37-goal-amber.png", 1600)]
frames = []
for filename, duration in sequence:
    with Image.open(report / filename) as source:
        if source.size != (1920, 1080):
            raise ValueError(f"Unexpected native frame dimensions: {filename} {source.size}")
        frames.append(source.convert("RGB").resize((960, 540), Image.Resampling.LANCZOS)
                      .convert("P", palette=Image.Palette.ADAPTIVE, colors=256))
destination = ROOT / "previews/football-fx-012.gif"
frames[0].save(destination, save_all=True, append_images=frames[1:],
               duration=[duration for _, duration in sequence], loop=0, disposal=2, optimize=True)
metadata = {"preview": str(destination.relative_to(ROOT)), "frames": len(frames), "size": [960, 540],
            "bytes": destination.stat().st_size, "source_report": str(report.relative_to(ROOT)),
            "sequence": [{"native_png": f, "duration_ms": d} for f, d in sequence],
            "timing_note": "Native screenshots with test-controlled Core/effect stepping; GIF timing is edited, not a keyboard feel benchmark."}
(ROOT / "reports/football-fx-012-preview.json").write_text(json.dumps(metadata, indent=2) + "\n")
print(json.dumps({k: v for k, v in metadata.items() if k != "sequence"}, indent=2))
