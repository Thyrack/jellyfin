#!/usr/bin/env bash
# Tests du benchmark G2 — source + shellcheck + dry-run, sans appareil.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }

t "sourcing sans exécuter main (garde \${BASH_SOURCE[0]} = \$0)" bash -c '
  source ../../tools/probe/bench.sh && type run_case >/dev/null && type parse_progress_speed >/dev/null'
t "shellcheck propre" bash -c 'shellcheck ../../tools/probe/bench.sh'
t "dry-run sans appareil ni ffmpeg : OK" bash -c '
  DRY_RUN=1 bash ../../tools/probe/bench.sh | grep -q "dry-run"'
t "run_case consomme les parseurs lib.sh (aucun awk inline)" bash -c '
  grep -q parse_progress_fps ../../tools/probe/bench.sh &&
  grep -q parse_progress_speed ../../tools/probe/bench.sh &&
  ! grep -q awk ../../tools/probe/bench.sh'

exit $fail
