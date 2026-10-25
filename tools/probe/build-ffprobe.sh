#!/usr/bin/env bash
# Construction de ffprobe Android aarch64 basé sur jellyfin-ffmpeg v7.1.3-6.
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
if [ "$DRY_RUN" -eq 1 ]; then
  log "dry-run : fetch v7.1.3-6 + configure minimal + make ffprobe + install vers out/ffprobe."
  exit 0
fi
require_cmd git make
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TAG="v7.1.3-6"
SRC="$PROBE_DIR/build/ffmpeg-$TAG"
[ -d "$SRC" ] || git clone --depth 1 --branch "$TAG" https://github.com/jellyfin/jellyfin-ffmpeg "$SRC"
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
mkdir -p "$PROBE_DIR/out/ffprobe"
cd "$SRC" && make distclean 2>/dev/null || true
./configure \
  --arch=aarch64 --target-os=android --enable-cross-compile \
  --cc="$TS/aarch64-linux-android26-clang" \
  --ar="$TS/llvm-ar" --ranlib="$TS/llvm-ranlib" --strip="$TS/llvm-strip" \
  --disable-programs --enable-ffprobe --disable-doc --disable-network \
  --extra-cflags="-Oz -fPIC" --extra-ldflags="-Wl,--gc-sections" \
  --prefix="$PROBE_DIR/out/ffprobe"
make -j"$(nproc)" ffprobe
cp -f ./ffprobe "$PROBE_DIR/out/ffprobe/ffprobe"
"$TS/llvm-readelf" -h "$PROBE_DIR/out/ffprobe/ffprobe" | grep -q AArch64 || die "binaire non aarch64."
log "ok : $PROBE_DIR/out/ffprobe/ffprobe"
