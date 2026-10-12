"""Encode actual client HUD capture frames into an animated preview; no repainting."""
import argparse
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("frames", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
paths = sorted(args.frames.glob("*.png"))
if not paths:
    raise SystemExit("No captured client frames found")
frames = [Image.open(path).convert("P", palette=Image.Palette.ADAPTIVE, colors=256) for path in paths]
durations = [90] * len(frames)
durations[0] = 180
durations[-1] = 800
args.output.parent.mkdir(parents=True, exist_ok=True)
frames[0].save(args.output, save_all=True, append_images=frames[1:], duration=durations,
               loop=0, disposal=2, optimize=True)
with Image.open(args.output) as result:
    print(f"{args.output}: {result.n_frames} frames, {result.size[0]}x{result.size[1]}, {args.output.stat().st_size} bytes")
