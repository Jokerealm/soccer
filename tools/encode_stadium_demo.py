"""Encode actual game captures into a compact animated preview; no repainting."""
import argparse
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('frames', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
paths = sorted(args.frames.glob('*.png'))
if not paths:
    raise SystemExit('No actual game frames found')
frames = []
for path in paths:
    with Image.open(path) as source:
        source.thumbnail((960, 540), Image.Resampling.LANCZOS)
        frames.append(source.convert('P', palette=Image.Palette.ADAPTIVE, colors=256))
durations = [130] * len(frames)
durations[0] = 240
durations[-1] = 500
args.output.parent.mkdir(parents=True, exist_ok=True)
frames[0].save(args.output, save_all=True, append_images=frames[1:], duration=durations,
               loop=0, disposal=2, optimize=True)
with Image.open(args.output) as result:
    print(f'{args.output}: {result.n_frames} frames, {result.size}, {args.output.stat().st_size} bytes')
