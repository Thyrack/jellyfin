#!/usr/bin/env bash
# Tests du rapport consolidé G2 — exécution réelle de report.sh (aucun appareil requis).
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }
# nettoyage : un rapport consolidé préexistant (le committé) est mis de côté au lancement puis
# RESTAURÉ à la sortie — la suite ne doit jamais détruire le rapport committé. À défaut de rapport
# préexistant, seul un rapport template généré par les tests (contenant <à compléter>) est supprimé.
REPORT=../../docs/jellyfin-shield/probe-report.md
BACKUP=$(mktemp)
if [ -f "$REPORT" ]; then mv "$REPORT" "$BACKUP"; fi
trap 'if [ -s "$BACKUP" ]; then mv "$BACKUP" "$REPORT"; elif [ -f "$REPORT" ] && grep -q "<à compléter>" "$REPORT"; then rm -f "$REPORT"; fi; rm -f "$BACKUP"' EXIT

t "rapport à 4 marches" bash -c '
  bash ../../tools/probe/report.sh --force >/dev/null
  grep -c "^| Marche : " ../../docs/jellyfin-shield/probe-report.md | grep -qx "4"'
t "refus sans --force (rapport existant)" bash -c '
  ! bash ../../tools/probe/report.sh >/dev/null 2>&1'
t "refus --force sur rapport complété" bash -c '
  printf "rapport déjà complété sans placeholder\n" > ../../docs/jellyfin-shield/probe-report.md
  ! bash ../../tools/probe/report.sh --force >/dev/null 2>&1'
t "sourcing sans exécuter main (garde \${BASH_SOURCE[0]} = \$0)" bash -c '
  source ../../tools/probe/report.sh && type main >/dev/null'
t "shellcheck propre" bash -c 'shellcheck ../../tools/probe/report.sh'

exit $fail
