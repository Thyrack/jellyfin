#!/usr/bin/env bash
# tests du pipeline d'assets serveur — exécutables sans build réel.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }
t "shellcheck propre" bash -c 'shellcheck ../../deployment/apk-android/collect-assets.sh'
t "dry-run : 5 étapes listées, aucune IO" bash -c '
  DRY_RUN=1 bash ../../deployment/apk-android/collect-assets.sh 2>&1 | grep -c "^-> " | grep -qx "5"'
exit $fail
