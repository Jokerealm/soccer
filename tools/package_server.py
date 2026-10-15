#!/usr/bin/env python3
"""Publish the portable ASP.NET service, excluding local data and credentials."""
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile


def main():
    root = Path(__file__).resolve().parents[1]
    version = re.search(r'^  bundleVersion: (\S+)$',
                        (root / 'UnityProject/ProjectSettings/ProjectSettings.asset').read_text(),
                        re.MULTILINE).group(1)
    folder = 'SkillSoccer-' + version + '-Server'
    archive = root / 'dist' / (folder + '.zip')
    archive.parent.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='soccer-server-publish-') as temporary:
        output = Path(temporary)
        subprocess.run([str(root / 'tools/dotnet'), 'publish',
                        str(root / 'server/Soccer.LoginServer'), '-c', 'Release',
                        '-p:UseAppHost=false', '-p:DebugType=None', '-p:DebugSymbols=false',
                        '-o', str(output), '--nologo'], check=True, stdout=subprocess.PIPE,
                       stderr=subprocess.STDOUT)
        # The portable service needs only the published assemblies and runtime manifests.
        files = sorted(p for p in output.iterdir()
                       if p.is_file() and (p.suffix == '.dll' or
                                          p.name.endswith(('.deps.json', '.runtimeconfig.json'))))
        if not (output / 'Soccer.LoginServer.dll').is_file() or len(files) < 3:
            raise SystemExit('Published service is incomplete.')
        hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
        manifest = {'version': version, 'runtime': 'ASP.NET Core Runtime 10',
                    'portable': True, 'containsCredentialsOrPlayerData': False, 'sha256': hashes}
        with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as package:
            for path in files:
                package.write(path, folder + '/' + path.name)
            package.write(root / 'server/DEPLOY.md', folder + '/部署说明.md')
            package.writestr(folder + '/BUILD.json', json.dumps(manifest, indent=2) + '\n')
    with zipfile.ZipFile(archive) as package:
        if package.testzip() is not None:
            raise SystemExit('Server ZIP verification failed.')
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    checksum = archive.with_suffix('.zip.sha256')
    checksum.write_text(digest + '  ' + archive.name + '\n', encoding='ascii')
    print(json.dumps({'archive': str(archive), 'bytes': archive.stat().st_size,
                      'sha256': digest, 'checksum': str(checksum),
                      'files': len(files), 'zipVerified': True}, indent=2))


if __name__ == '__main__':
    main()
