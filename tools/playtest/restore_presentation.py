#!/usr/bin/env python3
"""Restore only reviewed art bytes from hash-pinned owned archives; never import pack code."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import tarfile
import tempfile
import urllib.request
import zipfile

MAX_SELECTED = 250 * 1024 * 1024
MAX_ARCHIVE = 1024 * 1024 * 1024


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def relative(value):
    p = PurePosixPath(value)
    if not value or '\\' in value or p.is_absolute() or ':' in value or any(s in ('', '.', '..') for s in value.split('/')):
        raise ValueError('Unsafe selected path: ' + value)
    return p


def checked(root, name):
    p = root.joinpath(*relative(name).parts)
    if any(root.joinpath(*p.relative_to(root).parts[:i]).is_symlink() for i in range(1, len(p.relative_to(root).parts) + 1)):
        raise ValueError('Art paths cannot contain symlinks.')
    return p


def verify(path, row):
    return path.is_file() and path.stat().st_size == row['size'] and digest(path) == row['sha256']


def restore(manifest, cache, archive_cache, library, offline=False):
    rows = manifest['files']
    if not rows or len(rows) > 200 or sum(r['size'] for r in rows) > MAX_SELECTED:
        raise ValueError('Selected art exceeds the reviewed bound.')
    if len({r['relative_path'] for r in rows}) != len(rows):
        raise ValueError('Duplicate selected output.')
    for row in rows:
        path = relative(row['relative_path'])
        relative(row['member'])
        if path.suffix.lower() not in ('.fbx', '.png', '.anim') or not isinstance(row['size'], int) or not 0 < row['size'] <= MAX_SELECTED or not re.fullmatch('[0-9a-f]{64}', row['sha256']):
            raise ValueError('Only bounded, hash-pinned reviewed art may be restored.')
    archive_ids = set()
    for archive in manifest['archives']:
        relative(archive['name'])
        if archive['id'] in archive_ids or archive['format'] not in ('zip', 'unitypackage', 'raw') or not isinstance(archive['file_id'], int) or archive['file_id'] < 1 or not isinstance(archive['size'], int) or not 0 < archive['size'] <= MAX_ARCHIVE or not re.fullmatch('[0-9a-f]{64}', archive['sha256']):
            raise ValueError('Invalid purchased archive pin.')
        archive_ids.add(archive['id'])
    if any(row['archive'] not in archive_ids for row in rows):
        raise ValueError('Selected art has no pinned original archive.')
    for root in (cache, archive_cache):
        if root.is_symlink():
            raise ValueError('Private cache cannot be a symlink.')
        root.mkdir(parents=True, exist_ok=True)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    for archive in manifest['archives']:
        selected = [r for r in rows if r['archive'] == archive['id']]
        missing = []
        for row in selected:
            target = checked(cache, row['relative_path'])
            if target.exists() and not verify(target, row):
                raise ValueError('Preserve differing private art: ' + str(target))
            if not target.exists():
                missing.append(row)
        if not missing:
            continue
        original = checked(archive_cache, archive['name'])
        if original.exists() and not verify(original, archive):
            raise ValueError('Private archive differs from pin: ' + archive['name'])
        if not original.exists():
            if offline:
                raise FileNotFoundError('Restore purchased art first: ' + archive['name'])
            if not 0 < archive['size'] <= MAX_ARCHIVE:
                raise ValueError('Archive exceeds the reviewed download bound.')
            fd, temp_name = tempfile.mkstemp(dir=archive_cache, prefix='.frontier-')
            temp = Path(temp_name)
            try:
                with os.fdopen(fd, 'wb') as output, opener.open(library.rstrip('/') + '/raw/' + str(archive['file_id']), timeout=300) as response:
                    count = 0
                    for block in iter(lambda: response.read(1024 * 1024), b''):
                        count += len(block)
                        if count > archive['size']:
                            raise ValueError('Archive exceeds its exact byte count.')
                        output.write(block)
                if not verify(temp, archive):
                    raise ValueError('Purchased archive hash mismatch.')
                temp.replace(original)
            finally:
                temp.unlink(missing_ok=True)
        if archive['format'] == 'raw':
            for row in missing:
                if row['sha256'] != archive['sha256'] or row['size'] != archive['size']:
                    raise ValueError('Direct source differs from selected file.')
                target = checked(cache, row['relative_path'])
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(original, target)
            continue
        with (zipfile.ZipFile(original) if archive['format'] == 'zip' else tarfile.open(original, 'r:gz')) as source:
            for row in missing:
                member = relative(row['member']).as_posix()
                if archive['format'] == 'zip':
                    info = source.getinfo(member)
                    if info.file_size != row['size'] or info.is_dir():
                        raise ValueError('ZIP selection differs from pin.')
                    stream = source.open(info)
                else:
                    info = source.getmember(member)
                    if not info.isfile() or info.size != row['size']:
                        raise ValueError('Unity selection differs from pin.')
                    stream = source.extractfile(info)
                target = checked(cache, row['relative_path'])
                target.parent.mkdir(parents=True, exist_ok=True)
                fd, temp_name = tempfile.mkstemp(dir=target.parent, prefix='.frontier-')
                temp = Path(temp_name)
                try:
                    with os.fdopen(fd, 'wb') as output, stream:
                        shutil.copyfileobj(stream, output, 1024 * 1024)
                    if not verify(temp, row):
                        raise ValueError('Selected art hash mismatch.')
                    temp.replace(target)
                finally:
                    temp.unlink(missing_ok=True)
    return {'verifiedFiles': len(rows), 'bytes': sum(r['size'] for r in rows)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=Path(__file__).resolve().parents[2] / 'ContentSource/Art/presentation-selection.json')
    parser.add_argument('--cache', type=Path, default=Path(os.environ.get('FRONTIER_PRESENTATION_CACHE', str(Path.home() / 'code/game_asset_cache/frontier-presentation'))))
    parser.add_argument('--archives', type=Path, default=Path.home() / 'code/game_asset_cache/archives/frontier-presentation')
    parser.add_argument('--library', default='http://192.168.1.101:8090')
    parser.add_argument('--offline', action='store_true')
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding='utf8'))
    game = relative(manifest['game_id'])
    if len(game.parts) != 1:
        raise ValueError('Game ID must be one safe directory name.')
    print(json.dumps(restore(manifest, args.cache.expanduser().absolute() / game.as_posix(), args.archives.expanduser().absolute(), args.library, args.offline)))


if __name__ == '__main__':
    main()
