#!/usr/bin/env bash
# Matrice ffmpeg statique aarch64 (prérequis G2) : test sans appareil ni compilation.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }

t "variant inconnu : refus explicite" bash -c '! DRY_RUN=1 bash ../../tools/probe/build-ffmpeg.sh bogus 2>/dev/null'
t "dry-run sw"         bash -c 'DRY_RUN=1 bash ../../tools/probe/build-ffmpeg.sh sw'
t "dry-run v4l2m2m"    bash -c 'DRY_RUN=1 bash ../../tools/probe/build-ffmpeg.sh v4l2m2m'
t "dry-run mediacodec" bash -c 'DRY_RUN=1 bash ../../tools/probe/build-ffmpeg.sh mediacodec'
t "dry-run nvdec"      bash -c 'DRY_RUN=1 bash ../../tools/probe/build-ffmpeg.sh nvdec'
t "shellcheck propre"  bash -c 'shellcheck ../../tools/probe/build-ffmpeg.sh'

exit $fail
