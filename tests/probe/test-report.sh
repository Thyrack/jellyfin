#!/usr/bin/env bash
# Tests du rapport consolidé G2 — exécution réelle de report.sh (aucun appareil requis).
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }
# nettoyage garanti : le rapport généré par les tests ne doit pas polluer l'arbre
trap 'rm -f ../../docs/jellyfin-shield/probe-report.md' EXIT

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
