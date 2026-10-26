#!/usr/bin/env bash
# Assemblage de probe-report.md ; tolère les fichiers de données absents.
set -euo pipefail
# shellcheck disable=SC1090,SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"
ensure_data_dir
REPORT="$PROBE_DIR/../../docs/jellyfin-shield/probe-report.md"

main() {
  # garde anti-écrasement : TOUT rapport existant (complétion partielle comprise) est protégé sans --force
  local force="${1:-}"
  if [ -f "$REPORT" ] && [ "$force" != "--force" ]; then
    die "probe-report.md existe déjà (complétion partielle possible) — régénération refusée sans '--force'."
  fi
  # garde « sans <à compléter> » : même avec --force, un rapport déjà complété n'est pas écrasé
  if [ -f "$REPORT" ] && ! grep -q "<à compléter>" "$REPORT"; then
    die "probe-report.md est déjà complété : re-génération refusée (supprimez le fichier pour repartir de zéro)."
  fi
  {
    printf '# Rapport de sonde Jellyfin sur NVIDIA Shield — %s\n\n' "$(date +%F)"
    printf '## Données brutes collectées\n\n'
    local f
    for f in g0-raw.txt g0-dlopen-shell.txt g0-app-namespace.txt g1-storage.txt g2-bench.tsv; do
      if [ -s "$DATA_DIR/$f" ]; then
        printf '### %s\n\n```text\n' "$f"
        cat "$DATA_DIR/$f"
        printf '\n```\n\n'
      fi
    done
    printf "## Verdicts par marche (à compléter par l'exécutant, à valider par le propriétaire : portes G0/G1/G2)\n\n"
    printf '| Marche : V4L2-M2M | verdict : <à compléter> | fps : <à compléter> | observations : <à compléter> |\n'
    printf '| Marche : MediaCodec | verdict : <à compléter> | fps : <à compléter> | observations : encodeur h264_mediacodec présent/absent |\n'
    printf '| Marche : NVMedia décodage | verdict : <à compléter> | fps : — | observations : dlopen libnvmpeg11 OK/ÉCHEC |\n'
    printf '| Marche : NVMedia encodage (option G5) | verdict : — | fps : — | observations : chantier séparé (spec G5) |\n'
    printf '\nGénéré par tools/probe/report.sh — les champs « à compléter » du corps sont remplacés à la main après exécution ; tout « à compléter » résiduel dans le CORPS bloque la régénération.\n'
  } > "$REPORT"
  log "rapport : $REPORT"
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main "$@"; fi
