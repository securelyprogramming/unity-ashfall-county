#!/usr/bin/env bash
set -euo pipefail
frontier_repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
exec "${PYTHON:-python3}" "$frontier_repo_root/tools/playtest/run_current.py" "$@"
