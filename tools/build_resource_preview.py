#!/usr/bin/env python3
"""Build the offline art gallery; archived Godot scene generation is opt-in."""
from pathlib import Path
import argparse
import html
import json

ROOT = Path(__file__).resolve().parents[1]
CHARACTERS = [
    ("wind-shuttle", "风梭", "快速接应", (396,227,488,774), .065),
    ("iron-guard", "铁卫", "身体防守", (215,246,825,773), .06),
    ("arc-light", "弧光", "远射与曲线球", (354,152,549,932), .06),
]

def scene():
    ext = [('FontFile','assets/third_party/noto-sans-cjk/NotoSansCJKsc-Regular.otf','font'),
           ('Texture2D','assets/original/arenas/standard-floor-v1.png','floor'),
           ('Texture2D','assets/original/vector/arena-lines-v1.svg','lines'),
           ('Texture2D','assets/original/vector/ball-v1.svg','ball'),
           ('Texture2D','assets/original/vector/team-cyan-circle-v1.svg','cyan'),
           ('Texture2D','assets/original/vector/team-amber-diamond-v1.svg','amber')]
    for identifier,*_ in CHARACTERS:
        ext.append(('Texture2D',f'assets/original/characters/{identifier}-idle-v1.png',identifier))
        for key in ('passive','q','e'):
            ext.append(('Texture2D',f'assets/original/vector/{identifier}-{key}-v1.svg',f'{identifier}-{key}'))
    lines=[f'[gd_scene load_steps={len(ext)+4} format=3]', '']
    lines.extend(f'[ext_resource type="{kind}" path="res://{path}" id="{identifier}"]' for kind,path,identifier in ext)
    for identifier,_,_,rect,_ in CHARACTERS:
        atlas_id = 'atlas_' + identifier.replace('-', '_')
        lines.extend(['',f'[sub_resource type="AtlasTexture" id="{atlas_id}"]',f'atlas = ExtResource("{identifier}")',f'region = Rect2({", ".join(map(str,rect))})'])
    lines.extend(['','[node name="ArtResourcePreview" type="Control"]','layout_mode = 3','anchors_preset = 15','anchor_right = 1.0','anchor_bottom = 1.0','grow_horizontal = 2','grow_vertical = 2'])
    def label(name,text,x,y,size=20):
        lines.extend(['',f'[node name="{name}" type="Label" parent="."]','layout_mode = 0',f'offset_left = {x}',f'offset_top = {y}',f'offset_right = {x+1160}',f'offset_bottom = {y+48}', 'theme_override_colors/font_color = Color(0.96, 0.94, 0.89, 1)', 'theme_override_fonts/font = ExtResource("font")',f'theme_override_font_sizes/font_size = {size}',f'text = "{text}"'])
    def texture(name,resource,x,y,w,h):
        lines.extend(['',f'[node name="{name}" type="TextureRect" parent="."]','layout_mode = 0',f'offset_left = {x}',f'offset_top = {y}',f'offset_right = {x+w}',f'offset_bottom = {y+h}',f'texture = ExtResource("{resource}")','expand_mode = 1','stretch_mode = 0'])
    label('Title','技能足球 / 第一批美术资源',60,18,30)
    label('Subtitle','静态资源预览 · 无游戏逻辑 · 角色倾角与方向尚待统一',60,60,17)
    texture('Floor','floor',60,106,1160,652.5)
    texture('FieldMarkings','lines',60,106,1160,652.5)
    texture('Ball','ball',630,423,20,20)
    for i,(identifier,_,_,_,scale) in enumerate(CHARACTERS):
        for team,x in [('cyan',380),('amber',900)]:
            y=285+i*150
            texture(f'Team_{team}_{i}',team,x-22,y-8,44,44)
            atlas_id = 'atlas_' + identifier.replace('-', '_')
            lines.extend(['',f'[node name="Sprite_{team}_{i}" type="Sprite2D" parent="."]',f'position = Vector2({x}, {y})',f'scale = Vector2({scale}, {scale})',f'texture = SubResource("{atlas_id}")'])
    for i,(identifier,title,role,_,_) in enumerate(CHARACTERS):
        x=60+i*395
        label(f'CharacterLabel{i}',f'{title} · {role}',x,775,18)
        for j,key in enumerate(['passive','q','e']):
            texture(f'Icon{i}{key}',f'{identifier}-{key}',x+j*68,815,56,56)
    destination = ROOT/'previews/godot-legacy'
    destination.mkdir(parents=True, exist_ok=True)
    (destination/'resource_preview.tscn').write_text('\n'.join(lines)+'\n')

def gallery():
    spec = json.loads((ROOT/'assets/art-direction-v2.json').read_text())
    index = json.loads((ROOT/'assets/vector-index.json').read_text())
    legacy = {identifier: rect for identifier, _, _, rect, _ in CHARACTERS}
    cards = []
    versions = {'v1': {'camera': {'origin': [192,108], 'yScale': 1}, 'characters': []},
                'v2': {'camera': {'origin': spec['camera']['field_origin_px'], 'yScale': spec['camera']['ground_y_scale']}, 'characters': []}}
    for char in spec['characters']:
        identifier, title, role = char['id'], char['name'], char['role']
        x1,y1,x2,y2 = char['source_rect_alpha_ge_8']
        source = '../'+char['path']
        def sprite(height=None):
            sizing = f'height="{height}" width="{height*(x2-x1)/(y2-y1):.1f}"' if height else ''
            return f'<svg {sizing} viewBox="{x1} {y1} {x2-x1} {y2-y1}" role="img" aria-label="{title}主体预览"><image href="{source}" width="1254" height="1254"/></svg>'
        cards.append(f'<article class="sprite-card"><div class="sprite-stage">{sprite()}</div><h3>{title}</h3><p>{role}</p><div class="tiny"><figure>{sprite(64)}<figcaption>主体高 64px</figcaption></figure><figure>{sprite(96)}<figcaption>主体高 96px</figcaption></figure></div><a href="{source}">打开透明原图 ↗</a></article>')
        versions['v2']['characters'].append({'id':identifier,'path':source,'rect':[x1,y1,x2,y2],'pivot':char['preview_foot_anchor_px'],'bodyHeight':char['display_body_height_px_at_1920'],'radius':char['collision_radius_u_proposal']})
        x,y,w,h = legacy[identifier]
        versions['v1']['characters'].append({'id':identifier,'path':f'../assets/original/characters/{identifier}-idle-v1.png','rect':[x,y,x+w,y+h],'pivot':[x+w/2,y+h/2],'bodyHeight':70 if identifier=='iron-guard' else 64,'radius':char['collision_radius_u_proposal']})
    for version in ['v1','v2']:
        versions[version]['layers'] = {key:f'../assets/original/{path}-{version}.svg' for key,path in [('lines','vector/arena-lines'),('ball','vector/ball')]}
        versions[version]['layers']['floor'] = f'../assets/original/arenas/standard-floor-{version}.png'
    versions['v1']['layers'].update({'cyan':'../assets/original/vector/team-cyan-circle-v1.svg','amber':'../assets/original/vector/team-amber-diamond-v1.svg'})
    versions['v2']['layers'].update({key:f'../assets/original/vector/{path}-v2.svg' for key,path in [('cyan','team-cyan-ground'),('amber','team-amber-ground'),('shadow','shadow-soft'),('goals','arena-goals')]})
    versions['localMarker'] = '../assets/original/vector/local-player-v1.svg'
    paths = {versions['localMarker']}
    for version in ['v1','v2']:
        paths.update(versions[version]['layers'].values())
        paths.update(char['path'] for char in versions[version]['characters'])
    def figure(path, label):
        return f'<figure><a href="{path}"><img src="{path}" alt="{html.escape(label)}"></a><figcaption>{html.escape(label)}</figcaption></figure>'
    icons = ''.join(figure('../'+entry['path'],entry['label']) for entry in index)
    markers = ''.join(figure(f'../assets/original/vector/{name}-v1.svg',label) for name,label in [('local-player','本地玩家'),('mode-ranked','排位'),('mode-standard','普通'),('mode-party','娱乐')])
    layers = ''.join(figure(f'../assets/original/vector/{name}-v2.svg',label) for name,label in [('arena-lines','斜视地面线'),('arena-goals','独立立体球门'),('ball','有体积的足球'),('shadow-soft','柔和脚底阴影'),('team-cyan-ground','青色贴地圆环'),('team-amber-ground','琥珀贴地菱形')])
    template = (ROOT/'tools/templates/art-gallery.html').read_text()
    # Template lives in tools/templates; generated gallery lives one level below root.
    template = template.replace('../../assets/third_party/', '../assets/third_party/')
    replacements = {'__CARDS__':''.join(cards),'__ICONS__':icons,'__MARKERS__':markers,'__LAYERS__':layers,
                    '__PRELOAD__':''.join(f'<img src="{path}" alt="">' for path in sorted(paths)),
                    '__SCENE_DATA__':json.dumps(versions,ensure_ascii=False).replace('<','\\u003c')}
    for key,value in replacements.items():
        template = template.replace(key,value)
    (ROOT/'previews/art-gallery.html').write_text(template)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--legacy-godot', action='store_true')
    args = parser.parse_args()
    if args.legacy_godot:
        scene()
    gallery()
    print('Built offline HTML gallery' + (' and archived Godot scene' if args.legacy_godot else ''))
