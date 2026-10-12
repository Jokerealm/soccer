#!/usr/bin/env python3
"""Author editable SVG UI assets and exact, non-physical arena markings."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "assets/original/vector"
INK, PAPER = "#132C3C", "#F7F0E1"

ICONS = [
    ("wind-shuttle-passive", "风梭·接力", "#42D2C8", '<path d="M30 42h30m-8-8 8 8-8 8M66 66H36m8-8-8 8 8 8"/>'),
    ("wind-shuttle-q", "风梭·侧滑", "#42D2C8", '<path d="m35 28 18 20-18 20m18-40 18 20-18 20M23 36h9M23 60h9"/>'),
    ("wind-shuttle-e", "风梭·顺风带", "#42D2C8", '<path d="M23 68c21 0 12-38 49-38m-12-8 12 8-7 12M25 52c14 0 18-13 26-20"/>'),
    ("iron-guard-passive", "铁卫·稳重", "#F4A945", '<path d="m30 28 18-6 18 6v21c0 13-18 23-18 23S30 62 30 49ZM40 47l8 8 9-15"/>'),
    ("iron-guard-q", "铁卫·护板", "#F4A945", '<path d="M27 27h42v17H27ZM30 56h36M36 65h24M48 44v22"/>'),
    ("iron-guard-e", "铁卫·扎根", "#F4A945", '<path d="M48 25v43M33 37h30M28 53c2 13 12 17 20 17s18-4 20-17m-45 6 5-6 7 3m26 0 7-3 5 6"/><circle cx="48" cy="25" r="6"/>'),
    ("arc-light-passive", "弧光·远距脚法", "#AD77D7", '<path d="M24 61h15l10-12 11 5 10-15M61 34l9 5-2 10M28 34h14M24 43h10"/>'),
    ("arc-light-q", "弧光·弧线准备", "#AD77D7", '<path d="M24 68c-1-39 31-51 47-30m-1-11 1 11-12-1"/><circle cx="26" cy="67" r="5"/>'),
    ("arc-light-e", "弧光·重炮准备", "#AD77D7", '<path d="m51 22-20 28h17l-4 24 22-31H49ZM22 33h9M19 46h8M23 61h7"/>'),
    ("echo-passive", "回响·流畅触球", "#75D6DF", '<circle cx="48" cy="48" r="9"/><path d="M31 28a27 27 0 0 1 34 37M24 40a25 25 0 0 0 32 30"/>'),
    ("echo-q", "回响·引导传球", "#75D6DF", '<circle cx="27" cy="60" r="7"/><circle cx="68" cy="30" r="7"/><path d="m37 53 21-15m-11 0h11v11M30 31h9M57 65h9"/>'),
    ("echo-e", "回响·接应护罩", "#75D6DF", '<path d="m48 22 23 9v19c0 13-23 24-23 24S25 63 25 50V31Z"/><circle cx="48" cy="41" r="6"/><path d="M38 61c0-14 20-14 20 0"/>'),
    ("magnet-core-passive", "磁核·稳定底盘", "#D5BA68", '<path d="M29 33h38l6 25H23ZM29 58v12h38V58M38 42h20M38 49h20"/>'),
    ("magnet-core-q", "磁核·缓速场", "#D5BA68", '<path d="M30 25v27a18 18 0 0 0 36 0V25M30 37h9M57 37h9"/><ellipse cx="48" cy="69" rx="27" ry="8"/>'),
    ("magnet-core-e", "磁核·排斥波", "#D5BA68", '<circle cx="48" cy="48" r="9"/><path d="M48 31V21m-6 7 6-7 6 7M65 48h10m-7-6 7 6-7 6M48 65v10m-6-7 6 7 6-7M31 48H21m7-6-7 6 7 6"/>'),
    ("jump-point-passive", "跃点·回追", "#F38B76", '<path d="M68 59a24 24 0 1 0-38 6M68 59V44M68 59H53m-20 6 15-15M32 36h9"/>'),
    ("jump-point-q", "跃点·截击", "#F38B76", '<path d="m28 25 18 10-10 20 24 4-4 12H25l9-19-14-9M52 29l12 3 7-12"/><circle cx="68" cy="43" r="7"/>'),
    ("jump-point-e", "跃点·影步", "#F38B76", '<path d="M23 58h18M29 45h17M36 32h14m6-6 16 22-16 22M50 58h8"/>'),
]

def icon_svg(body, color, title):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 96 96">
<title>{title}</title><rect x="5" y="5" width="86" height="86" rx="23" fill="{INK}" stroke="{color}" stroke-width="4"/>
<g fill="none" stroke="{PAPER}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round">{body}</g></svg>\n'''

def main():
    DEST.mkdir(parents=True, exist_ok=True)
    records = []
    for identifier, title, color, body in ICONS:
        (DEST / f"{identifier}-v1.svg").write_text(icon_svg(body, color, title))
        records.append({"id": identifier, "label": title, "path": f"assets/original/vector/{identifier}-v1.svg", "status": "draft-icon", "source": "project-authored-svg"})
    (DEST / "arena-lines-v1.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080" viewBox="0 0 1920 1080">
<title>32u × 18u 球场视觉标线，48px/u；球门宽4.5u，深1.5u</title>
<g fill="none" stroke="#F7F0E1" stroke-width="5" stroke-linejoin="round" opacity=".8">
<path d="M192 432V108H1728V432M1728 648V972H192V648M960 108V972"/>
<circle cx="960" cy="540" r="96"/><circle cx="960" cy="540" r="6" fill="#F7F0E1"/>
<path d="M192 324H336V756H192M1728 324H1584V756H1728" opacity=".5"/>
</g><path d="M192 432H120V648H192" fill="#42D2C8" fill-opacity=".09" stroke="#42D2C8" stroke-width="6"/>
<path d="M1728 432H1800V648H1728" fill="#F4A945" fill-opacity=".09" stroke="#F4A945" stroke-width="6"/>
</svg>\n''')
    (DEST / "ball-v1.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
<circle cx="32" cy="32" r="26" fill="#F7F0E1" stroke="#132C3C" stroke-width="4"/>
<path d="m32 20 11 8-4 13H25l-4-13ZM32 6l-7 9H15l-4-4M55 15l-5 11 8 8M51 52l-13-2-5 8M13 50l3-13-10-5" fill="#132C3C"/>
<path d="M25 15 32 20M15 15l6 13M43 28l7-2M39 41l-1 9M25 41l-9-4" stroke="#132C3C" stroke-width="2" fill="none"/>
</svg>\n''')
    (DEST / "team-cyan-circle-v1.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 96 96"><circle cx="48" cy="48" r="38" fill="none" stroke="#132C3C" stroke-width="10"/><circle cx="48" cy="48" r="38" fill="none" stroke="#42D2C8" stroke-width="5"/></svg>\n''')
    (DEST / "team-amber-diamond-v1.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 96 96"><path d="m48 7 41 41-41 41L7 48Z" fill="none" stroke="#132C3C" stroke-width="10" stroke-linejoin="round"/><path d="m48 7 41 41-41 41L7 48Z" fill="none" stroke="#F4A945" stroke-width="5" stroke-linejoin="round"/></svg>\n''')
    (DEST / "local-player-v1.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" width="48" height="32" viewBox="0 0 48 32"><path d="m10 6 14 20L38 6Z" fill="#F7F0E1" stroke="#132C3C" stroke-width="4" stroke-linejoin="round"/></svg>\n''')
    for identifier, title, color, body in [
        ("mode-ranked", "排位", "#F4A945", '<path d="M34 25h28v22c0 19-28 19-28 0ZM34 31H23v8c0 11 11 14 14 14M62 31h11v8c0 11-11 14-14 14M48 62v9M36 72h24"/>'),
        ("mode-standard", "普通", "#42D2C8", '<circle cx="48" cy="48" r="24"/><path d="m48 32 15 11-6 17H39l-6-17ZM48 24v8M25 41l8 2M71 41l-8 2M33 68l6-8M63 68l-6-8"/>'),
        ("mode-party", "娱乐", "#AD77D7", '<path d="m29 66 8-29 22 22ZM37 37l22 22M57 22v9M72 37h-9M66 26l-6 6M26 29l5 5M65 70l5 5"/>')
    ]:
        (DEST / f"{identifier}-v1.svg").write_text(icon_svg(body, color, title))
    (ROOT / "assets/vector-index.json").write_text(json.dumps(records, ensure_ascii=False, indent=2)+"\n")
    (ROOT / "assets/arena-geometry.json").write_text(json.dumps({
        "status": "archived-v1-design-proposal-for-visual-preview-only", "design_decision": "D01 direction now oblique; this geometry is the archived v1 overhead preview; D02/D03/D07 remain pending",
        "canvas": [1920,1080], "pixels_per_unit": 48, "field_origin_px": [192,108],
        "field_size_u": [32,18], "goal_width_u": 4.5, "goal_depth_u": 1.5,
        "goal_y_px": [432,648], "physics_source": False,
        "note": "标线坐标供资源预览；生成背景不提供碰撞或进球边界，装饰与逻辑分离。"
    }, ensure_ascii=False, indent=2)+"\n")
    print(f"Created {len(list(DEST.glob('*.svg')))} editable SVG assets")

if __name__ == "__main__":
    main()
