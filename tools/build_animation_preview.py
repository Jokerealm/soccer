#!/usr/bin/env python3
"""Index generated sprite pixels without repainting, then build an offline player."""
from pathlib import Path
import argparse
import hashlib
import json
import statistics

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
CHARACTERS = [
    ('wind-shuttle', '风梭', '灵巧接应', '#42d2c8', 108),
    ('iron-guard', '铁卫', '厚重防守', '#f4a945', 120),
    ('arc-light', '弧光', '精准射门', '#ad77d7', 114),
    ('echo', '回响', '传球组织', '#75d7cd', 110),
    ('magnet-core', '磁核', '低重心干扰', '#dfb45f', 118),
    ('jump-point', '跃点', '轻快轮转', '#f38676', 106),
]
DIRECTIONS = ['S', 'SW', 'W', 'NW', 'N', 'NE', 'E', 'SE']
PHASES = {
    'run': ['触地 A', '下压 A', '换步 A', '腾空 A', '触地 B', '下压 B', '换步 B', '腾空 B'],
    'turn': ['正面', '左前', '左侧', '左后', '背面', '右后', '右侧', '右前'],
    'kick': ['准备', '移重心', '后摆', '蓄势', '击球', '随挥', '回收', '复位'],
}
RUN_PHASES_14 = [f'{phase} {leg}' for leg in ['A','B'] for phase in ['触地','下压','支撑','过身','蹬地','腾空','前伸']]


def components(mask):
    """8-connected scanline union; only used to find source rectangles."""
    parent = []
    spans = []
    previous = []

    def find(k):
        while parent[k] != k:
            parent[k] = parent[parent[k]]
            k = parent[k]
        return k

    for y, row in enumerate(mask):
        edges = np.diff(np.pad(row.astype(np.int8), (1, 1)))
        starts, ends = np.where(edges == 1)[0], np.where(edges == -1)[0]
        current = []
        for left, right in zip(starts, ends):
            label = len(parent)
            parent.append(label)
            for pl, pr, pk in previous:
                if pr >= left and pl <= right:
                    parent[find(pk)] = find(label)
            current.append((int(left), int(right), label))
            spans.append((int(left), int(right), y, label))
        previous = current
    boxes = {}
    for left, right, y, label in spans:
        key = find(label)
        if key not in boxes:
            boxes[key] = [left, y, right, y + 1, 0]
        b = boxes[key]
        b[0], b[1] = min(b[0], left), min(b[1], y)
        b[2], b[3] = max(b[2], right), max(b[3], y + 1)
        b[4] += right - left
    return list(boxes.values())


def inspect(path, columns=4, rows=2, expected_count=None):
    image = Image.open(ROOT / path)
    if image.mode != 'RGBA':
        raise ValueError(f'{path}: RGBA required')
    alpha = np.asarray(image.getchannel('A'))
    parts = components(alpha >= 8)
    bodies = [b for b in parts if b[4] > 3000]
    expected_count = expected_count or columns * rows
    if len(bodies) != expected_count:
        raise ValueError(f'{path}: expected {expected_count} isolated bodies, found {len(bodies)}')
    # Assign by actual body center; cell lines are only a sorting hint.
    bodies.sort(key=lambda b: (int((b[1] + b[3]) / 2 / image.height * rows), (b[0] + b[2]) / 2))
    # Small disconnected outline fragments join the nearest body metadata bounds.
    for part in parts:
        if part[4] > 3000:
            continue
        cx, cy = (part[0] + part[2]) / 2, (part[1] + part[3]) / 2
        distances = [max(b[0]-cx, 0, cx-b[2])**2 + max(b[1]-cy, 0, cy-b[3])**2 for b in bodies]
        idx = min(range(len(bodies)), key=distances.__getitem__)
        if distances[idx] < 25**2:
            b = bodies[idx]
            b[:4] = [min(b[0], part[0]), min(b[1], part[1]), max(b[2], part[2]), max(b[3], part[3])]
    return image, alpha, bodies


def frames_for(path, action, columns=4, rows=2, expected_count=None, phases=None):
    image, alpha, bodies = inspect(path, columns, rows, expected_count)
    row_height = image.height / rows
    contact_indices = ([0,1,7,8] if len(bodies)==14 else [0,1,4,5]) if action=='run' else list(range(len(bodies)))
    foot_offsets = [bodies[i][3] - (i // columns) * row_height for i in contact_indices]
    ground_offset = statistics.median(foot_offsets) - 5
    frames = []
    for i, b in enumerate(bodies):
        x0, y0, x1, y1 = b[:4]
        # Torso centroid avoids extended arms/boots shifting the entire figure.
        torso_top, torso_bottom = round(y0 + (y1-y0)*.36), round(y0 + (y1-y0)*.55)
        _, xs = np.where(alpha[torso_top:torso_bottom, x0:x1] >= 128)
        pivot_x = float(x0 + np.median(xs))
        # Generated rows have slightly different layout offsets. Estimate each
        # 14-frame row's ground separately, preserving the flight lift within it.
        pivot_y = (max(body[3] for j,body in enumerate(bodies) if j//columns==i//columns)-5
                   if len(bodies)==14 else (i//columns)*row_height+ground_offset)
        # Rect includes the estimated ground anchor, even in flight poses.
        left, top = max(0, x0-3), max(0, y0-3)
        right, bottom = min(image.width, x1+3), min(image.height, int(max(y1+3, pivot_y+3)))
        rect = [left, top, right-left, bottom-top]
        pivot = [round(pivot_x-left, 2), round(pivot_y-top, 2)]
        frames.append({
            'index': i, 'name': f'{action}_{i:02d}', 'phase': (phases or PHASES[action])[i] if columns == 4 else '补画方向',
            'texture': path, 'texture_size_px': list(image.size),
            'source_rect_px': rect, 'body_bounds_px': [x0, y0, x1, y1],
            'pivot_px': pivot,
            'pivot_normalized_bottom_left': [round(pivot[0]/rect[2], 6), round(1-pivot[1]/rect[3], 6)],
            'unity_rect_px': [left, image.height-bottom, rect[2], rect[3]],
            'source_scale_to_clip': 1,
        })
        if action == 'kick' and i == 4:
            ys, xs = np.where(alpha[round(y0+(y1-y0)*.55):y1, x0:x1] >= 128)
            toe_x = int(xs.max())
            toe_y = float(np.median(ys[xs >= toe_x-5])) + round(y0+(y1-y0)*.55)
            frames[-1]['contact_anchor_px'] = [x0+toe_x-left, round(toe_y-top, 2)]
            frames[-1]['contact_anchor_status'] = 'estimated leading shoe tip for visual demo only'
    return frames, statistics.median(b[3]-b[1] for b in bodies), image


def main(version=2):
    errors, records, characters = [], [], []
    for identifier, name, role, accent, display_height in CHARACTERS:
        clips = {}
        for action in PHASES:
            revision=2 if version==2 and action=='run' else 1
            path = f'assets/original/animations/{identifier}-{action}-v{revision}.png'
            if revision==2:
                frames,nominal_height,image=frames_for(path,action,4,4,14,RUN_PHASES_14)
            else:
                frames, nominal_height, image = frames_for(path, action)
            if identifier == 'magnet-core' and action == 'turn':
                patch_path = 'assets/original/animations/magnet-core-turn-left-v1.png'
                patches, patch_height, _ = frames_for(patch_path, action, 2, 1)
                for target, patch in zip([1, 3], patches):
                    patch.update(index=target, name=f'turn_{target:02d}', phase=PHASES[action][target],
                                 source_scale_to_clip=round(nominal_height/patch_height, 6))
                    frames[target] = patch
            for i, frame in enumerate(frames):
                frame['direction'] = DIRECTIONS[i] if action == 'turn' else 'SE'
            fps = {'run': 18 if revision==2 else 10, 'turn': 6, 'kick': 10}[action]
            clips[action] = {'id': f'{identifier}/{action}', 'revision':revision,'frame_count': len(frames), 'fps_proposal': fps,
                             'loop': action != 'kick', 'root_motion': False,
                             'direction': '8 drawn views' if action == 'turn' else 'SE',
                             'nominal_body_height_px': nominal_height, 'frames': frames,
                             'events': [{'name': 'kick_contact_preview', 'frame_index': 4, 'time_seconds_at_default_fps': .4,
                                         'status': 'visual timing proposal; no gameplay/physics authority'}] if action == 'kick' else []}
            records.append({'character': identifier, 'action': action, 'source': path,
                            'texture_size_px': list(image.size), 'rgba': image.mode == 'RGBA',
                            'alpha_extrema': list(image.getchannel('A').getextrema()),
                            'isolated_bodies': len(frames), 'indexed_frames': len(frames),
                            'sha256': hashlib.sha256((ROOT/path).read_bytes()).hexdigest()})
        characters.append({'id': identifier, 'name': name, 'role': role, 'accent': accent,
                           'display_body_height_px_proposal': display_height, 'clips': clips})
    spec = {
        'version': version, 'date': '2026-10-08', 'status': 'animation-keyframe-draft',
        'art_direction_reference': 'assets/art-direction-v2.json',
        'scope': f'six characters; {14 if version==2 else 8}-frame southeast run; 8-frame kick and drawn turn views; local playback only',
        'coordinate_system': 'source_rect_px = [x,y,width,height], top-left origin; pivot_px relative to source rect',
        'scale_rule': 'one scale per character/clip; multiply source_scale_to_clip for replacement directions; never fit each frame separately',
        'pivot_status': 'automatic torso centroid and shared row ground estimate for preview; production foot locking pending',
        'turn_direction_order': DIRECTIONS,
        'contact_frame_index': 4, 'frame_index_base': 0,
        'generation_prompts': f'assets/original/generation-prompts-animations-v{version}.json',
        'generation_prompt_sets': ['assets/original/generation-prompts-animations-v1.json']+(['assets/original/generation-prompts-animations-v2.json'] if version==2 else []),
        'source_png_count': 19, 'active_frame_count': 180 if version==2 else 144,
        'characters': characters,
        'known_followup': [
            '跑步和踢球目前只有右前 SE 一个绘制方向；其他移动方向尚待补齐',
            '动作关键帧有服装、比例、摆臂和步态漂移，需逐帧修型与中间帧补充',
            '转身包含真实正侧背视图，但相机倾角与方向间透视仍需统一',
            '弧光的非对称射门护具、磁核补画与原图的细节需人工核对',
            '预览脚点为估算，最终脚部接触、根位置、击球点和时序需引擎中标定',
            '尚无持球跑、抢断、受击、庆祝、技能动作或最终 Animator 绑定',
        ],
    }
    if version==2:
        old_spec=json.loads((ROOT/'assets/animation-manifest-v1.json').read_text())
        spec['comparison_runs']={char['id']:char['clips']['run'] for char in old_spec['characters']}
        spec['comparison_frame_count']=48
        spec['comparison_source_png_count']=6
        spec['preview_motion']={'status':'visual demonstration; not gameplay rules','stride_body_heights_proposal':.85,'motion_direction':'right-forward','default':'travel','note':'Travel speed follows fps/frame_count; sprites retain in-place root motion'}
    # Validate source bounds and actual pixels for all active frames, including patches.
    seen = set()
    for char in characters:
        for clip in char['clips'].values():
            for f in clip['frames']:
                with Image.open(ROOT/f['texture']) as im:
                    x, y, w, h = f['source_rect_px']
                    if x < 0 or y < 0 or x+w > im.width or y+h > im.height:
                        errors.append(f'Out-of-bounds frame: {char["id"]}/{f["name"]}')
                    if not im.getchannel('A').crop((x,y,x+w,y+h)).getbbox():
                        errors.append(f'Empty frame: {char["id"]}/{f["name"]}')
                    if not all(0 <= n <= 1 for n in f['pivot_normalized_bottom_left']):
                        errors.append(f'Pivot outside frame: {char["id"]}/{f["name"]}')
                    if f['source_scale_to_clip'] <= 0:
                        errors.append('Invalid frame scale')
                key = (f['texture'], tuple(f['source_rect_px']))
                if key in seen:
                    errors.append(f'Duplicate active frame rectangle: {key}')
                seen.add(key)
    (ROOT/f'assets/animation-manifest-v{version}.json').write_text(json.dumps(spec, ensure_ascii=False, indent=2)+'\n')
    template = (ROOT/'tools/templates/animation-player.html').read_text()
    player = template.replace('__ANIMATION_DATA__', json.dumps(spec, ensure_ascii=False).replace('<', '\\u003c'))
    for key,value in {'__TOTAL_FRAMES__':spec['active_frame_count'],'__INITIAL_FPS__':characters[0]['clips']['run']['fps_proposal'],
                      '__MANIFEST_PATH__':f'assets/animation-manifest-v{version}.json','__PROMPT_PATH__':spec['generation_prompts']}.items():
        player=player.replace(key,str(value))
    (ROOT/'previews/animation-player.html').write_text(player)
    report = {'date': spec['date'], 'status': 'passed' if not errors else 'failed',
              'source_png_count': 19, 'characters': 6, 'clips': 18, 'active_frames': len(seen),
              'checks': ['RGBA/alpha', 'isolated full-body rectangles', 'frame bounds', 'non-empty frames', 'pivot ranges', 'unique active frames'],
              'animation_version':version,'comparison_frames':48 if version==2 else 0,
              'records': records, 'errors': errors, 'visual_status': 'keyframe draft; not production-ready',
              'limitations': spec['known_followup']}
    (ROOT/'reports/animation-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ['records','limitations']}, ensure_ascii=False, indent=2))
    return 0 if not errors else 1


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version',type=int,choices=[1,2],default=2)
    raise SystemExit(main(parser.parse_args().version))
