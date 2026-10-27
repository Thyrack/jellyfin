#!/usr/bin/env bash
# Construction du stub natif crypto v2 (BoringSSL : empreintes/HMAC/PBKDF2 réels,
# RNG /dev/urandom, échec propre ailleurs) et copie vers app-server/app/src/main/assets/cryptostub/.
# Embarqué dans l'APK ; le runtime Kotlin écrase le .so JVM-dépendant au démarrage (syncNatives).
# Vérification finale : nm -D du .so produit == union complète des listes (log détaillé).
set -euo pipefail
DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
PROBE_DIR="$(cd "$DEPLOY_DIR/../../tools/probe" && pwd)"
ASSETS_DIR="$(cd "$DEPLOY_DIR/../../app-server/app/src/main/assets" && pwd)"
# shellcheck disable=SC1090,SC1091
. "$PROBE_DIR/lib.sh"
if [ "${DRY_RUN:-0}" -eq 1 ]; then
  log 'dry-run : NDK clang build aarch64 (stub crypto v2 BoringSSL) + copie assets/cryptostub'
  exit 0
fi
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
SRC_DIR="$DEPLOY_DIR/cryptostub"
OUT="$ASSETS_DIR/cryptostub"
NM="$TS/llvm-nm"
mkdir -p "$OUT"
# Garde-fou de fraîcheur : liste des exports régénérée depuis le pack runtime courant
SO="$HOME/.nuget/packages/microsoft.netcore.app.runtime.android-arm64/10.0.14/runtimes/android-arm64/native/libSystem.Security.Cryptography.Native.Android.so"
[ -f "$SO" ] || die "runtime pack introuvable : $SO"
REGEN_DIR="$(mktemp -d)"
REGEN="$REGEN_DIR/exported-regen.txt"
"$NM" -D "$SO" 2>/dev/null | awk '$2=="T"{print $3}' | sort -u > "$REGEN"
diff -q "$REGEN" "$SRC_DIR/exported-symbols.txt" >/dev/null || log "exports originaux dérivés du runtime pack local : diffère du fichier versionné — vérifier si intentionnel"
# Symboles gérés manquants (imports P/Invoke ImplMap des DLL embarquées du bundle) :
# liste committée ; régénération : python3 cryptostub/extract-managed-symbols.py (cf. entête).
MANAGED="$SRC_DIR/managed-symbols.txt"
[ -f "$MANAGED" ] || die "managed-symbols.txt absent : exécuter cryptostub/extract-managed-symbols.py."
UNION_F="$(mktemp)"
PRODUIT_F="$(mktemp)"
trap 'rm -f "$UNION_F" "$PRODUIT_F"; rm -rf "$REGEN_DIR"' EXIT
# Union cible : exports du pack + imports gérés + CryptoNative_Pbkdf2 (prescrite hors union).
{ cat "$SRC_DIR/exported-symbols.txt" "$MANAGED"; printf '%s\n' 'CryptoNative_Pbkdf2'; } | sort -u > "$UNION_F"
"$TS/aarch64-linux-android26-clang" -shared -fPIC -O2 -Wall \
  "$SRC_DIR/crypto_android_stub.c" -o "$OUT/libSystem.Security.Cryptography.Native.Android.so"
"$NM" -D "$OUT/libSystem.Security.Cryptography.Native.Android.so" | awk '$2=="T"{print $3}' | sort -u > "$PRODUIT_F"
MANQUANTS="$(comm -23 "$UNION_F" "$PRODUIT_F")"
SURPLUS="$(comm -13 "$UNION_F" "$PRODUIT_F")"
if [ -n "$MANQUANTS" ]; then
  log "$MANQUANTS"
  die "union incomplète : $(printf '%s\n' "$MANQUANTS" | wc -l) symbole(s) manquant(s)."
fi
[ -z "$SURPLUS" ] || log "surplus exporté (inoffensif) : $SURPLUS"
log "union couverte : $("$NM" -D "$OUT/libSystem.Security.Cryptography.Native.Android.so" | grep -c ' T ') exportés == $(wc -l < "$UNION_F") attendus (exports originaux : $(wc -l < "$SRC_DIR/exported-symbols.txt"), tous couverts)."
file "$OUT/libSystem.Security.Cryptography.Native.Android.so"
log "stub construit : $OUT/libSystem.Security.Cryptography.Native.Android.so"
