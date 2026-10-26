#!/usr/bin/env bash
# vérification de stabilité 24 h : état des process + santé HTTP ; <fichier-résultats> attendu en $1.
set -euo pipefail
DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
# défaut dry-run posé AVANT le sourcing : lib.sh fixe DRY_RUN=0 si la variable est absente
if [ -z "${DRY_RUN:-}" ]; then DRY_RUN=1; fi
# shellcheck disable=SC1090,SC1091
. "$DEPLOY_DIR/../../tools/probe/lib.sh"
RESULTS_FILE="${1:?fichier résultats (gitignored) attendu en argument 1}"
if [ "${DRY_RUN:-1}" -eq 1 ]; then
  log 'dry-run : collecte uptime + /health du serveur.'; exit 0
fi
adb_target
{
  echo "## process Android (ps -A | grep app/serveur) ($(date +%F_%T))"
  shell_cmd 'ps -A | grep -E "org\\.jellyfin\\.shieldserver|jellyfin" || echo "(processus absent — constat de stabilité)"'
  echo "## santé http : $(curl -s -o /dev/null -w '%{http_code}' "http://${JF_SHIELD_IP:?}:8096/health")"
} > "$RESULTS_FILE"
log "collecte : $RESULTS_FILE"
