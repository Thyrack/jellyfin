#!/usr/bin/env bash
# installation et vérification du serveur embarqué sur la Shield (JF_SHIELD_IP requis).
set -euo pipefail
DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
# défaut dry-run posé AVANT le sourcing : lib.sh fixe DRY_RUN=0 si la variable est absente
if [ -z "${DRY_RUN:-}" ]; then DRY_RUN=1; fi
# shellcheck disable=SC1090,SC1091
. "$DEPLOY_DIR/../../tools/probe/lib.sh"   # réutilisation de adb_target/run_adb/log de la sonde
if [ "${DRY_RUN:-1}" -eq 1 ]; then
  log 'dry-run : adb install, force-stop, am start MainActivity (démarrage du service par l app), curl /health PC.'
  exit 0
fi
adb_target
run_adb install -r "$(cd "$DEPLOY_DIR/../../app-server" && pwd)/app/build/outputs/apk/debug/app-debug.apk"
run_adb shell am force-stop org.jellyfin.shieldserver
run_adb shell am start -n org.jellyfin.shieldserver/.MainActivity
log "IP de la Shield : ${JF_SHIELD_IP:?}"
sleep 20
log "santé : $(curl -s -o /dev/null -w '%{http_code}' "http://${JF_SHIELD_IP:?}:8096/health" || echo indéterminée)"
log "ouvrir http://$JF_SHIELD_IP:8096 dans le navigateur PC pour l'assistant (10 fichiers test bibliothèque)."
