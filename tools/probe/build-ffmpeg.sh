#!/usr/bin/env bash
# Construction de la matrice ffmpeg statique aarch64 pour les sondes G2.
# Usage : build-ffmpeg.sh sw | v4l2m2m | mediacodec | nvdec
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
VARIANT="${1:-}"
case "$VARIANT" in
  sw)         CFG_EXTRA="" ;;
  v4l2m2m)    CFG_EXTRA="--enable-v4l2-m2m" ;;
  mediacodec) CFG_EXTRA="--enable-mediacodec" ;;
  nvdec)      CFG_EXTRA="--enable-nvdec" ;;
  *) die "variant inconnu ou absent : '$VARIANT' (attendu : sw | v4l2m2m | mediacodec | nvdec)." ;;
esac
if [ "$DRY_RUN" -eq 1 ]; then
  log "dry-run : variant=$VARIANT ($CFG_EXTRA), fetch + configure + make ffmpeg."
  exit 0
fi
require_cmd git make
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TAG="v7.1.3-6"
SRC="$PROBE_DIR/build/ffmpeg-$TAG"
[ -d "$SRC" ] || git clone --depth 1 --branch "$TAG" https://github.com/jellyfin/jellyfin-ffmpeg "$SRC"
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
OUT="$PROBE_DIR/out/ffmpeg-$VARIANT"; mkdir -p "$OUT"
cd "$SRC" && make distclean 2>/dev/null || true
# shellcheck disable=SC2086
if ! ./configure \
  --arch=aarch64 --target-os=android --enable-cross-compile \
  --cc="$TS/aarch64-linux-android26-clang" \
  --ar="$TS/llvm-ar" --ranlib="$TS/llvm-ranlib" --strip="$TS/llvm-strip" \
  --disable-doc --disable-ffplay --disable-ffprobe --disable-network \
  --extra-cflags="-Oz -fPIC" --extra-ldflags="-Wl,--gc-sections" \
  $CFG_EXTRA \
  --prefix="$OUT" > "$OUT/configure.log" 2>&1; then
  log "configure a échoué pour $VARIANT (cf. $OUT/configure.log) — constat enregistré (nvdec : attendu sans SDK L4T)."
  exit 0
fi
make -j"$(nproc)" ffmpeg
cp -f ./ffmpeg "$OUT/ffmpeg"
log "$VARIANT : $OUT/ffmpeg"
if [ "$VARIANT" = "mediacodec" ]; then
  {
    echo "# détection de l'encodeur h264_mediacodec via config.h du build :"
    if grep -q "CONFIG_H264_MEDIACODEC_ENCODER 1" "$SRC/config.h"; then
      echo "ENCODEUR PRÉSENT (CONFIG_H264_MEDIACODEC_ENCODER 1)."
    else
      echo "ABSENT : aucune macro CONFIG_H264_MEDIACODEC_ENCODER=1 dans config.h."
    fi
  } > "$OUT/encoders-presence.txt"
  log "presence encodeur : $(tr '\n' ' ' < "$OUT/encoders-presence.txt")"
fi
