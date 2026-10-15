#!/usr/bin/env python3
"""Install or control this Mac's persistent game service and configure LAN clients."""
import argparse
import ipaddress
import json
import os
from pathlib import Path
import plistlib
import shutil
import signal
import socket
import subprocess
import tempfile
import time
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[1]
LABEL = 'com.skillsoccer.local-server'
DOMAIN = 'gui/' + str(os.getuid())
SERVICE = Path.home() / 'Library/Application Support/SkillSoccer/Server'
PLIST = Path.home() / 'Library/LaunchAgents' / (LABEL + '.plist')
MANIFEST = ROOT / 'server/.local/local-server.json'


def launch(*args, check=True):
    return subprocess.run(['/bin/launchctl', *args], check=check,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)


def lan_address():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as route:
        route.connect(('192.0.2.1', 80))  # Select the default route without sending packets.
        return route.getsockname()[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['install', 'status', 'stop', 'start', 'uninstall'])
    parser.add_argument('--host')
    parser.add_argument('--port', type=int, default=27862)
    parser.add_argument('--replace-pid', type=int, action='append', default=[])
    args = parser.parse_args()
    if os.uname().sysname != 'Darwin':
        raise SystemExit('This helper manages the current macOS host. See server/DEPLOY.md for other hosts.')
    if args.action in ['stop', 'uninstall']:
        launch('bootout', DOMAIN + '/' + LABEL, check=False)
        if args.action == 'uninstall':
            PLIST.unlink(missing_ok=True)
        print(json.dumps({'running': False, 'dataPreserved': True}))
        return
    if args.action == 'install':
        if not 1 <= args.port <= 65535:
            raise SystemExit('Invalid service port.')
        host = str(ipaddress.IPv4Address(args.host or lan_address()))
        endpoint = 'http://' + host + ':' + str(args.port)
        SERVICE.mkdir(parents=True, exist_ok=True, mode=0o700)
        data = SERVICE / 'data'
        data.mkdir(exist_ok=True, mode=0o700)
        releases = SERVICE / 'releases'
        releases.mkdir(exist_ok=True)
        release = releases / time.strftime('%Y%m%d-%H%M%S')
        with tempfile.TemporaryDirectory(prefix='soccer-host-publish-') as staging:
            subprocess.run([str(ROOT / 'tools/dotnet'), 'publish',
                            str(ROOT / 'server/Soccer.LoginServer'), '-c', 'Release',
                            '-p:UseAppHost=false', '-p:DebugType=None', '-o', staging, '--nologo'],
                           check=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            shutil.copytree(staging, release)
        # Preserve the previous local service's saves when migrating the first time.
        for name in ['accounts.json', 'social.json', 'chat.json']:
            previous = ROOT / 'server/Soccer.LoginServer/bin/Debug/net10.0/data' / name
            if previous.is_file() and not (data / name).exists():
                shutil.copy2(previous, data / name)
        for pid in args.replace_pid:
            command = subprocess.run(['ps', '-p', str(pid), '-o', 'command='],
                                     stdout=subprocess.PIPE, text=True).stdout
            if not command.strip():
                continue
            if str(ROOT / 'server/Soccer.LoginServer') not in command or ':' + str(args.port) not in command:
                raise SystemExit('Refusing to stop an unrelated process: ' + str(pid))
            try:
                os.kill(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
        launch('bootout', DOMAIN + '/' + LABEL, check=False)
        runtime = Path('/opt/homebrew/opt/dotnet/libexec/dotnet')
        if not runtime.is_file():
            runtime = Path(shutil.which('dotnet') or '')
        if not runtime.is_file():
            raise SystemExit('Install the .NET 10 runtime before starting the service.')
        logs = Path.home() / 'Library/Logs/SkillSoccer'
        logs.mkdir(parents=True, exist_ok=True)
        PLIST.parent.mkdir(parents=True, exist_ok=True)
        settings = {
            'Label': LABEL,
            'ProgramArguments': [str(runtime), str(release / 'Soccer.LoginServer.dll'),
                                 '--urls', 'http://0.0.0.0:' + str(args.port)],
            'WorkingDirectory': str(SERVICE),
            'EnvironmentVariables': {
                'DOTNET_ROOT': str(runtime.parent), 'ASPNETCORE_ENVIRONMENT': 'Production',
                'Accounts__DataPath': str(data / 'accounts.json'),
                'Social__DataPath': str(data / 'social.json'),
                'Chat__DataPath': str(data / 'chat.json'), 'Accounts__AllowDebug': 'false',
                'Logging__LogLevel__Default': 'Warning',
                'Logging__LogLevel__Microsoft.AspNetCore': 'Warning'},
            'RunAtLoad': True, 'KeepAlive': True, 'ThrottleInterval': 5,
            'ProcessType': 'Background',
            'StandardOutPath': str(logs / 'server.log'),
            'StandardErrorPath': str(logs / 'server.log')}
        PLIST.write_bytes(plistlib.dumps(settings))
        PLIST.chmod(0o600)
        # bootout can return before launchd finishes unregistering the previous job.
        # Retry only its transient EIO result; preserve other failures for diagnosis.
        for attempt in range(20):
            registered = launch('bootstrap', DOMAIN, str(PLIST), check=False)
            if registered.returncode == 0:
                break
            if registered.returncode != 5 or attempt == 19:
                raise subprocess.CalledProcessError(registered.returncode, registered.args,
                                                    registered.stdout, registered.stderr)
            time.sleep(.15)
        # Configuration travels with both desktop clients and remains editable after unpacking.
        config = ROOT / 'UnityProject/Assets/StreamingAssets/game-service.json'
        config.parent.mkdir(parents=True, exist_ok=True)
        config.write_text(json.dumps({'ServiceUrl': endpoint}, indent=2) + '\n')
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_text(json.dumps({'serviceUrl': endpoint, 'port': args.port,
                                       'data': str(data), 'release': str(release),
                                       'launchAgent': str(PLIST)}, indent=2) + '\n')
    elif args.action == 'start':
        current = launch('print', DOMAIN + '/' + LABEL, check=False)
        if current.returncode != 0:
            launch('bootstrap', DOMAIN, str(PLIST))
        else:
            launch('kickstart', DOMAIN + '/' + LABEL)
    if not MANIFEST.exists():
        raise SystemExit('Run install first.')
    manifest = json.loads(MANIFEST.read_text())
    running = False
    for attempt in range(100 if args.action != 'status' else 1):
        try:
            with urlopen('http://127.0.0.1:' + str(manifest['port']) + '/health', timeout=2) as response:
                health = json.loads(response.read())
                running = health.get('service') == 'SkillSoccer.Login'
                if running:
                    break
        except OSError:
            pass
        time.sleep(.1)
    print(json.dumps({**manifest, 'running': running,
                      'version': health.get('version') if running else None}, ensure_ascii=False, indent=2))
    if not running:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
