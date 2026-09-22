"""Package one built pet directory, excluding diagnostic data and local settings."""
import hashlib
import json
import sys
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

build = Path(sys.argv[1]).resolve()
destination = build.parent / (build.name + '-portable.zip')
files = [build/'SilverWolfPet.exe', build/'README.md']
default = build/'assets/default'
manifest = default/'pet.json'
spec = json.loads(manifest.read_text(encoding='utf-8-sig'))
files += [build/'assets/silver-wolf.ico', manifest, default/spec['image']]
files += [default/atlas['file'] for atlas in spec.get('atlases', {}).values()]
mode = build/'assets/codex-mode'
files += [mode/name for name in ('cross-original-action.png', 'exit-portal.png',
                               'invincible-clean-foot.png', 'ultimate-cutin-transparent.png')]
files += [mode/'work-states'/f'{state}-frame-{frame}.png'
          for state in ('thinking','executing','waiting-input','failed','completed')
          for frame in (1,2)]
files = list(dict.fromkeys(files))
if not all(path.is_file() for path in files):
    raise SystemExit('Build executable, README and all runtime assets before packaging')
with ZipFile(destination, 'w', ZIP_DEFLATED, compresslevel=3) as archive:
    for path in files:
        archive.write(path, Path(build.name)/path.relative_to(build))
with ZipFile(destination) as archive:
    if archive.testzip() is not None:
        raise SystemExit('Archive integrity check failed')
checksum = hashlib.sha256(destination.read_bytes()).hexdigest()
destination.with_suffix('.zip.sha256').write_text(checksum+'  '+destination.name+'\n', encoding='utf-8')
print(f'{destination}\nFiles: {len(files)}\nSHA-256: {checksum}')
