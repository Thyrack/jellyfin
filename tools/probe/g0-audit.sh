#!/usr/bin/env bash
# G0 — inventaire firmware/matériel de la Shield (sans root ; ADB réseau si actif).
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
ensure_data_dir
OUT="$DATA_DIR/g0-raw.txt"
if [ "$DRY_RUN" -eq 1 ]; then
  printf '### props\n### mémoire\n### /dev NVIDIA et vidéo\n### bibliothèques publiques (nvmedia)\n### dmesg (bonus)\n### lsusb (bonus)\n' > "$OUT"
  log 'audit simulé : sections vides attendues.'
  exit 0
fi
adb_target
: > "$OUT"
section() { # section <titre> <commande-shell> — tolérante aux échecs individuels
  { printf '\n### %s\n' "$1"; shell_cmd "$2" 2>&1; } >> "$OUT" || true
}
section 'props'                              'getprop | sort'
section 'mémoire'                            'head -n 30 /proc/meminfo'
section '/dev NVIDIA et vidéo'               'ls -l /dev | grep -E "video|nvhost|nvv4l2"'
section 'bibliothèques publiques (nvmedia)'  'grep -inE "nvmedia|nvmpeg|nvidia" /system/etc/public.libraries.txt'
section 'dmesg (bonus)'                      'dmesg 2>/dev/null | grep -iE "nvmedia|v4l2|nvenc|nvdec" | tail -n 100'
section 'lsusb (bonus)'                      'lsusb 2>/dev/null'
log "inventaire brut : $OUT"
