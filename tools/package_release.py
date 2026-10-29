"""Package only the built plugin and its copied runtime dependencies."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

root = Path(__file__).resolve().parents[1]

def package(tag, changelog=None):
    if not re.fullmatch(r'v\d+\.\d+\.\d+\.\d+', tag):
        raise ValueError('A four-part version tag is required')
    build = root / 'Stylist/bin/Release'
    manifest = json.loads((build / 'Stylist.json').read_text(encoding='utf-8-sig'))
    assert manifest['InternalName'] == 'Stylist'
    assert manifest['AssemblyVersion'] == tag[1:]
    assert manifest['DalamudApiLevel'] == 15
    assert manifest['RepoUrl'] == 'https://github.com/kaldigo/Stylist'
    manifest['Changelog'] = changelog or 'Sort gearset items into the armory chest and unused armory gear into inventory after updating, using the existing move-replaced-items setting. In-game acceptance testing remains pending.'
    output = root / 'artifacts'
    output.mkdir(exist_ok=True)
    archive = output / 'Stylist.zip'
    dependencies = ['Stylist.dll', 'Stylist.deps.json', 'ECommons.dll', 'NightmareUI.dll']
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for name in dependencies:
            z.write(build / name, name)
        z.writestr('Stylist.json', json.dumps(manifest, indent=2) + '\n')
        z.write(root / 'assets/icon.png', 'images/icon.png')
        z.write(root / 'ECommons/LICENSE.md', 'licenses/ECommons.txt')
        z.write(root / 'NightmareUI/LICENSE', 'licenses/NightmareUI.txt')
        z.write(root / 'FORK.md', 'FORK.md')
    url = f'https://github.com/kaldigo/Stylist/releases/download/{tag}/Stylist.zip'
    manifest.update(DownloadLinkInstall=url, DownloadLinkUpdate=url, DownloadLinkTesting=url)
    feed = json.dumps([manifest], indent=2) + '\n'
    (output / 'pluginmaster.json').write_text(feed, encoding='utf-8')
    (root / 'pluginmaster.json').write_text(feed, encoding='utf-8')
    (output / 'SHA256SUMS.txt').write_text(f'{hashlib.sha256(archive.read_bytes()).hexdigest()}  Stylist.zip\n', encoding='utf-8')

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--tag', required=True)
    parser.add_argument('--changelog-file')
    args = parser.parse_args()
    package(args.tag, Path(args.changelog_file).read_text(encoding='utf-8').strip() if args.changelog_file else None)
