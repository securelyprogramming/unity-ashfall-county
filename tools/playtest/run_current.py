#!/usr/bin/env python3
"""Build current source into a fresh external directory, then launch its native player."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import uuid

VERSION = '6000.6.0f1'


def editor_candidates(host, environment):
    if environment.get('UNITY_EDITOR'):
        return [Path(environment['UNITY_EDITOR']).expanduser()]
    roots = []
    if environment.get('UNITY_EDITORS_ROOT'):
        roots.append(Path(environment['UNITY_EDITORS_ROOT']).expanduser())
    if host == 'Darwin':
        roots += [Path('/Applications/Unity/Hub/Editor'), Path.home() / 'Applications/Unity/Hub/Editor']
        # Bounded discovery of existing external DevelopmentTools locations; no installs or crawl.
        volumes = Path('/Volumes')
        if volumes.is_dir():
            for volume in sorted(volumes.iterdir())[:32]:
                if not volume.is_dir():
                    continue
                roots.append(volume / 'DevelopmentTools/Unity')
                try:
                    children = sorted(volume.iterdir())[:32]
                except OSError:
                    continue
                roots += [child / 'DevelopmentTools/Unity' for child in children if child.is_dir()]
        return [root / VERSION / 'Unity.app/Contents/MacOS/Unity' for root in roots]
    if host == 'Windows':
        roots.append(Path(environment.get('ProgramFiles', 'C:/Program Files')) / 'Unity/Hub/Editor')
        return [root / VERSION / 'Editor/Unity.exe' for root in roots]
    roots += [Path.home() / 'Unity/Hub/Editor', Path('/opt/Unity/Hub/Editor')]
    return [root / VERSION / 'Editor/Unity' for root in roots]


def main(argv=None, repository=None):
    parser = argparse.ArgumentParser(description=__doc__, epilog='Optional native player arguments follow --, for example: ./run.sh -- -screen-width 1280 -screen-height 720')
    parser.add_argument('--output', type=Path, help='Fresh or empty directory outside this checkout')
    parser.add_argument('--build-only', action='store_true', help='Build without launching')
    parser.add_argument('--check', action='store_true', help='Check private sources and native build prerequisites without building')
    parser.add_argument('--restore-archive', type=Path, help='Restore only reviewed paid sources from a private owned archive before building')
    args, game_arguments = parser.parse_known_args(argv)
    if game_arguments and game_arguments[0] == '--':
        game_arguments = game_arguments[1:]
    root = Path(repository).resolve() if repository is not None else Path(__file__).resolve().parents[2]
    host = platform.system()
    profile, artifact = {'Darwin': ('macos', 'Game.app'), 'Linux': ('linux', 'Game.x86_64'), 'Windows': ('windows', 'Game.exe')}.get(host, (None, None))
    if profile is None:
        raise RuntimeError('Use the native macOS, Linux, or Windows host for this project.')
    environment = os.environ.copy()
    for candidate in editor_candidates(host, environment):
        if candidate.is_file():
            environment['UNITY_EDITOR'] = str(candidate.resolve()); break
    if args.output is None:
        cache = Path(environment.get('FRONTIER_BUILD_ROOT', str(Path.home() / ('Library/Caches' if host == 'Darwin' else 'AppData/Local' if host == 'Windows' else '.cache') / 'GameWorkbench/playtests'))).expanduser()
        slug = root.name.removeprefix('unity-')
        output = cache / slug / (datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:12])
    else:
        output = args.output.expanduser()
    output = output.resolve()
    if output == root or root in output.parents:
        raise RuntimeError('Choose a fresh output outside the source checkout.')
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        raise RuntimeError('Preserving earlier builds: output must be fresh or empty.')
    restore = [sys.executable, str(root / 'tools/playtest/restore_selection.py'), '--verify-only']
    if args.restore_archive is not None:
        if args.check:
            restore += ['--archive', str(args.restore_archive.expanduser())]
        else:
            restore.remove('--verify-only'); restore += ['--archive', str(args.restore_archive.expanduser())]
    subprocess.run(restore, cwd=root, env=environment, check=True)
    build = [sys.executable, str(root / 'tools/playtest/build.py'), '--platform', profile, '--output', str(output)]
    if args.check:
        build.append('--check')
    subprocess.run(build, cwd=root, env=environment, check=True)
    player = output / artifact
    if args.check:
        return {'status': 'checked', 'platform': profile}
    if not player.exists():
        raise RuntimeError('The build wrapper returned without the expected native player.')
    if args.build_only:
        print(json.dumps({'status': 'built', 'artifact': str(player)}))
        return {'status': 'built', 'artifact': str(player)}
    if host == 'Darwin':
        subprocess.run(['/usr/bin/open', '-n', str(player), '--args', *game_arguments], check=True)
    elif host == 'Linux':
        if not player.is_file() or not os.access(player, os.X_OK):
            raise RuntimeError('The Linux player is missing executable permission.')
        os.execv(str(player), [str(player), *game_arguments])
    else:
        subprocess.run([str(player), *game_arguments], cwd=output, check=True)
    return {'status': 'launched', 'artifact': str(player)}


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, OSError, subprocess.CalledProcessError) as error:
        raise SystemExit('Run error: ' + str(error))
