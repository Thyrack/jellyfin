#!/usr/bin/env bash
# Outils communs des sondes Jellyfin/Shield — dry-runnable sans appareil.
set -euo pipefail

PROBE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_DIR="$PROBE_DIR/../../docs/jellyfin-shield/probe-data"
DRY_RUN="${DRY_RUN:-0}"

log()  { printf '[probe] %s\n' "$*"; }
warn() { printf '[probe] ATTENTION : %s\n' "$*" >&2; }
die()  { printf '[probe] ERREUR : %s\n' "$*" >&2; exit 1; }

require_cmd() {
  local missing="" c
  for c in "$@"; do
    command -v "$c" >/dev/null 2>&1 || missing="$missing$c "
  done
  [ -z "$missing" ] || die "commandes requises introuvables : $missing"
}

ensure_data_dir() { mkdir -p "$DATA_DIR"; }

run_adb() {
  if [ "$DRY_RUN" -eq 1 ]; then printf '[dry-run] adb %s\n' "$*" >&2; return 0; fi
  adb "$@"
}
shell_cmd() { run_adb shell "$1"; }

adb_target() {
  require_cmd adb
  local ip="${JF_SHIELD_IP:-}"
  [ -n "$ip" ] || die "IP de la Shield manquante : export JF_SHIELD_IP=x.x.x.x (cf. docs/jellyfin-shield/README.md)."
  if [ "$DRY_RUN" -eq 1 ]; then return 0; fi
  local target="$ip:5555"
  run_adb connect "$target" >/dev/null
  run_adb devices | tail -n +2 | grep -q "$target"'[[:space:]]*device[[:space:]]*$' \
    || die "$target non détecté ni autorisé (acceptez la boîte de dialogue sur la TV)."
}

# parse_progress_fps <fichier> — dernière valeur « fps= » d'une sortie ffmpeg -stats ; renvoie '0' si absente
parse_progress_fps() {
  LC_ALL=C awk '{
    i = match($0, /fps=[0-9]*\.?[0-9]*/)
    if (i) { v = substr($0, i + 4, RLENGTH - 4); gsub(/\r/, "", v); last = v + 0 }
  } END { print last + 0 }' "$1"
}

# parse_progress_speed <fichier> — dernière valeur « speed= » (x retiré) ; renvoie '0' si absente
parse_progress_speed() {
  LC_ALL=C awk '{
    i = match($0, /speed=[0-9.]+/)
    if (i) { v = substr($0, i + 6, RLENGTH - 6); gsub(/[^0-9.]/, "", v); last = v + 0 }
  } END { print last + 0 }' "$1"
}

if [ "${1:-}" = "--selftest" ]; then
  log "[selftest] DATA_DIR=$DATA_DIR"
  log "[selftest] require_cmd : cas vide géré"
  log "[selftest] adb_target en dry-run : retour immédiat après contrôle IP"
  exit 0
fi
