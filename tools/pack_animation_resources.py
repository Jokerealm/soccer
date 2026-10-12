#!/usr/bin/env python3
"""Bundle active animation sources, metadata and the self-contained local player."""
from pathlib import Path
import json
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    version = 2 if (ROOT/'assets/animation-manifest-v2.json').exists() else 1
    manifest = f'assets/animation-manifest-v{version}.json'
    spec = json.loads((ROOT/manifest).read_text())
    paths = {f['texture'] for c in spec['characters'] for clip in c['clips'].values() for f in clip['frames']}
    active_sources = len(paths)
    comparison_paths = {f['texture'] for clip in spec.get('comparison_runs', {}).values() for f in clip['frames']}
    paths.update(comparison_paths)
    animation_sources = len(paths)
    paths.update([
        manifest, *spec.get('generation_prompt_sets', [spec['generation_prompts']]),
        'assets/art-direction-v2.json', 'docs/动画资源准备.md',
        'previews/animation-player.html',
        'assets/original/arenas/standard-floor-v2.png', 'assets/original/vector/ball-v2.svg',
        'assets/third_party/noto-sans-cjk/NotoSansCJKsc-Regular.otf',
        'assets/third_party/noto-sans-cjk/OFL.txt',
        'reports/animation-validation.json', 'reports/animation-preview-validation.json',
    ])
    destination = ROOT/f'assets/packages/soccer-animation-keyframes-v{version}.zip'
    destination.parent.mkdir(parents=True, exist_ok=True)
    run_note = (f'跑步 v2 为每人 14 帧，默认 18 fps，一轮约 0.78 秒；转身和踢球各 8 帧。\n'
                f'另保留 {len(comparison_paths)} 张上一版跑步 PNG、48 个对比帧，播放器可切换 8／14 帧、行进／原地检查。\n'
                '14 帧跑步源图为 1254×1254 RGBA，四列四行，最后两格空白；读取逐帧 source_rect_px。'
                if version == 2 else '跑步、转身、踢球各 8 帧，源图为 1774×887 RGBA。')
    readme = f'''# 技能足球 · 六角动画关键帧资源 v{version}

{active_sources} 张当前透明源 PNG，六人各有跑步／转身／踢球，共 18 套动作、{spec['active_frame_count']} 个选用帧。
请按 {manifest} 切帧；不要机械等分图集。
磁核左向补画由逐帧 texture 与 source_scale_to_clip 自动关联。
跑步与踢球目前只有右前 SE，当前为动画草稿，步态、脚点与细节仍需精修。
{run_note}

播放器：从解压目录启动本地静态 HTTP 服务，然后打开 previews/animation-player.html。
例如在已安装 Python 的机器上运行：
python3 -m http.server 8765 --bind 127.0.0.1
再访问 http://127.0.0.1:8765/previews/animation-player.html

生产整理、坐标字段、帧率与击球时序见 docs/动画资源准备.md。
PNG 使用内置 image_gen；完整提示词包含主仓库中的参考路径，参考图与未选用尝试不在本资源包中。
资源包包含演示用球场／足球及 Noto 中文字体，字体许可见 assets/third_party/noto-sans-cjk/OFL.txt。
未包含 Unity / 团结玩法工程；球的位置与击球时序只是美术演示。
'''
    with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(paths):
            if path == 'previews/animation-player.html':
                player = (ROOT/path).read_text().replace('href="art-gallery.html">← 美术图库', 'href="../README.md">← 资源包说明')
                archive.writestr(path, player)
            else:
                archive.write(ROOT/path, path)
        archive.writestr('README.md', readme)
    with zipfile.ZipFile(destination) as archive:
        failure = archive.testzip()
        if failure:
            raise ValueError(f'Invalid archive member: {failure}')
    report = {'archive': str(destination.relative_to(ROOT)), 'animation_version': version,
              'animation_pngs': animation_sources, 'active_animation_pngs': active_sources,
              'comparison_animation_pngs': len(comparison_paths), 'active_frames': spec['active_frame_count'],
              'files': len(paths)+1, 'bytes': destination.stat().st_size, 'crc_validation': 'passed'}
    (ROOT/'reports/animation-package-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
