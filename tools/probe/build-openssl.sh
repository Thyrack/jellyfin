#!/usr/bin/env bash
# Construction d'OpenSSL statique aarch64 (bionic, API 26) pour le ffmpeg « server-live ».
# Usage : build-openssl.sh  [défaut : version OPENSSL_VER ci-dessous]
# Sortie : tools/probe/out/openssl/{lib/libssl.a,lib/libcrypto.a,include/}
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
OPENSSL_VER="${OPENSSL_VER:-3.3.2}"
if [ "$DRY_RUN" -eq 1 ]; then
  log "dry-run : fetch openssl $OPENSSL_VER + Configure android-arm64 (statique) + make."
  exit 0
fi
require_cmd make perl
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
OUT="$PROBE_DIR/out/openssl"; mkdir -p "$OUT"
SRC="$PROBE_DIR/build/openssl-$OPENSSL_VER"
if [ ! -d "$SRC" ]; then
  log "fetch openssl $OPENSSL_VER…"
  mkdir -p "$PROBE_DIR/build"
  curl -sL "https://github.com/openssl/openssl/releases/download/openssl-$OPENSSL_VER/openssl-$OPENSSL_VER.tar.gz" \
    | tar -xzf - -C "$PROBE_DIR/build"
fi
cd "$SRC"
# rebuild propre si la config précédente est présente
make distclean 2>/dev/null || true
export PATH="$TS:$PATH"
./Configure android-arm64 -D__ANDROID_API__=26 \
  no-shared no-tests no-docs \
  --prefix="$OUT" --openssldir="$OUT" \
  > "$OUT/configure.log" 2>&1
make -j"$(nproc)" build_sw > "$OUT/build.log" 2>&1
make install_sw install_ssldirs > "$OUT/install.log" 2>&1
[ -f "$OUT/lib/libssl.a" ] && [ -f "$OUT/lib/libcrypto.a" ] \
  || die "libs statiques absentes après install (cf. $OUT/install.log)."
log "openssl statique OK : $OUT/lib/libssl.a + libcrypto.a"
