#!/usr/bin/env bash
# ffprobe statique aarch64 (prérequis G1) : test sans appareil ni compilation.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }

t "dry-run sans appareil"  bash -c 'DRY_RUN=1 bash ../../tools/probe/build-ffprobe.sh'
t "shellcheck propre"      bash -c 'shellcheck ../../tools/probe/build-ffprobe.sh'
t "tag v7.1.3-6 présent"   grep -q 'v7.1.3-6' ../../tools/probe/build-ffprobe.sh

exit $fail
