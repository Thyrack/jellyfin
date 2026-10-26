#!/usr/bin/env bash
# Connexion ADB réseau vers la Shield ; mémorise l'IP dans device.env (ignoré par git).
set -euo pipefail
JF_LIB_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck disable=SC1090,SC1091
. "$JF_LIB_DIR/lib.sh"
JF_SHIELD_DIR="$(cd "$PROBE_DIR/../../docs/jellyfin-shield" && pwd)"
ENV_FILE="${JF_ENV_FILE:-$JF_SHIELD_DIR/device.env}" # JF_ENV_FILE : override pour les tests (isolation de device.env)
# priorité à l'environnement : device.env n'est sourcé que si JF_SHIELD_IP n'est pas déjà défini
if [ -z "${JF_SHIELD_IP:-}" ] && [ -f "$ENV_FILE" ]; then
  # shellcheck disable=SC1090
  . "$ENV_FILE"
fi
adb_target
# divergence env/device.env : l'environnement est prioritaire ; l'écart est signalé avant écrasement
if [ -f "$ENV_FILE" ]; then
  env_file_ip="$(grep -oE 'JF_SHIELD_IP=[^[:space:]]+' "$ENV_FILE" | head -n 1 | cut -d= -f2 || true)"
  if [ -n "${JF_SHIELD_IP:-}" ] && [ -n "$env_file_ip" ] && [ "$env_file_ip" != "${JF_SHIELD_IP:-}" ]; then
    warn "divergence env/device.env : JF_SHIELD_IP=${JF_SHIELD_IP:-} (environnement) vs $env_file_ip (device.env) — l'environnement est prioritaire."
  fi
fi
if [ "$DRY_RUN" -eq 0 ]; then
  printf 'JF_SHIELD_IP=%s\n' "${JF_SHIELD_IP:?}" > "$ENV_FILE"
  log "IP mémorisée dans $ENV_FILE."
fi
