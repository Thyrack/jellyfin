#!/usr/bin/env bash
# T4 — connecteur ADB réseau : test sans appareil.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }

t "sans IP : refus explicite" bash -c '! DRY_RUN=1 bash ../../tools/probe/connect.sh >/dev/null 2>&1'
t "shellcheck propre"         bash -c 'shellcheck ../../tools/probe/connect.sh'

exit $fail
