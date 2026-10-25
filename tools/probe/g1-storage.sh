#!/usr/bin/env bash
# G1 — lecture du disque USB de la Shield via ffprobe statique ; test RW informatif.
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
if [ "$DRY_RUN" -eq 1 ]; then
  ensure_data_dir
  printf '### volume USB\n### RW\n### lecture ffprobe\n' > "$DATA_DIR/g1-storage.txt"
  log 'sonde simulée : sections vides.'
  exit 0
fi
ensure_data_dir
FFPROBE="$PROBE_DIR/out/ffprobe/ffprobe"
[ -x "$FFPROBE" ] || die "ffprobe manquant : exécuter tools/probe/build-ffprobe.sh d'abord."
OUT="$DATA_DIR/g1-storage.txt"
: > "$OUT" # troncature en mode réel (le dry-run a déjà écrit le stub des 3 sections)
adb_target
run_adb push "$FFPROBE" /data/local/tmp/jf-ffprobe
shell_cmd 'chmod 744 /data/local/tmp/jf-ffprobe'
{
  printf '\n### volume USB\n'
  shell_cmd 'ls /storage 2>/dev/null'
  printf '\n### RW\n'
  # payload exécuté sur l'appareil : variables volontairement non développées côté hôte
  # shellcheck disable=SC2016
  shell_cmd 'for vol in /storage/*; do case "$vol" in */emulated*|*/self) continue;; esac; test -d "$vol" || continue; if touch "$vol/.jf_rw_test" 2>/dev/null; then echo "$vol : RW"; rm -f "$vol/.jf_rw_test"; else echo "$vol : R/O (attendu)"; fi; done'
  printf '\n### lecture ffprobe\n'
  # shellcheck disable=SC2016
  shell_cmd 'media=$(find /storage -maxdepth 3 -type f \( -name "*.mkv" -o -name "*.mp4" -o -name "*.m4a" -o -name "*.flac" -o -name "*.mov" -o -name "*.flv" \) 2>/dev/null | head -n 1); if [ -n "$media" ]; then echo "média : $media"; /data/local/tmp/jf-ffprobe -v panic -show_format -show_entries stream=codec_name,width,height,sample_rate "$media" 2>&1; else echo "aucun média trouvé : déposer 1 à 2 fichiers test sur la clé (cf. README)."; fi'
} >> "$OUT" 2>&1 || true
log "sonde G1 : $OUT"
