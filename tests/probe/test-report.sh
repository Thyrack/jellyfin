#!/usr/bin/env bash
# Tests du rapport consolidé G2 — exécution réelle de report.sh (aucun appareil requis).
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }

t "rapport à 4 marches" bash -c '
  bash ../../tools/probe/report.sh >/dev/null
  grep -c "^| Marche : " ../../docs/jellyfin-shield/probe-report.md | grep -qx "4"'
t "sourcing sans exécuter main (garde \${BASH_SOURCE[0]} = \$0)" bash -c '
  source ../../tools/probe/report.sh && type main >/dev/null'
t "shellcheck propre" bash -c 'shellcheck ../../tools/probe/report.sh'

exit $fail
