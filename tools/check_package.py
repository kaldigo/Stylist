"""Validate a candidate package without advertising it in the source feed."""
import hashlib
import json
import struct
import zipfile
from package_release import root, package

feed = root / 'pluginmaster.json'
original = feed.read_bytes()
try:
    version = json.loads((root / 'Stylist/bin/Release/Stylist.json').read_text(encoding='utf-8-sig'))['AssemblyVersion']
    package('v' + version)
    archive = root / 'artifacts/Stylist.zip'
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == (root / 'artifacts/SHA256SUMS.txt').read_text().split()[0]
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        assert {'Stylist.dll', 'Stylist.json', 'ECommons.dll', 'NightmareUI.dll', 'images/icon.png', 'licenses/ECommons.txt', 'licenses/NightmareUI.txt'} <= set(z.namelist())
        assert json.loads(z.read('Stylist.json'))['AssemblyVersion'] == version
        icon = z.read('images/icon.png')
        assert icon == (root / 'assets/icon.png').read_bytes()
        assert struct.unpack('>II', icon[16:24]) == (512, 512)
        assert json.loads(z.read('Stylist.json'))['IconUrl'] == 'https://raw.githubusercontent.com/kaldigo/Stylist/main/assets/icon.png'
    print('Package, runtime dependencies, identity and checksum verified.')
finally:
    feed.write_bytes(original)
