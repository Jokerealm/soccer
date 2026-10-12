#!/usr/bin/env python3
"""Report Unity/Tuanjie readiness without opening an editor or signing in."""
from datetime import datetime
from pathlib import Path
import json
import os
import platform
import plistlib
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
CODE = Path('/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code')
REQUIRED_EXTENSIONS = ('visualstudiotoolsforunity.vstuc', 'ms-dotnettools.csharp', 'ms-dotnettools.csdevkit')
ENV = dict(os.environ, PATH='/opt/homebrew/bin:/usr/local/bin:' + os.environ.get('PATH', ''))


def command(argv):
    try:
        result = subprocess.run(list(map(str, argv)), env=ENV, cwd=ROOT, capture_output=True, text=True, timeout=30)
        return {'available': result.returncode == 0, 'exit_code': result.returncode,
                'output': (result.stdout + result.stderr).strip()}
    except (OSError, subprocess.TimeoutExpired) as exc:
        return {'available': False, 'error': str(exc)}


def app_info(path):
    path = Path(path)
    if not path.is_dir():
        return {'available': False, 'path': str(path)}
    try:
        with (path / 'Contents/Info.plist').open('rb') as stream:
            info = plistlib.load(stream)
        return {'available': True, 'path': str(path),
                'version': info.get('CFBundleShortVersionString', info.get('CFBundleVersion')),
                'bundle_id': info.get('CFBundleIdentifier')}
    except (OSError, plistlib.InvalidFileException) as exc:
        return {'available': True, 'path': str(path), 'metadata_error': str(exc)}


def is_editor(path):
    return any((path / 'Contents/MacOS' / name).is_file() for name in ('Unity', 'Tuanjie'))


def editors():
    apps = set()
    for base in (Path('/Applications'), Path.home() / 'Applications'):
        if not base.is_dir():
            continue
        for path in base.glob('*.app'):
            if is_editor(path):
                apps.add(path)
        for folder in ('Unity', 'Tuanjie', 'Tuanjie Hub'):
            root = base / folder
            if root.is_dir():
                apps.update(p for p in root.rglob('*.app') if is_editor(p))
    if os.environ.get('SOCCER_UNITY_EDITOR'):
        path = Path(os.environ['SOCCER_UNITY_EDITOR']).expanduser()
        if path.name in ('Unity', 'Tuanjie') and path.parent.name == 'MacOS':
            path = path.parents[2]
        if is_editor(path):
            apps.add(path)
    found = []
    for app in sorted(apps):
        entry = app_info(app)
        entry['distribution'] = 'Tuanjie' if (app / 'Contents/MacOS/Tuanjie').is_file() else 'Unity'
        # Tuanjie Hub places add-on modules beside Tuanjie.app; built-in Mac
        # support remains inside Contents. Unity installations can use either.
        locations = [app / 'Contents/PlaybackEngines', app.parent / 'PlaybackEngines']
        module_paths = [p for location in locations for p in location.glob('*') if p.is_dir()]
        entry['platform_modules'] = sorted({p.name for p in module_paths})
        entry['platform_module_paths'] = [str(p) for p in module_paths]
        entry['windows_build_support_detected'] = any(
            (variation / 'WindowsPlayer.exe').is_file()
            for module in module_paths if module.name == 'WindowsStandaloneSupport'
            for variation in (module / 'Variations').glob('win64_player_*'))
        found.append(entry)
    return found


def main():
    git = command([ROOT / 'tools/git', '--version'])
    lfs = command([ROOT / 'tools/git', 'lfs', 'version'])
    lfs_local = command([ROOT / 'tools/git', 'config', '--local', '--get', 'filter.lfs.process'])
    lfs['configured_for_repository'] = lfs_local.get('output') == 'git-lfs filter-process'
    sdk = command([ROOT / 'tools/dotnet', '--version'])
    sdk['installed_sdks'] = command([ROOT / 'tools/dotnet', '--list-sdks']).get('output', '')
    extensions_result = command([CODE, '--list-extensions', '--show-versions'])
    installed = {}
    for line in extensions_result.get('output', '').splitlines():
        if '@' in line:
            name, version = line.rsplit('@', 1)
            installed[name.lower()] = version
    required_extensions = {name: {'available': name in installed, 'version': installed.get(name)} for name in REQUIRED_EXTENSIONS}
    found = editors()
    tuanjie = [editor for editor in found if editor['distribution'] == 'Tuanjie']
    windows_ready = any(editor['windows_build_support_detected'] for editor in tuanjie)
    validation_path = ROOT / 'reports/tuanjie-environment-validation.json'
    validation = json.loads(validation_path.read_text()) if validation_path.exists() else {}
    validated_current_editor = validation.get('status') == 'passed' and any(
        validation.get('editor_path') == str(Path(editor['path']) / 'Contents/MacOS/Tuanjie')
        for editor in tuanjie)
    project = ROOT / 'UnityProject'
    try:
        import PIL
        pillow = {'available': True, 'version': PIL.__version__}
    except ImportError:
        pillow = {'available': False}
    clt = command(['/usr/bin/xcode-select', '-p'])
    tools_ready = all([git['available'], lfs['available'], lfs['configured_for_repository'], sdk['available'],
                       CODE.is_file(), *[item['available'] for item in required_extensions.values()]])
    report = {
        'checked_at': datetime.now().astimezone().isoformat(timespec='seconds'),
        'engine_route': 'Tuanjie Editor (Unity China) + C#; distribution confirmed',
        'host': {'system': platform.system(), 'machine': platform.machine(), 'macos': platform.mac_ver()[0]},
        'git': git, 'git_lfs': lfs, 'dotnet_sdk': sdk,
        'vscode': app_info('/Applications/Visual Studio Code.app'),
        'vscode_extensions': required_extensions,
        'dotnet_acquisition_extension': {'available': 'ms-dotnettools.vscode-dotnet-runtime' in installed,
                                       'version': installed.get('ms-dotnettools.vscode-dotnet-runtime')},
        'tuanjie_cowork': app_info('/Applications/Tuanjie Cowork.app'),
        'engine_editors': found,
        'editor_detection_note': 'Default application locations plus SOCCER_UNITY_EDITOR; detection does not validate licensing or launch/build compatibility.',
        'unity_project': {'path': str(project), 'created': (project / 'ProjectSettings/ProjectVersion.txt').is_file()},
        'xcode_command_line_tools': clt,
        'full_xcode': app_info('/Applications/Xcode.app'),
        'steam': app_info('/Applications/Steam.app'),
        'krita': next((app_info(p) for p in (Path('/Applications/krita.app'), Path('/Applications/Krita.app'),
                                           Path.home() / 'Applications/krita.app') if p.is_dir()), {'available': False}),
        'python': {'available': True, 'version': sys.version.split()[0]}, 'pillow': pillow,
        'readiness': {'csharp_git_tools_ready': tools_ready, 'engine_editor_detected': bool(found),
                      'tuanjie_editor_detected': bool(tuanjie), 'tuanjie_windows_build_support_detected': windows_ready,
                      'pc_development_smoke_passed': validated_current_editor,
                      'full_environment_ready': tools_ready and bool(tuanjie) and windows_ready},
        'build_verification': {'status': 'passed' if validated_current_editor else 'not verified',
                               'report': 'reports/tuanjie-environment-validation.json',
                               'scope': 'Temporary project: engine startup, C# compile, PNG sprite import and Windows x64 Mono build',
                               'windows_native_execution': 'not tested on this Mac',
                               'vscode_debugger_and_production_project': 'not yet configured or verified'},
        'manual_next_steps': (['Install an Apple Silicon Tuanjie Editor and activate its license.'] if not tuanjie else [])
                             + ([] if windows_ready else ['Add Windows Build Support (Mono) to the selected Tuanjie Editor in Tuanjie Hub.']),
    }
    destination = ROOT / 'reports/environment-check.json'
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report['readiness']['full_environment_ready'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
