#!/usr/bin/env bash
export LC_ALL=C LANG=C
# Construction des deux providers natifs TLS (tlsprovider/) et copie dans
# app-server/app/src/main/assets/tlsprovider/ + IL linux substituées (2 DLL
# System.Security.Cryptography.dll + System.Net.Security.dll depuis le pack
# runtime linux-arm64 local, même version que le pack android du bundle).
# Vérification finale : nm -D == union exacte des listes ImplMap (failover_gen.py
# régénère les fichiers par défaut).
#
# Version du pack NuGet : VERROU UNIQUE pack-version.txt (M3-revue) — symboles
# extraits et IL copiées proviennent de la même version, sans exception.
#
# Construction atomique (M4-revue) : artefacts compilés et verrouillés dans un
# répertoire privé (mktemp -d) ; assets/ n'est touché qu'après le verrou nm.
#
# Usage : ANDROID_NDK_ROOT=<ndk> ./build-tlsprovider.sh   (DRY_RUN=1 : verbeux, 0 : tout)
set -euo pipefail
DEPLOY_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SRC_DIR="$(cd "$(dirname "$0")" && pwd)"
ASSETS_DIR="$(cd "$DEPLOY_DIR/../../app-server/app/src/main/assets" && pwd)"
TS="${ANDROID_NDK_ROOT:?exporter ANDROID_NDK_ROOT}/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || { echo "toolchain Android 26 absente : $TS" >&2; exit 1; }
CERTIF_NM="$TS/llvm-nm"

# Version verrouillée — une seule source de vérité (extraite aussi par le script python)
TLS_PACK_VERSION="$(tr -d '[:space:]' < "$SRC_DIR/pack-version.txt")"
if [ -z "$TLS_PACK_VERSION" ]; then echo "pack-version.txt vide" >&2; exit 1; fi

PACKLINUX="$HOME/.nuget/packages/microsoft.netcore.app.runtime.linux-arm64/$TLS_PACK_VERSION"
PACKANDROID="$HOME/.nuget/packages/microsoft.netcore.app.runtime.android-arm64/$TLS_PACK_VERSION"
for pack in "$PACKLINUX" "$PACKANDROID"; do
    [ -d "$pack" ] || { echo "pack runtime $TLS_PACK_VERSION absent : $pack" >&2; exit 1; }
done

# Stage privé : rien n'arrive dans assets/ avant le verrou de couverture.
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

# (1) Listes exactes (ImplMap ECMA-335) — rejoués pour rester fidèle au pack 10.0.14
python3 "$SRC_DIR/extract-tls-symbols.py" > "$STAGE/extract.log" 2>&1
python3 "$SRC_DIR/failover_gen.py" > "$STAGE/pal.log" 2>&1

# (2) Compilation dans le stage privé (erreurs remontées par set -e)
"$TS/aarch64-linux-android26-clang" -shared -fPIC -O2 -fvisibility=hidden -Wno-unused-function \
  "$SRC_DIR/crypto_openssl_provider.c" "$SRC_DIR/der_x509_parser.c" "$SRC_DIR/crypto_failover.c" -o "$STAGE/libSystem.Security.Cryptography.Native.OpenSsl.so"
"$TS/aarch64-linux-android26-clang" -shared -fPIC -O2 -fvisibility=hidden -Wno-unused-function \
  "$SRC_DIR/net_security_provider.c" -o "$STAGE/libSystem.Net.Security.Native.so"

# (3) IL linux verrouillées sur LA version du pack android (bande TLS_PACK_VERSION)
cp "$PACKLINUX/runtimes/linux-arm64/lib/net10.0/System.Security.Cryptography.dll" "$STAGE/System.Security.Cryptography.dll"
cp "$PACKLINUX/runtimes/linux-arm64/lib/net10.0/System.Net.Security.dll" "$STAGE/System.Net.Security.dll"

# (4) Verrou de couverture : nm -D == union (openssl + netsecurity exactement)
couverts() {
  "$CERTIF_NM" -D "$1" | awk '$2=="T"{print $3}' | grep '^CryptoNative_\|^NetSecurityNative_' | sort -u
}

{ cat "$SRC_DIR/symbols-openssl.txt" "$SRC_DIR/symbols-netsecurity.txt"; } | sort -u > "$STAGE/union.txt"
{
  couverts "$STAGE/libSystem.Security.Cryptography.Native.OpenSsl.so"
  couverts "$STAGE/libSystem.Net.Security.Native.so"
} | sort -u > "$STAGE/couvert.txt"

if ! diff -q "$STAGE/union.txt" "$STAGE/couvert.txt" >/dev/null; then
  comm -23 "$STAGE/union.txt" "$STAGE/couvert.txt" > "$STAGE/manquants.txt"
  if [ -s "$STAGE/manquants.txt" ]; then
    echo "union incomplète — manquants :" >&2
    cat "$STAGE/manquants.txt" >&2
    exit 1
  fi
  comm -13 "$STAGE/union.txt" "$STAGE/couvert.txt" > "$STAGE/surplus.txt"
  [ -s "$STAGE/surplus.txt" ] && { echo "surplus (inoffensif, non verrouillé) :" >&2; cat "$STAGE/surplus.txt" >&2; }
fi
total="$(wc -l < "$STAGE/union.txt")"

# (5) Verrou passé : promotion atomique des artefacts vers assets/
mkdir -p "$ASSETS_DIR/tlsprovider"
mv "$STAGE/libSystem.Security.Cryptography.Native.OpenSsl.so" \
   "$STAGE/libSystem.Net.Security.Native.so" \
   "$STAGE/System.Security.Cryptography.dll" \
   "$STAGE/System.Net.Security.dll" \
   "$ASSETS_DIR/tlsprovider/"

echo "couverture TLS : $total / $total symbole(s) verrouillés [pack $TLS_PACK_VERSION]"
file "$ASSETS_DIR/tlsprovider/"*
