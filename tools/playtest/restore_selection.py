#!/usr/bin/env python3
"""Restore the reviewed three-file art selection from the owner's private Unity package.
No package scripts, metadata sidecars, receipts or unselected assets are imported.
"""
import argparse
import hashlib
import gzip
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import tarfile
import tempfile

ARCHIVE_SHA256 = '1f2f9a8c6b5fa3b0a2400a9c501053bc23502f0767ef8442590fccdc1f0c648c'
SOURCE_PREFIX = 'Assets/Animpic Studio/Spline Builder Plugin/Scenes/FencePresentation/Models/'
REVIEWED = {
    SOURCE_PREFIX + 'SM_StoneFoot.fbx': ('SplineBuilderFooting/SM_StoneFoot.fbx', 105788, 'a110d940730657b896d229f3c1d7ec729323673a9ba4126bb71d273ee28349e4'),
    SOURCE_PREFIX + 'SM_StoneFootConnector.fbx': ('SplineBuilderFooting/SM_StoneFootConnector.fbx', 156364, '25168fe06062c8e0fdfba12ca5c32138cdbea94f8df3f138d39adda17e15f6f3'),
    SOURCE_PREFIX + 'Polygon_Texture2.png': ('SplineBuilderFooting/Polygon_Texture2.png', 82411, '1af7a17ccd0df06088204661a2fb0598d5a4b39146f0b805df42a2c9368fa77f'),
}
MAX_ARCHIVE = 64 * 1024 * 1024
MAX_UNPACKED = 64 * 1024 * 1024
MAX_MEMBER = 16 * 1024 * 1024
MAX_SELECTION = 16 * 1024 * 1024
MAX_MEMBERS = 10000
MAX_PATHNAME = 2048
ALLOWED = {'.fbx', '.obj', '.png', '.jpg', '.jpeg', '.tga', '.wav', '.ogg'}
SHA = re.compile(r'^[a-f0-9]{64}$')


def safe_relative(value):
    if not isinstance(value, str) or not value or len(value) > 800 or '\\' in value or ':' in value or any(ord(c) < 32 for c in value):
        raise ValueError('Expected a bounded relative POSIX path.')
    parts = value.split('/')
    if any(not part or part in {'.', '..'} or part.endswith((' ', '.')) for part in parts):
        raise ValueError('Unsafe relative path component.')
    path = PurePosixPath(value)
    if path.is_absolute():
        raise ValueError('Absolute paths are not permitted.')
    for part in parts:
        stem = part.split('.')[0].upper()
        if stem in {'CON', 'PRN', 'AUX', 'NUL'} or re.match(r'^(COM|LPT)[1-9]$', stem):
            raise ValueError('Reserved filesystem path component.')
    return path


def digest_file(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(data)
    return digest.hexdigest()


def validate_rows(rows):
    if not isinstance(rows, list) or not rows or len(rows) > 64:
        raise ValueError('Selection must contain a bounded file list.')
    total, sources, destinations = 0, set(), set()
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError('Invalid selection record.')
        source = str(safe_relative(row.get('source_path')))
        destination = str(safe_relative(row.get('relative_path')))
        if PurePosixPath(source).suffix.lower() not in ALLOWED or PurePosixPath(destination).suffix.lower() not in ALLOWED:
            raise ValueError('Only reviewed portable art/audio is permitted; package code is excluded.')
        if source in sources or destination.casefold() in destinations:
            raise ValueError('Ambiguous source or destination in selection.')
        sources.add(source)
        destinations.add(destination.casefold())
        if type(row.get('size')) is not int or not 0 < row['size'] <= MAX_SELECTION or not SHA.fullmatch(row.get('sha256', '')):
            raise ValueError('Selection files need bounded sizes and exact SHA-256 pins.')
        total += row['size']
    if total > MAX_SELECTION:
        raise ValueError('Reviewed selection exceeds 16 MiB.')
    return rows


def validate_production_manifest(manifest):
    if not isinstance(manifest, dict) or manifest.get('schema_version') != 1 or manifest.get('source_type') != 'unitypackage-selection':
        raise ValueError('Expected the version-one reviewed Unity package selection.')
    if manifest.get('source_archive') != 'Spline_Builder_Plugin.unitypackage' or manifest.get('archive_sha256') != ARCHIVE_SHA256:
        raise ValueError('Manifest does not identify the reviewed owned archive.')
    rows = validate_rows(manifest.get('files'))
    records = {row['source_path']: (row['relative_path'], row['size'], row['sha256']) for row in rows}
    if records != REVIEWED:
        raise ValueError('Only the three reviewed footing meshes and palette may be restored. Review new selections separately.')
    return rows


def cache_root(value, checkout=None):
    requested = Path(value).expanduser().absolute()
    if requested.is_symlink():
        raise ValueError('The private cache root cannot be a symlink.')
    root = requested.resolve()
    if checkout is not None:
        checkout = Path(checkout).resolve()
        if root == checkout or checkout in root.parents:
            raise ValueError('Keep the licensed source cache outside the checkout.')
    return root


def safe_target(root, relative):
    root = cache_root(root)
    current = root
    for index, part in enumerate(safe_relative(relative).parts):
        current = current / part
        try:
            info = current.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode):
            raise ValueError('Private cache paths cannot follow symlinks.')
        if index < len(PurePosixPath(relative).parts) - 1 and not stat.S_ISDIR(info.st_mode):
            raise ValueError('A cache parent is not a directory.')
        if index == len(PurePosixPath(relative).parts) - 1 and not stat.S_ISREG(info.st_mode):
            raise ValueError('A cache destination is not a regular file.')
    if root not in current.resolve().parents:
        raise ValueError('Selection escapes the private cache.')
    return current


def verify_cache(rows, root):
    validate_rows(rows)
    for row in rows:
        target = safe_target(root, row['relative_path'])
        if not target.is_file():
            raise FileNotFoundError('Missing private source: ' + row['relative_path'])
        if target.stat().st_size != row['size'] or digest_file(target) != row['sha256']:
            raise ValueError('Existing private source differs; preserving it: ' + row['relative_path'])
    return {'verified_files': len(rows), 'restored_files': 0, 'bytes': sum(row['size'] for row in rows)}


def checked_member(member, seen):
    # Unity packages consist of safe relative tar members. We never extract any pathname.
    safe_relative(member.name.rstrip('/') if member.isdir() else member.name)
    if member.name in seen:
        raise ValueError('Duplicate archive member; package is ambiguous.')
    seen.add(member.name)
    if not member.isfile() and not member.isdir():
        raise ValueError('Links and special archive members are not permitted.')
    if not 0 <= member.size <= MAX_MEMBER:
        raise ValueError('Archive member exceeds its bounded size.')


class BoundedInflated:
    """Bound decompression before tarfile parses PAX headers or allocates their payload."""
    def __init__(self, source):
        self.source, self.total = source, 0
    def read(self, size):
        if size < 0 or size > MAX_UNPACKED:
            raise ValueError('Unbounded archive read is not permitted.')
        remaining = MAX_UNPACKED - self.total
        data = self.source.read(min(size, remaining + 1))
        self.total += len(data)
        if self.total > MAX_UNPACKED:
            raise ValueError('Archive exceeds its expanded-byte bound.')
        return data


def read_archive_selection(archive_path, rows, expected_hash):
    """Bounded reader, also used by offline fixture tests. CLI always supplies production pins."""
    validate_rows(rows)
    if not SHA.fullmatch(expected_hash or ''):
        raise ValueError('Archive requires an exact SHA-256 pin.')
    selected = {row['source_path']: row for row in rows}
    with Path(archive_path).open('rb') as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode) or not 0 < info.st_size <= MAX_ARCHIVE:
            raise ValueError('Archive must be a regular file below 64 MiB.')
        digest = hashlib.sha256()
        for data in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(data)
        if digest.hexdigest() != expected_hash:
            raise ValueError('Source archive differs from the reviewed SHA-256.')
        stream.seek(0)
        seen, pathnames, assets, total, count = set(), {}, {}, 0, 0
        with gzip.GzipFile(fileobj=stream, mode='rb') as inflated, tarfile.open(fileobj=BoundedInflated(inflated), mode='r|') as archive:
            for member in archive:
                checked_member(member, seen)
                count += 1; total += member.size
                if count > MAX_MEMBERS or total > MAX_UNPACKED:
                    raise ValueError('Archive exceeds member or expanded-byte limits.')
                if member.isfile() and member.name.endswith('/asset'):
                    assets[member.name] = member.size
                if not member.isfile() or not member.name.endswith('/pathname'):
                    continue
                if not 0 < member.size <= MAX_PATHNAME:
                    raise ValueError('Unity package pathname exceeds its bound.')
                handle = archive.extractfile(member)
                data = handle.read(MAX_PATHNAME + 1)
                if len(data) != member.size:
                    raise ValueError('Truncated Unity package pathname.')
                source = data.decode('utf-8')
                safe_relative(source)
                if source in pathnames:
                    raise ValueError('Ambiguous duplicate Unity source pathname.')
                pathnames[source] = member.name[:-len('pathname')] + 'asset'
        wanted = {}
        for source, row in selected.items():
            member_name = pathnames.get(source)
            if member_name is None or assets.get(member_name) != row['size']:
                raise ValueError('Reviewed source is missing or differs in byte count: ' + source)
            wanted[member_name] = row
        stream.seek(0)
        payloads = {}
        with gzip.GzipFile(fileobj=stream, mode='rb') as inflated, tarfile.open(fileobj=BoundedInflated(inflated), mode='r|') as archive:
            for member in archive:
                row = wanted.get(member.name)
                if row is None:
                    continue
                if not member.isfile() or member.size != row['size']:
                    raise ValueError('Source archive changed during restoration.')
                handle = archive.extractfile(member)
                data = handle.read(row['size'] + 1)
                if len(data) != row['size'] or hashlib.sha256(data).hexdigest() != row['sha256']:
                    raise ValueError('Selected source differs from its reviewed SHA-256.')
                payloads[row['relative_path']] = data
        if len(payloads) != len(rows):
            raise ValueError('Incomplete reviewed selection.')
        stream.seek(0)
        digest = hashlib.sha256()
        for data in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(data)
        if digest.hexdigest() != expected_hash:
            raise ValueError('Source archive changed during restoration.')
    return payloads


def write_cache(rows, root, payloads):
    # Check all original destinations before creating any reviewed file.
    pending = []
    validate_rows(rows)
    for row in rows:
        payload = payloads.get(row['relative_path'])
        if not isinstance(payload, bytes) or len(payload) != row['size'] or hashlib.sha256(payload).hexdigest() != row['sha256']:
            raise ValueError('Unverified cache payload; no files written.')
    for row in rows:
        target = safe_target(root, row['relative_path'])
        if target.exists():
            if target.stat().st_size != row['size'] or digest_file(target) != row['sha256']:
                raise ValueError('Existing private source differs; preserving it: ' + row['relative_path'])
        else:
            pending.append((row, target))
    for row, target in pending:
        target.parent.mkdir(parents=True, exist_ok=True)
        safe_target(root, row['relative_path'])
        fd, temporary_name = tempfile.mkstemp(prefix='.frontier-reviewed-', dir=target.parent)
        temporary = Path(temporary_name)
        try:
            with os.fdopen(fd, 'wb') as output:
                output.write(payloads[row['relative_path']]); output.flush(); os.fsync(output.fileno())
            # Link a complete staged file atomically without replacing a concurrent original.
            try:
                os.link(temporary, target)
            except FileExistsError:
                if target.is_symlink() or target.stat().st_size != row['size'] or digest_file(target) != row['sha256']:
                    raise ValueError('Concurrent private source differs; preserving it.')
        finally:
            temporary.unlink(missing_ok=True)
    result = verify_cache(rows, root)
    result['restored_files'] = len(pending)
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    repository = Path(__file__).resolve().parents[2]
    parser.add_argument('--manifest', type=Path, default=repository / 'ContentSource/Art/selection.json')
    parser.add_argument('--archive', type=Path, help='Private purchased Spline_Builder_Plugin.unitypackage')
    parser.add_argument('--cache', type=Path, default=Path(os.environ.get('FRONTIER_ASSET_CACHE', str(Path.home() / 'code/game_asset_cache/frontier-synty'))))
    parser.add_argument('--verify-only', action='store_true', help='Verify existing private bytes; make no filesystem changes')
    args = parser.parse_args(argv)
    if args.manifest.stat().st_size > 64 * 1024:
        raise ValueError('Selection manifest exceeds its bound.')
    rows = validate_production_manifest(json.loads(args.manifest.read_text(encoding='utf-8')))
    root = cache_root(args.cache, repository)
    if args.verify_only:
        if args.archive:
            read_archive_selection(args.archive.expanduser(), rows, ARCHIVE_SHA256)
        result = verify_cache(rows, root)
    else:
        if args.archive is None:
            parser.error('Restoration requires --archive; use --verify-only to check an existing cache.')
        payloads = read_archive_selection(args.archive.expanduser(), rows, ARCHIVE_SHA256)
        result = write_cache(rows, root, payloads)
    print(json.dumps({**result, 'selection': 'Spline Builder footing meshes and palette', 'package_code_imported': False}))
    return result


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, tarfile.TarError, UnicodeError) as error:
        raise SystemExit('Restoration error: ' + str(error))
