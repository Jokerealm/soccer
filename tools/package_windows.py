#!/usr/bin/env python3
"""Package a successful Windows build, with checksums and player instructions."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, default=root / 'dist')
    args = parser.parse_args()
    settings = (root / 'UnityProject/ProjectSettings/ProjectSettings.asset').read_text()
    version = re.search(r'^  bundleVersion: (\S+)$', settings, re.MULTILINE).group(1)
    build = root / 'UnityProject/Builds/Windows'
    report = json.loads((root / 'reports/project-build-windows.json').read_text())
    if report['result'] != 'Succeeded' or report['errors'] or report['target'] != 'StandaloneWindows64':
        raise SystemExit('A successful Windows x64 build is required before packaging.')
    required = ['SkillSoccer.exe', 'TuanjiePlayer.dll', 'TuanjieCrashHandler64.exe',
                'SkillSoccer_Data/globalgamemanagers', 'SkillSoccer_Data/Managed/Soccer.Core.dll',
                'SkillSoccer_Data/Managed/Soccer.Runtime.dll', 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll']
    for name in required:
        if not (build / name).is_file():
            raise SystemExit('Missing player file: ' + name)
    if (build / 'SkillSoccer_Data/Managed/Soccer.Core.dll').stat().st_mtime > (root / 'reports/project-build-windows.json').stat().st_mtime:
        raise SystemExit('Build report is older than the compiled game. Rebuild before packaging.')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    package_name = 'SkillSoccer-' + version + '-Windows-x64'
    archive = args.output_dir / (package_name + '.zip')
    files = sorted(p for p in build.rglob('*') if p.is_file())
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for path in files:
            z.write(path, package_name + '/' + path.relative_to(build).as_posix())
        z.write(root / 'docs/Windows试玩与开发.md', package_name + '/Windows试玩说明.md')
        network_guide = root / 'docs/局域网与互联网联机0.17.0.md'
        if network_guide.is_file():
            z.write(network_guide, package_name + '/' + network_guide.name)
        account_guide = root / 'docs/调试帐号与Windows联机验证0.17.1.md'
        if account_guide.is_file():
            z.write(account_guide, package_name + '/' + account_guide.name)
        usability_guide = root / 'docs/联机发现与操作优化0.18.0.md'
        if usability_guide.is_file():
            z.write(usability_guide, package_name + '/' + usability_guide.name)
        z.write(root / 'server/README.md', package_name + '/登录服务说明.md')
        z.write(root / 'assets/third_party/noto-sans-cjk/OFL.txt', package_name + '/Licenses/Noto-OFL.txt')
        for path in sorted((root / 'assets/licenses').glob('*.txt')):
            z.write(path, package_name + '/Licenses/' + path.name)
        z.writestr(package_name + '/开始试玩.txt',
                   '技能足球 ' + version + ' · Windows x64\r\n\r\n'
                   '完整解压后双击 SkillSoccer.exe。请保留所有同目录文件和文件夹。\r\n'
                   '无需安装 Git、团结或 .NET SDK。启动先联网登录或离线进入；离线可训练、AI 对练与局域网联机。\r\n'
                   '联机：普通模式 → 局域网/互联网 → 创建/加入房间。最多6人，空位由AI补齐。\r\n'
                   '启动页联网登录需要登录服务地址；调试帐号：帐号 a / 密码 a，或帐号 b / 密码 b。\r\n'
                   '帐号密码与房间密码分别填写；局域网不要求登录。\r\n'
                   '客人选择角色和队伍后准备，由房主开始；互联网建房可选择启用密码。\r\n'
                   '局域网支持自动发现；互联网需房主可访问的公网地址和房间TCP端口。\r\n'
                   '联机Esc只打开本机菜单，比赛继续。雷神 E 后左键确认方向；C 取消。配置步骤见随包 0.18 更新说明。\r\n'
                   '右键走位/追击（不显示路径）；V 蓄力踢球；左键确认技能目标；空格推击；Q/E 技能；Esc 暂停。\r\n'
                   '训练 R 重置，F/T 持球，F1 角度，F2 角球，F3 任意球。\r\n'
                   '详细步骤见 Windows试玩说明.md。\r\n'
                   '仓库：https://github.com/Jokerealm/soccer\r\n')
        z.writestr(package_name + '/BUILD.json', json.dumps({
            'version': version, 'target': report['target'], 'editor': report['editor'],
            'result': report['result'], 'errors': report['errors'], 'warnings': report['warnings'],
            'windowsRuntimeVerified': False,
            'files': len(files)
        }, indent=2) + '\n')
    with zipfile.ZipFile(archive) as z:
        bad = z.testzip()
        if bad:
            raise SystemExit('Corrupt ZIP entry: ' + bad)
        if len(z.namelist()) != len(set(z.namelist())):
            raise SystemExit('Duplicate ZIP entries found.')
    digest = hashlib.sha256()
    with archive.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    checksum = archive.with_suffix('.zip.sha256')
    checksum.write_text(digest.hexdigest() + '  ' + archive.name + '\n', encoding='ascii')
    print(json.dumps({'archive': str(archive), 'bytes': archive.stat().st_size,
                      'sha256': digest.hexdigest(), 'checksum': str(checksum),
                      'playerFiles': len(files), 'zipVerified': True}, indent=2))


if __name__ == '__main__':
    main()
