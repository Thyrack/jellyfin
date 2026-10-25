#!/usr/bin/env bash
# G1 — lecture du disque USB via ffprobe statique : test sans appareil.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }

t "dry-run sans appareil" bash -c 'DRY_RUN=1 bash ../../tools/probe/g1-storage.sh'
t "shellcheck propre"     bash -c 'shellcheck ../../tools/probe/g1-storage.sh'

exit $fail
