#!/usr/bin/env bash
# G0 — compile, pousse et exécute la micro-sonde dlopen via ADB (informatif).
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
ensure_data_dir
OUT="$DATA_DIR/g0-dlopen-shell.txt"
if [ "$DRY_RUN" -eq 1 ]; then
  log 'dry-run : compilation NDK, push vers /data/local/tmp, exécution, sortie capturée.'
  exit 0
fi
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
adb_target
mkdir -p "$PROBE_DIR/out"
"$TS/aarch64-linux-android26-clang" "$PROBE_DIR/g0-dlopen-probe.c" -o "$PROBE_DIR/out/dlopen-probe" -ldl
run_adb push "$PROBE_DIR/out/dlopen-probe" /data/local/tmp/jf-dlopen-probe
shell_cmd 'chmod 744 /data/local/tmp/jf-dlopen-probe && /data/local/tmp/jf-dlopen-probe' > "$OUT" 2>&1
log "sortie : $OUT"; cat "$OUT"
