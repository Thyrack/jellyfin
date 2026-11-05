#!/usr/bin/env bash
# Construction de libx264 statique aarch64 (bionic, API 26) pour le ffmpeg « server-live ».
# Usage : build-x264.sh
# Sortie : tools/probe/out/x264/{lib/libx264.a,include/}
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
if [ "$DRY_RUN" -eq 1 ]; then
  log "dry-run : fetch x264 (git) + configure cross aarch64 (statique, sans CLI) + make."
  exit 0
fi
require_cmd make git
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
OUT="$PROBE_DIR/out/x264"; mkdir -p "$OUT"
SRC="$PROBE_DIR/build/x264"
if [ ! -d "$SRC" ]; then
  log "fetch x264…"
  mkdir -p "$PROBE_DIR/build"
  git clone --depth 1 https://code.videolan.org/videolan/x264.git "$SRC"
fi
cd "$SRC"
make distclean 2>/dev/null || true
CC="$TS/aarch64-linux-android26-clang" \
STRINGS="$TS/llvm-strings" \
AR="$TS/llvm-ar" \
RANLIB="$TS/llvm-ranlib" \
bash ./configure \
  --host=aarch64-linux-android \
  --cross-prefix="$TS/aarch64-linux-android26-" \
  --enable-static --enable-pic --disable-cli --disable-opencl \
  --prefix="$OUT" \
  > "$OUT/configure.log" 2>&1
make -j"$(nproc)" > "$OUT/build.log" 2>&1
make install > "$OUT/install.log" 2>&1
[ -f "$OUT/lib/libx264.a" ] || die "libx264.a absente après install (cf. $OUT/install.log)."
log "x264 statique OK (asm NEON ARM64 active via intl. assembleur clang) : $OUT/lib/libx264.a"
