#!/usr/bin/env python3
"""Author v2 projected SVG layers from the shared visual camera proposal."""
from pathlib import Path
import json
import math

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'assets/original/vector'
INK, PAPER = '#132C3C', '#F7F0E1'


def save(name, width, height, body, title):
    (DEST / f'{name}-v2.svg').write_text(
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
        f'viewBox="0 0 {width} {height}"><title>{title}</title>{body}</svg>\n')


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    spec = json.loads((ROOT / 'assets/art-direction-v2.json').read_text())
    cam = spec['camera']
    px = cam['pixels_per_world_unit']
    sy = cam['ground_y_scale']
    zpx = px * math.cos(math.radians(cam['elevation_degrees_proposal']))
    ox, oy = cam['field_origin_px']
    fw, fh = cam['field_size_u_proposal']

    def point(x, y, z=0):
        return f'{ox+x*px:.3f},{oy+y*px*sy-z*zpx:.3f}'

    # Work in logical ground units, then project the entire line layer once.
    save('arena-lines', 1920, 1080, f'''
<g transform="translate({ox} {oy}) scale({px} {px*sy})" fill="none" stroke="{PAPER}" stroke-width=".095" opacity=".88">
<path d="M0 6.75V0H32V6.75M32 11.25V18H0V11.25M16 0V18"/>
<circle cx="16" cy="9" r="2"/><circle cx="16" cy="9" r=".10" fill="{PAPER}"/>
<path d="M0 4.5H3V13.5H0M32 4.5H29V13.5H32" opacity=".55"/>
</g>''', '轻斜俯视地面标线；32u×18u 草案，55°正交投影，无球门插画')

    goals = []
    for front, back, color in [(0, -1.5, '#42D2C8'), (fw, fw+1.5, '#F4A945')]:
        a, b, h = 6.75, 11.25, 2.5
        # Far/back net, roof mesh, ground footprint, then raised front posts.
        goals.append(f'<polygon points="{point(back,a)} {point(front,a)} {point(front+.4,b+.35)} {point(back+.4,b+.35)}" fill="{INK}" opacity=".12"/>')
        goals.append(f'<polygon points="{point(back,a)} {point(back,b)} {point(back,b,h)} {point(back,a,h)}" fill="{color}" fill-opacity=".13" stroke="{INK}" stroke-width="3"/>')
        goals.append(f'<polygon points="{point(back,a,h)} {point(front,a,h)} {point(front,b,h)} {point(back,b,h)}" fill="{PAPER}" fill-opacity=".14"/>')
        net = []
        for step in range(1, 9):
            y = a+(b-a)*step/9
            net.append(f'M{point(back,y)}L{point(back,y,h)}M{point(back,y,h)}L{point(front,y,h)}')
        for step in range(1, 5):
            z = h*step/5
            net.append(f'M{point(back,a,z)}L{point(back,b,z)}')
        goals.append(f'<path d="{" ".join(net)}" fill="none" stroke="{PAPER}" stroke-width="2" opacity=".55"/>')
        frame = f'M{point(back,a)}L{point(back,a,h)}L{point(back,b,h)}L{point(back,b)}M{point(back,a,h)}L{point(front,a,h)}M{point(back,b,h)}L{point(front,b,h)}M{point(front,a)}L{point(front,a,h)}L{point(front,b,h)}L{point(front,b)}'
        for stroke, width in [(INK, 13), (PAPER, 8)]:
            goals.append(f'<path d="{frame}" fill="none" stroke="{stroke}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>')
        goals.append(f'<path d="M{point(front,a)}L{point(front,a,.65)}M{point(front,b)}L{point(front,b,.65)}" stroke="{color}" stroke-width="8" stroke-linecap="round"/>')
    save('arena-goals', 1920, 1080, ''.join(goals), '独立球门插画；门宽4.5u、深1.5u、高2.5u为视觉草案')
    save('shadow-soft', 128, 64, '''
<defs><radialGradient id="s"><stop offset="0" stop-color="#132C3C" stop-opacity=".34"/><stop offset=".55" stop-color="#132C3C" stop-opacity=".20"/><stop offset="1" stop-color="#132C3C" stop-opacity="0"/></radialGradient></defs>
<ellipse cx="64" cy="32" rx="58" ry="25" fill="url(#s)"/>''', '独立柔和脚底接触阴影')
    for name, color, body in [
        ('team-cyan-ground', '#42D2C8', '<ellipse cx="64" cy="56" rx="51" ry="41.8"/>'),
        ('team-amber-ground', '#F4A945', '<path d="M64 12 117 56 64 100 11 56Z"/>')
    ]:
        save(name, 128, 112, f'<g fill="none" stroke="{INK}" stroke-width="10" stroke-linejoin="round">{body}</g><g fill="none" stroke="{color}" stroke-width="5" stroke-linejoin="round">{body}</g>', '贴地队伍形状标记；独立于服装配色')
    save('ball', 64, 64, f'''
<defs><radialGradient id="ball" cx=".28" cy=".22" r=".8"><stop stop-color="#FFFFFF"/><stop offset=".55" stop-color="{PAPER}"/><stop offset="1" stop-color="#B5C9CF"/></radialGradient><clipPath id="sphere"><circle cx="32" cy="32" r="25"/></clipPath></defs>
<circle cx="32" cy="32" r="28" fill="{PAPER}" stroke="{INK}" stroke-width="3"/>
<circle cx="32" cy="32" r="25" fill="url(#ball)"/>
<g clip-path="url(#sphere)" fill="{INK}"><path d="M31 20 43 27 39 40 25 42 20 29ZM29 5 24 14 12 15 8 8M55 13 49 25 58 35 65 22M52 52 38 49 32 60 48 65M12 51 16 37 4 30 0 46"/>
<path d="M24 14 31 20M12 15 20 29M43 27 49 25M39 40 38 49M25 42 16 37" fill="none" stroke="{INK}" stroke-width="1.5"/>
</g><path d="M14 24C16 17 20 13 27 12" fill="none" stroke="#FFFFFF" stroke-width="4" stroke-linecap="round" opacity=".8"/>''', '暖白深蓝足球；上左高光与下右体积阴影')
    print('Created 6 v2 SVG layers')


if __name__ == '__main__':
    main()
