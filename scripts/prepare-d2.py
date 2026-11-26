#!/usr/bin/env python3
"""Fetch a pinned official D2 CLI for build machines, never for end users."""
import argparse
import hashlib
import io
from pathlib import Path
import tarfile
import urllib.request

VERSION = '0.9.0'
ARCHIVES = {
    'osx-arm64': ('macos-arm64', 'eaf6c0c143e56dd9fa97bfb6df25ea9c1ebce40245f056a0768cf1a6c15d3064'),
    'win-x64': ('windows-amd64', '5f63b643de8f5a6dfb922d172e1b5496e4caf47497c33c4427cf1127f28c340f'),
}
ROOT = Path(__file__).resolve().parent.parent


def prepare(runtime, archive=None):
    platform, expected = ARCHIVES[runtime]
    url = f'https://github.com/d2lang/d2/releases/download/v{VERSION}/d2-v{VERSION}-{platform}.tar.gz'
    data = Path(archive).read_bytes() if archive else urllib.request.urlopen(url, timeout=60).read()
    if hashlib.sha256(data).hexdigest() != expected:
        raise RuntimeError('D2 archive SHA-256 mismatch')
    name = 'd2.exe' if runtime.startswith('win-') else 'd2'
    dest = ROOT / 'tools/d2/runtime' / runtime
    dest.mkdir(parents=True, exist_ok=True)
    licenses = ROOT / 'tools/d2/licenses'
    licenses.mkdir(parents=True, exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(data), mode='r:gz') as package:
        for member, target in [(f'bin/{name}', dest / name), ('LICENSE.txt', licenses / 'LICENSE.txt'),
                               ('THIRD_PARTY_NOTICES.txt', licenses / 'THIRD_PARTY_NOTICES.txt')]:
            content = package.extractfile(f'd2-v{VERSION}/{member}')
            if content is None:
                raise RuntimeError('Missing D2 package member: ' + member)
            target.write_bytes(content.read())
    (dest / name).chmod(0o755)
    (licenses / 'SOURCE.txt').write_text(
        f'D2 v{VERSION} (unmodified official executable)\n'
        f'Source: https://github.com/d2lang/d2/tree/v{VERSION}\n'
        f'Source archive: https://github.com/d2lang/d2/archive/refs/tags/v{VERSION}.tar.gz\n'
        'License: Mozilla Public License 2.0; see LICENSE.txt.\n'
        'Dependency notices: THIRD_PARTY_NOTICES.txt and D2-fonts-NOTICE.txt.\n'
        'Official binary archive SHA-256:\n' +
        ''.join(f'{key}: {value[1]}\n' for key, value in ARCHIVES.items()), encoding='utf-8')
    print(f'D2 v{VERSION} prepared for {runtime}: {dest / name}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('runtime', choices=ARCHIVES)
    parser.add_argument('--archive', help='Use a downloaded official archive (SHA-256 is still verified)')
    opts = parser.parse_args()
    prepare(opts.runtime, opts.archive)
