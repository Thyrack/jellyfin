#!/usr/bin/env bash
# tests du stub crypto v2 — exécutables sans build réel (le build réel est verrouillé par
# les vérifications nm -D internes à build-cryptostub.sh).
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }
t "shellcheck propre" bash -c 'shellcheck ../../deployment/apk-android/build-cryptostub.sh'
t "dry-run : aucune IO disque" bash -c '
  DRY_RUN=1 bash ../../deployment/apk-android/build-cryptostub.sh | grep -q "stub crypto v2"'
t "source du stub présent" bash -c 'test -f ../../deployment/apk-android/cryptostub/crypto_android_stub.c'
t "union des symboles gérés committée" bash -c '
  test -s ../../deployment/apk-android/cryptostub/managed-symbols.txt && \
  grep -q "^CryptoNative_GetMaxMdSize$" ../../deployment/apk-android/cryptostub/managed-symbols.txt'
t "aucun symbole géré de la liste absent du C" bash -c '
  while read -r s; do
    grep -q "$s(" ../../deployment/apk-android/cryptostub/crypto_android_stub.c || exit 1
  done < ../../deployment/apk-android/cryptostub/managed-symbols.txt'
exit $fail
