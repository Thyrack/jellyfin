#!/usr/bin/env bash
# Tests de la bibliothèque commune — exécutables sans appareil.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }

t "shellcheck propre" bash -c 'shellcheck ../../tools/probe/lib.sh'
t "selftest : 3 lignes" bash -c '
  bash ../../tools/probe/lib.sh --selftest | grep -c "\[selftest\]" | grep -qx "3"'
t "dry-run sans appareil ni IP : selftest OK" bash -c '
  DRY_RUN=1 bash ../../tools/probe/lib.sh --selftest >/dev/null'
t "parse_progress_fps sur la fixture renvoie 30" bash -c '
  source ../../tools/probe/lib.sh >/dev/null
  parse_progress_fps fixtures/sample-progress.txt | grep -qx "30"'
t "parse_progress_speed sur la fixture renvoie 1.2" bash -c '
  source ../../tools/probe/lib.sh >/dev/null
  parse_progress_speed fixtures/sample-progress.txt | grep -qx "1.2"'

exit $fail
