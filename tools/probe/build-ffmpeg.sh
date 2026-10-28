#!/usr/bin/env bash
# Construction de la matrice ffmpeg statique aarch64 pour les sondes G2,
# plus le variant « server-live » embarqué dans l'APK (Lot ffmpeg Live :
# réseau + TLS openssl statique + libx264 + mediacodec + ffprobe).
# Usage : build-ffmpeg.sh sw | v4l2m2m | mediacodec | nvdec | server-live
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
VARIANT="${1:-}"
case "$VARIANT" in
  sw)         CFG_EXTRA="" ;;
  v4l2m2m)    CFG_EXTRA="--enable-v4l2-m2m" ;;
  # ffjni requis par le backend MediaCodec (constat de configure du run T8)
  mediacodec) CFG_EXTRA="--enable-mediacodec --enable-jni --enable-decoder=h264_mediacodec,hevc_mediacodec --enable-encoder=h264_mediacodec,hevc_mediacodec" ;;
  nvdec)      CFG_EXTRA="--enable-nvdec" ;;
  # Variant embarqué : réseau (http/https via openssl statique), libx264 (transcodage
  # LiveTV Jellyfin), mediacodec (G4), ffprobe requis par le serveur (MVP sonde = sans réseau).
  server-live)
    # --enable-version3 : licence Apache-2.0 d'OpenSSL 3 exigée par le configure
    CFG_EXTRA="--enable-version3 --enable-gpl --enable-openssl --enable-libx264 \
      --enable-mediacodec --enable-jni \
      --enable-decoder=h264_mediacodec,hevc_mediacodec \
      --enable-encoder=h264_mediacodec,hevc_mediacodec"
    OPENSSL_OUT="$PROBE_DIR/out/openssl"
    X264_OUT="$PROBE_DIR/out/x264"
    DEP_CFLAGS="-I$OPENSSL_OUT/include -I$X264_OUT/include"
    DEP_LDFLAGS="-L$OPENSSL_OUT/lib -L$X264_OUT/lib"
    DEP_LIBS="-lssl -lcrypto -lx264"
    MAKE_TARGETS="ffmpeg ffprobe"
    ;;
  *) die "variant inconnu ou absent : '$VARIANT' (attendu : sw | v4l2m2m | mediacodec | nvdec | server-live)." ;;
esac
if [ "$DRY_RUN" -eq 1 ]; then
  log "dry-run : variant=$VARIANT ($CFG_EXTRA), fetch + configure + make ${MAKE_TARGETS:-ffmpeg}."
  exit 0
fi
DEP_CFLAGS="${DEP_CFLAGS:-}"
DEP_LDFLAGS="${DEP_LDFLAGS:-}"
DEP_LIBS="${DEP_LIBS:-}"
MAKE_TARGETS="${MAKE_TARGETS:-ffmpeg}"   # sonde : ffmpeg seul (--disable-ffprobe)
# Les variantes de sonde excluent le réseau et ffprobe (scope G2 : fichiers locaux) ;
# server-live les garde (chaînes IPTV https + probing du serveur Jellyfin).
PROBE_EXCLUSIONS="--disable-ffprobe --disable-network"
if [ "$VARIANT" = "server-live" ]; then
  PROBE_EXCLUSIONS=""
  [ -f "${OPENSSL_OUT:-}/lib/libssl.a" ] || die "openssl statique absente : lancer build-openssl.sh."
  [ -f "${X264_OUT:-}/lib/libx264.a" ] || die "libx264 statique absente : lancer build-x264.sh."
  # le configure résout x264 (et openssl) via pkg-config ; --static tire les Libs.private.
  # pkgconf est installé localement (pas de sudo pour apt) : /home/bruno/tools/pkgconf.
  PKGCONF_BIN="/home/bruno/tools/pkgconf/bin/pkgconf"
  PKGCONF_LIB="/home/bruno/tools/pkgconf/lib"
  [ -x "$PKGCONF_BIN" ] || die "pkgconf local absent ($PKGCONF_BIN) — cf. README build."
  if [ -d "$PKGCONF_LIB" ]; then export LD_LIBRARY_PATH="$PKGCONF_LIB${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"; fi
  export PKG_CONFIG_PATH="$X264_OUT/lib/pkgconfig:$OPENSSL_OUT/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
  CFG_EXTRA="$CFG_EXTRA --pkg-config=$PKGCONF_BIN --pkg-config-flags=--static"
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
  --disable-doc --disable-ffplay $PROBE_EXCLUSIONS \
  --extra-cflags="-Oz -fPIC $DEP_CFLAGS" \
  --extra-ldflags="-Wl,--gc-sections $DEP_LDFLAGS" \
  --extra-libs="$DEP_LIBS" \
  $CFG_EXTRA \
  --prefix="$OUT" > "$OUT/configure.log" 2>&1; then
  log "configure a échoué pour $VARIANT (cf. $OUT/configure.log) — constat enregistré (nvdec : attendu sans SDK L4T)."
  exit 0
fi
# MAKE_TARGETS est une liste de cibles make : split volontaire (SC2086).
# shellcheck disable=SC2086
make -j"$(nproc)" $MAKE_TARGETS
cp -f ./ffmpeg "$OUT/"
[ "$VARIANT" != "server-live" ] || cp -f ./ffprobe "$OUT/"
log "$VARIANT : $OUT/ffmpeg $( [ "$VARIANT" = "server-live" ] && printf '%s' "$OUT/ffprobe" )"
if [ "$VARIANT" = "mediacodec" ]; then
  {
    echo "# détection de l'encodeur h264_mediacodec via config.h / config_components.h du build :"
    # NB : les macros de codecs vivent dans config_components.h depuis FFmpeg 6 (constat on-device).
    if grep -q "CONFIG_H264_MEDIACODEC_ENCODER 1" "$SRC/config_components.h" "$SRC/config.h" 2>/dev/null; then
      echo "ENCODEUR PRÉSENT (CONFIG_H264_MEDIACODEC_ENCODER 1)."
      grep -E "MEDIACODEC_(DECOD|ENCOD)ER 1" "$SRC/config_components.h" 2>/dev/null
    else
      echo "ABSENT : aucune macro CONFIG_H264_MEDIACODEC_ENCODER=1 dans config.h ni config_components.h."
    fi
  } > "$OUT/encoders-presence.txt"
  log "presence encodeur : $(tr '\n' ' ' < "$OUT/encoders-presence.txt")"
fi
