#!/usr/bin/env python3
"""Run the native Ashfall County acceptance probe against a built player and record a validation report.

Usage: python tools/playtest/ashfall_qa.py --player /abs/build/Game.exe [--size 3072]

The probe always uses an isolated GameWorkbenchQA user directory outside the checkout, so real saves and
preferences are never touched. The report separates what the native run proved from what remains unverified.
"""
import argparse, json, os, subprocess, sys, time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player', required=True, type=Path)
    parser.add_argument('--size', type=int, default=3072)
    parser.add_argument('--timeout', type=int, default=900)
    args = parser.parse_args()
    player = args.player.expanduser().resolve()
    if not player.is_file():
        raise SystemExit('Player not found: ' + str(player))
    stamp = datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')
    base = Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'GameWorkbench' / 'qa' / ('ashfall-' + stamp)
    user = base / 'GameWorkbenchQA'
    user.mkdir(parents=True)
    report = base / 'ashfall-qa.json'
    cmd = [str(player), '-screen-width', '1600', '-screen-height', '900', '-screen-fullscreen', '0',
           '-ashfall-qa=' + str(report), '-ashfall-user-dir=' + str(user), '-ashfall-qa-size=' + str(args.size), '-logFile', str(base / 'player.log')]
    started = time.time()
    result = subprocess.run(cmd, cwd=player.parent, timeout=args.timeout)
    data = json.loads(report.read_text(encoding='utf8'))
    build_dir = player.parent
    native = build_dir / 'native-build.json'
    summary = {
        'schemaVersion': 1,
        'game': 'ashfall-county',
        'kind': 'native-gameplay-probe',
        'platform': data.get('platform'),
        'unity': data.get('unity'),
        'recorded': datetime.now(timezone.utc).isoformat(),
        'player': str(player),
        'nativeBuild': json.loads(native.read_text()) if native.is_file() else None,
        'exitCode': result.returncode,
        'durationSeconds': round(time.time() - started, 1),
        'status': data.get('status'),
        'county': {k: data.get(k) for k in ('seed', 'mapSize', 'buildings', 'settlements', 'zombies', 'generationMs')},
        'performance': {k: data.get(k) for k in ('averageFrameMs', 'worstFrameMs', 'simMs', 'viewMs', 'uiMs', 'activeZombies', 'loadedChunks', 'loadedBuildings')},
        'ownedAssets': {'preparedArtRoles': data.get('purchasedArtRoles'), 'ownedAudioCues': data.get('purchasedAudioCues')},
        'checks': data.get('checks', []),
        'qaOnlySetup': data.get('qaSetup', []),
        'screenshots': data.get('screenshots', []),
        'notProven': [
            'Physical controller hardware (the probe uses a virtual gamepad device)',
            'Human playtime and the evacuation outcome over a full multi-hour session',
            'Linux/macOS native builds, LAN multiplayer (not implemented in the rebuilt game), Steam packaging',
            'Long-session memory and save growth, and every sandbox option combination',
        ],
    }
    out = ROOT / 'Docs' / 'ValidationReports' / ('AshfallSurvival-' + (data.get('platform') or 'native') + '-' + stamp + '.json')
    out.write_text(json.dumps(summary, indent=2) + '\n', encoding='utf8')
    print(json.dumps({'status': summary['status'], 'report': str(out), 'evidence': str(base)}))
    return 0 if summary['status'] == 'passed' else 1


if __name__ == '__main__':
    sys.exit(main())
