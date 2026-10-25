#!/usr/bin/env bash
# Connexion ADB réseau vers la Shield ; mémorise l'IP dans device.env (ignoré par git).
set -euo pipefail
JF_LIB_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck disable=SC1090,SC1091
. "$JF_LIB_DIR/lib.sh"
JF_SHIELD_DIR="$(cd "$PROBE_DIR/../../docs/jellyfin-shield" && pwd)"
ENV_FILE="$JF_SHIELD_DIR/device.env"
# shellcheck disable=SC1090
if [ -f "$ENV_FILE" ]; then . "$ENV_FILE"; fi
adb_target
if [ "$DRY_RUN" -eq 0 ]; then
  printf 'JF_SHIELD_IP=%s\n' "${JF_SHIELD_IP:?}" > "$ENV_FILE"
  log "IP mémorisée dans $ENV_FILE."
fi
