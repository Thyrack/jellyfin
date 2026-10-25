#!/usr/bin/env bash
# G0 — inventaire firmware/matériel : test sans appareil.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }

t "shellcheck propre" bash -c 'shellcheck ../../tools/probe/g0-audit.sh'
t "dry-run : 6 sections" bash -c '
  DRY_RUN=1 bash ../../tools/probe/g0-audit.sh >/dev/null 2>&1
  grep -c "^### " ../../docs/jellyfin-shield/probe-data/g0-raw.txt | grep -qx "6"'

exit $fail
