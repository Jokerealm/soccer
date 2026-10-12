#!/usr/bin/env python3
"""Validate actual asset files, transparency, licenses and gallery references."""
from pathlib import Path
from html.parser import HTMLParser
import hashlib
import json
import math
import urllib.parse
import xml.etree.ElementTree as ET
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]

def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream,'sha256').hexdigest()

class GalleryImages(HTMLParser):
    def __init__(self):
        super().__init__()
        self.sources=[]
    def handle_starttag(self,tag,attributes):
        attrs=dict(attributes)
        if tag=='img' and 'src' in attrs:
            self.sources.append(attrs['src'])
        elif tag=='image' and 'href' in attrs:
            self.sources.append(attrs['href'])

def main():
    spec=json.loads((ROOT/'assets/art-direction-v2.json').read_text())
    animation_path=ROOT/'assets/animation-manifest-v2.json'
    if not animation_path.is_file():
        animation_path=ROOT/'assets/animation-manifest-v1.json'
    animation_spec=json.loads(animation_path.read_text()) if animation_path.is_file() else None
    active_animation_paths={f['texture'] for c in animation_spec['characters'] for clip in c['clips'].values() for f in clip['frames']} if animation_spec else set()
    character_specs={Path(c['path']).stem:c for c in spec['characters']}
    records=[]
    errors=[]
    inspection=[]
    for path in sorted((ROOT/'assets/original').rglob('*')):
        if path.suffix not in ['.png','.svg']:
            continue
        relative=str(path.relative_to(ROOT))
        is_v2=path.stem.endswith('-v2')
        entry={'id':path.stem,'path':relative,'sha256':sha256(path),'bytes':path.stat().st_size,'status':'draft','source':'project-authored-svg' if path.suffix=='.svg' else 'built-in-image_gen','third_party_license':None,'art_version':2 if is_v2 else 1,'current_direction':is_v2}
        is_animation='animations' in path.parts
        if is_animation:
            entry.update(art_version=2,animation_version=2 if is_v2 else 1,current_direction=True,status='animation-keyframe-draft',animation_specification=str(animation_path.relative_to(ROOT)))
            entry['animation_usage']='active' if relative in active_animation_paths else 'previous-run-comparison'
        try:
            if path.suffix=='.png':
                with Image.open(path) as image:
                    image.load()
                    entry.update({'dimensions_px':list(image.size),'color_mode':image.mode})
                    row={'path':relative,'size':list(image.size),'mode':image.mode}
                    if is_animation:
                        if image.mode!='RGBA':
                            errors.append(f'Animation lacks RGBA: {relative}')
                        else:
                            extrema=image.getchannel('A').getextrema()
                            entry.update(transparent=extrema[0]==0,alpha_extrema=list(extrema))
                            row['alpha_extrema']=list(extrema)
                            if extrema!=(0,255):
                                errors.append(f'Unexpected animation alpha: {relative}')
                    elif 'characters' in path.parts:
                        entry['status']='static-character-concept-draft'
                        entry['orientation']=character_specs[path.stem]['orientation'] if path.stem in character_specs else 'high-angle-overhead; direction and camera not standardized'
                        entry['animation_complete']=False
                        if image.mode!='RGBA':
                            errors.append(f'Character lacks RGBA: {relative}')
                        else:
                            alpha=image.getchannel('A')
                            extrema=alpha.getextrema()
                            body=alpha.point(lambda n:255 if n>=8 else 0).getbbox()
                            row.update({'alpha_extrema':extrema,'raw_alpha_bbox':alpha.getbbox(),'body_bbox_alpha_ge_8':body})
                            entry.update({'transparent':extrema[0]==0,'body_bbox_alpha_ge_8':body})
                            if extrema[0]!=0 or extrema[1]!=255:
                                errors.append(f'Unexpected character alpha: {relative}')
                            if not body or body[0]<=0 or body[1]<=0 or body[2]>=image.width or body[3]>=image.height:
                                errors.append(f'Body touches canvas edge: {relative}')
                            if path.stem in character_specs:
                                char=character_specs[path.stem]
                                anchor=char['preview_foot_anchor_px']
                                if list(body or [])!=char['source_rect_alpha_ge_8']:
                                    errors.append(f'Character source rectangle differs from actual alpha: {relative}')
                                if body and not (body[0]<anchor[0]<body[2] and body[1]<anchor[1]<body[3]):
                                    errors.append(f'Preview foot anchor outside body bounds: {relative}')
                                entry.update({'preview_foot_anchor_px':anchor,'unity_pivot_normalized_proposal':[round(anchor[0]/image.width,6),round(1-anchor[1]/image.height,6)],'anchor_status':char['anchor_status'],'display_body_height_px_at_1920':char['display_body_height_px_at_1920']})
                                row['alpha_below_8_pixels']=sum(alpha.histogram()[1:8])
                    elif is_v2:
                        entry['status']='arena-background-concept-draft' if 'arenas' in path.parts else 'character-lineup-concept-draft'
                    inspection.append(row)
            else:
                svg=ET.parse(path).getroot()
                if svg.tag!='{http://www.w3.org/2000/svg}svg':
                    errors.append(f'Invalid SVG root: {relative}')
                entry['dimensions_px']=[int(svg.attrib['width']),int(svg.attrib['height'])]
                entry['view_box']=svg.attrib.get('viewBox')
                if is_v2:
                    entry['status']='editable-oblique-visual-layer-draft'
        except Exception as exc:
            errors.append(f'{relative}: {exc}')
        records.append(entry)
    counts={'png':sum(r['path'].endswith('.png') for r in records),'svg':sum(r['path'].endswith('.svg') for r in records)}
    # Validate declared deliveries, allowing later versioned assets to be added.
    index=json.loads((ROOT/'assets/vector-index.json').read_text())
    required_v1={Path(e['path']).stem for e in index}|{name+'-v1' for name in ['roster','standard-floor','wind-shuttle-idle','iron-guard-idle','arc-light-idle','arena-lines','ball','team-cyan-circle','team-amber-diamond','local-player','mode-ranked','mode-standard','mode-party']}
    required=required_v1|set(spec['required_v2_asset_ids'])
    actual={r['id'] for r in records}
    for missing in sorted(required-actual):
        errors.append(f'Missing declared asset: {missing}')
    cam=spec['camera']
    expected_scale=math.sin(math.radians(cam['elevation_degrees_proposal']))
    if not math.isclose(cam['ground_y_scale'],expected_scale,abs_tol=1e-8):
        errors.append('Camera elevation and ground y scale disagree')
    prompt_data=json.loads((ROOT/spec['generation_prompts']).read_text())
    if animation_spec:
        for prompt_set in animation_spec.get('generation_prompt_sets',[animation_spec['generation_prompts']]):
            prompt_data['outputs']+=json.loads((ROOT/prompt_set).read_text())['outputs']
    for output in prompt_data['outputs']:
        if not (ROOT/output['path']).is_file():
            errors.append(f'Missing generation output: {output["path"]}')
        for reference in output['references']:
            if not (ROOT/reference).is_file():
                errors.append(f'Missing generation reference: {reference}')
    third_party=[]
    for key in ['sports-pack','ui-pack','input-prompts','particle-pack']:
        folder=ROOT/'assets/third_party/kenney'/key
        license_path=ROOT/'assets/licenses'/f'kenney-{key}-license.txt'
        if not license_path.exists() or 'CC0' not in license_path.read_text(errors='replace'):
            errors.append(f'Missing or invalid CC0 license: {key}')
        third_party.append({'id':f'kenney-{key}','source_page':f'https://kenney.nl/assets/{key}','license':'CC0-1.0','local_directory':str(folder.relative_to(ROOT)),'license_file':str(license_path.relative_to(ROOT)),'files':sum(p.is_file() for p in folder.rglob('*')),'png_files':len(list(folder.rglob('*.png')))})
    font=ROOT/'assets/third_party/noto-sans-cjk/NotoSansCJKsc-Regular.otf'
    ofl=font.parent/'OFL.txt'
    if not font.exists() or not ofl.exists() or 'SIL OPEN FONT LICENSE' not in ofl.read_text():
        errors.append('Missing Noto font or OFL license')
    third_party.append({'id':'noto-sans-cjk-sc-regular','source_page':'https://github.com/notofonts/noto-cjk','path':str(font.relative_to(ROOT)),'license':'OFL-1.1','license_file':str(ofl.relative_to(ROOT)),'sha256':sha256(font)})
    gallery=ROOT/'previews/art-gallery.html'
    parser=GalleryImages()
    parser.feed(gallery.read_text())
    for source in parser.sources:
        target=(gallery.parent/urllib.parse.unquote(source)).resolve()
        if not target.is_relative_to(ROOT) or not target.is_file():
            errors.append(f'Missing gallery image: {source}')
    if '__SCENE_DATA__' in gallery.read_text() or '__CARDS__' in gallery.read_text():
        errors.append('Unresolved gallery template placeholder')
    followup=spec['known_followup']
    if animation_spec:
        followup=[note for note in followup if '目前只有新版总览' not in note]+animation_spec['known_followup']
        player=ROOT/'previews/animation-player.html'
        if not player.is_file() or '__ANIMATION_DATA__' in player.read_text():
            errors.append('Missing or unresolved animation player')
        for char in animation_spec['characters']:
            for clip in char['clips'].values():
                for frame in clip['frames']:
                    if not (ROOT/frame['texture']).is_file():
                        errors.append(f'Missing animation frame texture: {frame["texture"]}')
        animation_report=json.loads((ROOT/'reports/animation-validation.json').read_text())
        if animation_report['status']!='passed':
            errors.append('Animation geometry validation failed')
    manifest={'version':3 if animation_spec else 2,'date':spec['date'],'art_direction':spec['art_direction'],'scope':'static art plus six-character animation keyframe preparation; no gameplay implementation' if animation_spec else spec['scope'],'art_specification':'assets/art-direction-v2.json','original_assets':records,'third_party_sources':third_party,'generation_prompts':['assets/original/generation-prompts.json',spec['generation_prompts']]+(animation_spec.get('generation_prompt_sets',[animation_spec['generation_prompts']]) if animation_spec else []),'known_followup':followup}
    if animation_spec:
        manifest['animation_specification']=str(animation_path.relative_to(ROOT))
    report={'date':spec['date'],'status':'passed' if not errors else 'failed','original_counts':counts,'original_total':len(records),'current_v2_asset_count':sum(r['current_direction'] for r in records),'gallery_image_references':len(parser.sources),'third_party_sources':third_party,'errors':errors,'limitations':manifest['known_followup']}
    (ROOT/'assets/manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
    (ROOT/'reports/image-inspection.json').write_text(json.dumps(inspection,ensure_ascii=False,indent=2)+'\n')
    (ROOT/'reports/asset-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(report,ensure_ascii=False,indent=2))
    return 0 if not errors else 1

if __name__=='__main__':
    raise SystemExit(main())
