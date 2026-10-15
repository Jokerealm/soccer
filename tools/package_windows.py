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
    required = ['SkillSoccer.exe', 'TuanjiePlayer.dll', 'TuanjieCrashHandler64.exe', 'SkillSoccer_Data/StreamingAssets/game-service.json',
                'SkillSoccer_Data/globalgamemanagers', 'SkillSoccer_Data/Managed/Soccer.Core.dll',
                'SkillSoccer_Data/Managed/Soccer.Runtime.dll', 'SkillSoccer_Data/Plugins/x86_64/steam_api64.dll', 'steam_appid.txt', 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll']
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
        network_guide = root / 'docs/界面布阵与AI配合0.31.0.md'
        z.write(network_guide, package_name + '/' + network_guide.name)
        z.write(root / 'docs/聊天好友与组队匹配0.29.0.md', package_name + '/聊天好友与组队匹配0.29.0.md')
        steam_fix_guide = root / 'docs/Steam房间搜索修复0.26.1.md'
        z.write(steam_fix_guide, package_name + '/' + steam_fix_guide.name)
        z.write(root / 'UnityProject/Packages/com.rlabrecque.steamworks.net/LICENSE.md', package_name + '/Licenses/Steamworks.NET-MIT.md')
        z.write(root / 'server/README.md', package_name + '/登录服务说明.md')
        z.write(root / 'server/DEPLOY.md', package_name + '/游戏服务部署.md')
        z.write(root / 'assets/third_party/noto-sans-cjk/OFL.txt', package_name + '/Licenses/Noto-OFL.txt')
        for path in sorted((root / 'assets/licenses').glob('*.txt')):
            z.write(path, package_name + '/Licenses/' + path.name)
        z.writestr(package_name + '/开始试玩.txt',
                   '技能足球 ' + version + ' · Windows x64\r\n\r\n'
                   '完整解压后双击 SkillSoccer.exe。请保留所有同目录文件和文件夹。\r\n'
                   '无需安装 Git、团结或 .NET SDK。启动先 Steam 登录、联网登录或离线进入；离线可训练、AI 对练与局域网联机。\r\n'
                   '联机：联机对战 → Steam/局域网/互联网 → 创建/加入房间。最多6人，机器人由房主手动添加。\r\n'
                   'Steam测试：两端Steam登录不同帐号，首次登录设置1–6字游戏名，可在个人主页修改；联机页默认浏览Steam房间。测试AppID为480。\r\n'
                   '登录服务地址由部署配置提供，可创建独立游戏帐号；a/a、b/b 仅用于开启调试帐号的本地开发服务。\r\n'
                   '帐号密码与房间密码分别填写；局域网不要求登录。\r\n'
                   '进入房间前无需选队，进房后可切换青队或橙队；房主与玩家全员准备后，倒计时3秒进入选人布阵。\r\n'
                   '建房自动选择端口；局域网输入房间号、从列表加入或输入房主IP；互联网输入6位房间号。\r\n'
                   'Steam列表显示房主名字、人数和密码锁；加入房间按钮打开房间号弹窗，有密码房间先输入密码。建议双方使用本次新版。\r\n'
                   '选人时两队轮流锁定角色与开局位置；每队六个固定点位，队友不能占同一点；30秒超时自动选择，暂不ban，允许重复角色。\r\n'
                   '好友分Steam和游戏好友；Enter队内聊天、Shift+Enter全场，Esc收起。\r\n'
                   '已预设当前Mac服务器：192.168.31.29:27862，同一局域网可注册帐号、使用游戏好友、聊天和组队匹配。公网尚未配置。\r\n'
                   '开场在各自六边形内右键热身；AI难度为球童、球员、球神、作弊者。\r\n'
                   '滚轮缩放，Ctrl+滚轮旋转；按住中键或Alt选择信号、松开发送。小地图点击观察；Ctrl+1–9编队、双击数字观察，反引号键回到自己。\r\n'
                   '开始游戏 → 新手教程：13步实际练习，可跳过、回看和重练。\r\n'
                   '设置 → 按键：点击当前键位后按新键，自动保存；Esc取消，恢复默认可重置。\r\n'
                   '首页方向键上下选择，Enter只打开聊天，鼠标点击进入菜单；商城显示金币，正式对局胜利+100金币。\r\n'
                   '金币按离线与帐号分别保存在当前设备，暂不云同步；英雄与皮肤购买后续开放。\r\n'
                   '首页 → 个人主页：累计与各角色战绩；战绩详情查看最近50场。录像回放后续开放。\r\n'
                   '首页右上角 → 好友：联网登录后按帐号查找、申请、接受/拒绝、移除和查看好友主页。\r\n'
                   '选角时点击各张技能卡，分别展开或收起消耗、冷却和详细说明。\r\n'
                   '选角：左键拖动环绕模型，滚轮缩放，可切换自动旋转与复位。\r\n'
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
