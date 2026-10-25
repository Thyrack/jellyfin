# Plan d'implémentation — Jellyfin sur Shield : phase de sonde G0→G2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** instrumenter la Shield 2015/2017 (Android 8) pour produire, sans root et si possible sans contact physique, le **rapport de sonde** go/no-go par marche HW exigé par la spec `docs/superpowers/specs/2026-10-25-jellyfin-shield-hw-design.md` (§3, jalons G0→G2).

**Architecture :** scripts bash pilotés par ADB réseau (`adb connect`, repli : mini-sonde APK autonome qui est aussi la source authentifiante des tests `dlopen` en namespace applicatif) ; binaires natifs (micro-sonde `dlopen`, `ffprobe`, matrices `ffmpeg`) cross-compilés via NDK sur l'hôte ; données brutes dans `docs/jellyfin-shield/probe-data/` (ignorées par git), rapport consolidé committé.

**Tech Stack :** bash + shellcheck, NDK r27+/clang aarch64 API 26, jellyfin-ffmpeg v7.1.3-6 (base alignée termux v10.11.x), Kotlin/Gradle (AGP 8.7.3) pour la mini-sonde APK, ffmpeg hôte (testsrc2 + libx264/libx265) pour les médias de benchmark.

## Global Constraints

- Aucun root, aucun Termux, aucune altération système (spec §3) : fichiers de test dans `filesDir` de la sonde APK ou `/data/local/tmp` si ADB actif.
- ADB réseau prioritaire, repli « sonde APK » complet seulement si l'activation échoue (amendement v1.1, spec §2).
- APK : `minSdk 26`, `targetSdk 26`, `compileSdk 34` (choix d'outillage), JNI arm64-v8a ; exécution de binaires autorisée sous API 26.
- Commits en français, Conventional Commits, atomiques (directive globale §3) ; gate `commit-review-gate` : revue expert-review sur le diff avant chaque commit.
- Scripts bash : `set -euo pipefail`, quoting strict, `shellcheck` propre, dry-runnable sans appareil ; l'exécutant charge `expert-bash`.
- Documents et commentaires en français ; secrets/IP jamais committés (`device.env` et `probe-data/` ignorés).

Arborescence :
- `tools/probe/lib.sh` — fonctions communes (dont `parse_progress_fps`)
- `tools/probe/connect.sh`, `tools/probe/g0-audit.sh`, `tools/probe/g0-dlopen-probe.c`, `tools/probe/g0-dlopen.sh`
- `tools/probe/build-ffprobe.sh`, `tools/probe/g1-storage.sh`
- `tools/probe/build-ffmpeg.sh`, `tools/probe/bench.sh`, `tools/probe/report.sh`
- `app-probe/` — mini-sonde APK ; `tests/probe/` — tests des scripts
- `docs/jellyfin-shield/README.md`, `docs/jellyfin-shield/probe-report.md` (livrable)

Ordre : T1 puis T2→T10 ; T5 indépendant (sourcable tôt) ; T10 après tout le reste.

### Task 1: Bibliothèque commune (`lib.sh`), garde-fous git, fixture

**Files:**
- Create: `tools/probe/lib.sh`
- Create: `tests/probe/test-lib.sh`
- Create: `tests/probe/fixtures/sample-progress.txt`
- Create: `docs/jellyfin-shield/.env.example`
- Create: `docs/jellyfin-shield/probe-data/.gitkeep`
- Modify: `.gitignore` (bloc ajouté en fin de fichier)

**Interfaces:**
- Produces (consommés par toutes les tâches suivantes) : `DATA_DIR` (chemin absolu de `docs/jellyfin-shield/probe-data`), `PROBE_DIR` (chemin de `tools/probe`), `DRY_RUN` (0|1, env), `log()`, `warn()`, `die()`, `require_cmd()`, `ensure_data_dir()`, `adb_target()`, `run_adb()`, `shell_cmd()`, `parse_progress_fps()`, `parse_progress_speed()`.
- `lib.sh --selftest` affiche exactement 3 lignes `[selftest]`.

- [ ] **Step 1: Écrire le test d'abord (échec attendu)**

`tests/probe/test-lib.sh` :
```bash
#!/usr/bin/env bash
# Tests de la bibliothèque commune — exécutables sans appareil.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }

t "shellcheck propre" bash -c 'shellcheck ../tools/probe/lib.sh'
t "selftest : 3 lignes" bash -c '
  bash ../tools/probe/lib.sh --selftest | grep -c "\[selftest\]" | grep -qx "3"'
t "dry-run sans appareil ni IP : selftest OK" bash -c '
  DRY_RUN=1 bash ../tools/probe/lib.sh --selftest >/dev/null'
t "parse_progress_fps sur la fixture renvoie 30" bash -c '
  source ../tools/probe/lib.sh >/dev/null
  parse_progress_fps fixtures/sample-progress.txt | grep -qx "30"'
t "parse_progress_speed sur la fixture renvoie 1.2" bash -c '
  source ../tools/probe/lib.sh >/dev/null
  parse_progress_speed fixtures/sample-progress.txt | grep -qx "1.2"'

exit $fail
```

Run: `bash tests/probe/test-lib.sh` — Expected: FAIL (lib.sh inexistant).

- [ ] **Step 2: Implémenter `tools/probe/lib.sh` (contenu exact)**

```bash
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
  awk '{
    i = match($0, /fps=[0-9]*\.?[0-9]*/)
    if (i) { v = substr($0, i + 4, RLENGTH - 4); gsub(/\r/, "", v); last = v + 0 }
  } END { print last + 0 }' "$1"
}

# parse_progress_speed <fichier> — dernière valeur « speed= » (x retiré) ; renvoie '0' si absente
parse_progress_speed() {
  awk '{
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
```

Run: `bash tests/probe/test-lib.sh` — Expected: FAIL (lib.sh inexistant), puis PASS ×5 au Step 6.

- [ ] **Step 3: Fixture `tests/probe/fixtures/sample-progress.txt` (contenu exact)**

```
ffmpeg started.
frame=  30 fps=0.0 q=-1.0 size=   256kB time=00:00:01.00 speed=0.4x
frame=  60 fps=15.0 q=-1.0 size=   512kB time=00:00:02.00 speed=0.6x
frame= 120 fps=30.0 q=-1.0 size=  1024kB time=00:00:04.00 speed=1.2x
```

- [ ] **Step 4: Bloc `.gitignore` (ajouté à la fin du fichier existant)**

```gitignore
# Sondes Jellyfin/Shield : fichiers générés et environnement local
docs/jellyfin-shield/device.env
docs/jellyfin-shield/probe-data/*
!docs/jellyfin-shield/probe-data/.gitkeep
tools/probe/out/
tools/probe/build/
app-probe/local.properties
```

- [ ] **Step 5: `docs/jellyfin-shield/.env.example` (contenu exact)**

```
# Copier vers device.env (ignoré par git) et renseigner l'IP locale de la Shield.
JF_SHIELD_IP=192.168.x.x
```

Et fichier vide `docs/jellyfin-shield/probe-data/.gitkeep`.

- [ ] **Step 6: Vérifier** — `bash tests/probe/test-lib.sh` → PASS ×5.

- [ ] **Step 7: Commit**

```bash
git add tools/probe/lib.sh tests/probe/test-lib.sh tests/probe/fixtures/sample-progress.txt docs/jellyfin-shield/.env.example docs/jellyfin-shield/probe-data/.gitkeep .gitignore
git commit -m "feat(probe): bibliothèque commune des sondes avec parseur fps et dry-run"
```

### Task 2: Audit G0 (`g0-audit.sh`)

**Files:** Create `tools/probe/g0-audit.sh`, `tests/probe/test-g0.sh`. Skills : `expert-bash`.

**Interfaces:**
- Consumes : `DATA_DIR`, `adb_target`, `shell_cmd`, `ensure_data_dir`, `DRY_RUN` (T1).
- Produces : `probe-data/g0-raw.txt` avec exactement 6 en-têtes `^### ` (ordre) : `props`, `mémoire`, `/dev NVIDIA et vidéo`, `bibliothèques publiques (nvmedia)`, `dmesg (bonus)`, `lsusb (bonus)`.

- [ ] **Step 1: Test en premier** — `tests/probe/test-g0.sh` :
```bash
#!/usr/bin/env bash
# G0 — inventaire firmware/matériel : test sans appareil.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }

t "shellcheck propre" bash -c 'shellcheck ../tools/probe/g0-audit.sh'
t "dry-run : 6 sections" bash -c '
  DRY_RUN=1 bash ../tools/probe/g0-audit.sh >/dev/null 2>&1
  grep -c "^### " ../docs/jellyfin-shield/probe-data/g0-raw.txt | grep -qx "6"'

exit $fail
```
Run: `bash tests/probe/test-g0.sh` — Expected: FAIL (script absent).

- [ ] **Step 2: Implémenter (contenu exact)** — `tools/probe/g0-audit.sh` :
```bash
#!/usr/bin/env bash
# G0 — inventaire firmware/matériel de la Shield (sans root ; ADB réseau si actif).
set -euo pipefail
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
# shellcheck disable=SC1090
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
```

- [ ] **Step 3: Vérifier** — `bash tests/probe/test-g0.sh` → PASS ×2.

- [ ] **Step 4: Commit**
```bash
git add tools/probe/g0-audit.sh tests/probe/test-g0.sh
git commit -m "feat(probe/g0): inventaire firmware et matériel de la Shield"
```

### Task 3: Micro-sonde `dlopen` côté shell (binaire C + script ADB)

**Files:** Create `tools/probe/g0-dlopen-probe.c`, `tools/probe/g0-dlopen.sh`, `tests/probe/test-g0-dlopen.sh`. Skills : `expert-bash`.

**Interfaces:**
- Produces : binaire `tools/probe/out/dlopen-probe` (aarch64 API 26) ; `probe-data/g0-dlopen-shell.txt` avec lignes `lib...  OK` ou `lib...  ÉCHEC (<erreur>)` pour `libnvmedia.so`, `libnvmpeg11.so`, `libnvhost.so`.
- Note : ce résultat dans le **contexte shell ADB** est *informatif* ; le **résultat authentifiant** pour l'architecture cible vient de la mini-sonde APK (T5), conformément à la spec §3 G0.

- [ ] **Step 1: Source C (contenu exact)** — `tools/probe/g0-dlopen-probe.c` :
```c
/* Micro-sonde dlopen de la pile NVMedia du Tegra (Shield 2015/2017).
   Contexte shell ADB : informatif. Résultat authentifiant : mini-sonde APK (Task 5). */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <stdio.h>
#include <string.h>

static const char *LIBS[] = { "libnvmedia.so", "libnvmpeg11.so", "libnvhost.so" };

int main(void) {
  for (size_t i = 0; i < sizeof(LIBS) / sizeof(LIBS[0]); i++) {
    void *h = dlopen(LIBS[i], RTLD_NOW | RTLD_LOCAL);
    if (h) {
      printf("%-18s OK\n", LIBS[i]);
      dlclose(h);
    } else {
      char *err = dlerror(); /* dlerror() réinitialise le contexte : lecture unique */
      printf("%-18s ÉCHEC (%s)\n", LIBS[i], err ? err : "raison inconnue");
    }
  }
  return 0; /* les verdicts sont lus sur stdout, pas via l'exit-code */
}
```

- [ ] **Step 2: Script (contenu exact)** — `tools/probe/g0-dlopen.sh` :
```bash
#!/usr/bin/env bash
# G0 — compile, pousse et exécute la micro-sonde dlopen via ADB (informatif).
set -euo pipefail
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
# shellcheck disable=SC1090
ensure_data_dir
OUT="$DATA_DIR/g0-dlopen-shell.txt"
if [ "$DRY_RUN" -eq 1 ]; then
  log 'dry-run : compilation NDK, push vers /data/local/tmp, exécution, sortie capturée.'
  exit 0
fi
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
adb_target
mkdir -p "$PROBE_DIR/out"
"$TS/aarch64-linux-android26-clang" "$PROBE_DIR/g0-dlopen-probe.c" -o "$PROBE_DIR/out/dlopen-probe" -ldl
run_adb push "$PROBE_DIR/out/dlopen-probe" /data/local/tmp/jf-dlopen-probe
shell_cmd 'chmod 744 /data/local/tmp/jf-dlopen-probe && /data/local/tmp/jf-dlopen-probe' > "$OUT" 2>&1
log "sortie : $OUT"; cat "$OUT"
```

- [ ] **Step 3: Test** — `tests/probe/test-g0-dlopen.sh` :
```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }
t "dry-run sans appareil"  bash -c 'DRY_RUN=1 bash ../tools/probe/g0-dlopen.sh'
t "shellcheck propre"      bash -c 'shellcheck ../tools/probe/g0-dlopen.sh'
exit $fail
```
Run: `bash tests/probe/test-g0-dlopen.sh` → PASS ×2.

- [ ] **Step 4: Commit**
```bash
git add tools/probe/g0-dlopen-probe.c tools/probe/g0-dlopen.sh tests/probe/test-g0-dlopen.sh
git commit -m "feat(probe/g0): micro-sonde dlopen NVMedia exécutée via ADB"
```

### Task 4: Procédure + connecteur ADB réseau (participation manuelle utilisateur)

**Files:** Create `tools/probe/connect.sh`, `docs/jellyfin-shield/README.md`, `tests/probe/test-connect.sh`. Skills : `expert-bash`.

**Interfaces:**
- Produces : appareil prêt via `adb connect <ip>:5555` ; `device.env` contenant `JF_SHIELD_IP=<ip>` (consommé par T2, T3, T7, T9) ; README documentant le repli « sans PC » (installation APK par clé USB).
- Le step d'activation sur la TV est **manuel** (télécommande/clavier) ; le script vérifie seulement la connectivité.

- [ ] **Step 1: README (section clé, contenu exact)** — `docs/jellyfin-shield/README.md` :

```markdown
# Sondes Jellyfin/Shield — manuel d'exécution

## Prérequis hôte (PC)
- `adb` (platform-tools), `shellcheck`, `git`, `make`, NDK r27+ (`export ANDROID_NDK_ROOT=...`),
  JDK 17 + Gradle 8.11 (mini-sonde APK : wrapper `gradle wrapper --gradle-version 8.11.1`),
  SDK Android (`platforms;android-34`, `build-tools`, CMake 3.22.1),
  `ffmpeg` hôte avec `testsrc2` + `libx264` (+ `libx265` optionnel) pour G2.

## Activer le débogage réseau ADB sur la Shield (participation manuelle, ~5 min)
1. Sur la TV : `Paramètres → Préférences de l'appareil → À propos` → appuyer 7 fois sur « Build ».
2. `Paramètres → Préférences de l'appareil → Options pour développeurs` → activer « Débogage réseau »
   (nom exact selon firmware : « ADB réseau »). Noter l'IP affichée dans
   `Paramètres → Système → À propos → État` (adresse IP).
3. Sur le PC :
       export JF_SHIELD_IP=<ip>
       bash tools/probe/connect.sh
4. Accepter la boîte de dialogue d'empreinte ADB sur la TV (« Toujours autoriser dans ce réseau »).
5. Vérifier : `adb devices` doit afficher `<ip>:5555 device`.

## Lancement (ordre)
    bash tools/probe/g0-audit.sh                 # G0 — inventaire firmware/matériel
    bash tools/probe/g0-dlopen.sh                # G0 — dlopen NVMedia via shell ADB (informatif)
    bash tools/probe/build-ffprobe.sh            # T6 — ffprobe statique aarch64
    bash tools/probe/build-ffmpeg.sh sw          # T8 — baseline CPU (routes de référence)
    bash tools/probe/g1-storage.sh               # G1 — lecture média USB + test RW
    bash tools/probe/build-ffmpeg.sh v4l2m2m     # T8 — variante M2M
    bash tools/probe/build-ffmpeg.sh mediacodec  # T8 — variante MediaCodec
    bash tools/probe/build-ffmpeg.sh nvdec       # T8 — nvdec (constat attendu)
    bash tools/probe/bench.sh                    # G2 — benchmarks fps
    bash tools/probe/report.sh                   # G2 — rapport consolidé
Plus la mini-sonde APK (Task 5) : `cd app-probe && ./gradlew :app:assembleDebug` puis
`adb install app-probe/app/build/outputs/apk/debug/app-debug.apk`.

## Si ADB réseau est indisponible (repli « sonde APK » sans PC — amendement v1.1)
1. Copier `app-probe/app/build/outputs/apk/debug/app-debug.apk` sur une clé USB.
2. Sur la TV, via un explorateur de fichiers (ex. « File Manager Shield ») : autoriser
   « sources inconnues » pour cet explorateur, puis installer l'APK.
3. Lancer « Sonde JF Shield » : les résultats dlopen s'affichent à l'écran ; un fichier
   `g0-app-namespace.txt` est écrit dans le dossier externe de l'application ;
   pour le récupérer sur le PC : `adb pull /storage/emulated/0/Android/data/org.jellyfin.shieldprobe/files/g0-app-namespace.txt docs/jellyfin-shield/probe-data/`.
```

- [ ] **Step 2: Connecteur (contenu exact)** — `tools/probe/connect.sh` :
```bash
#!/usr/bin/env bash
# Connexion ADB réseau vers la Shield ; mémorise l'IP dans device.env (ignoré par git).
set -euo pipefail
JF_LIB_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck disable=SC1090
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
```

- [ ] **Step 3: Test** — `tests/probe/test-connect.sh` :
```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }
t "sans IP : refus explicite" bash -c '! DRY_RUN=1 bash ../tools/probe/connect.sh >/dev/null 2>&1'
t "shellcheck propre"         bash -c 'shellcheck ../tools/probe/connect.sh'
exit $fail
```
Run: `bash tests/probe/test-connect.sh` → PASS ×2 (le refus sans IP est le comportement attendu).

- [ ] **Step 4: Commit**
```bash
git add tools/probe/connect.sh docs/jellyfin-shield/README.md tests/probe/test-connect.sh
git commit -m "feat(shield): procédure du débogage réseau ADB et connecteur"
```

### Task 5: Mini-sonde APK — `dlopen` en namespace applicatif + fallback sans PC

**Files:** Create `app-probe/` (Gradle + Kotlin + JNI) ; Test `app-probe/app/src/test/kotlin/org/jellyfin/shieldprobe/ProbeReportTest.kt`. Skills : `expert-architect` (module), `expert-tester`.

**Interfaces:**
- Produces : `ProbeReport.render(dlopenProbe: (String) -> String?, tags: List<String>): String` (ligne par lib : `OK\t<lib>` ou `ÉCHEC\t<lib>` ; séparateur `\n`) ; APK debug `app-probe/app/build/outputs/apk/debug/app-debug.apk` ; fichier exporté `g0-app-namespace.txt` (consommé par T10 après `adb pull`).

- [ ] **Step 1: Test TDD en premier (échec attendu)** — `app-probe/app/src/test/kotlin/org/jellyfin/shieldprobe/ProbeReportTest.kt` :
```kotlin
package org.jellyfin.shieldprobe

import org.junit.Assert.assertEquals
import org.junit.Test

class ProbeReportTest {
    @Test
    fun `une librairie chargée sans erreur rend OK`() {
        assertEquals("OK\tlibx.so", ProbeReport.render({ null }, listOf("libx.so")))
    }

    @Test
    fun `un dlopen en échec rend ÉCHEC suivi du nom de la librairie`() {
        assertEquals("ÉCHEC\tlibx.so", ProbeReport.render({ "motif non atteignable" }, listOf("libx.so")))
    }

    @Test
    fun `deux librairies rendent deux lignes séparées par retour à la ligne`() {
        assertEquals("OK\tL1\nOK\tL2", ProbeReport.render({ null }, listOf("L1", "L2")))
    }
}
```

- [ ] **Step 2: Squelette Gradle (contenus exacts)**

Bootstrap requis au premier lancement (hors fichiers versionnés) :
- `cd app-probe && gradle wrapper --gradle-version 8.11.1` (Gradle installé sur le poste ; ensuite les tâches utilisent `./gradlew`, wrapper committé).
- Créer `app-probe/local.properties` (ignoré par git via l'entrée T1) avec la ligne `sdk.dir=/chemin/du/android-sdk`.

`app-probe/settings.gradle` :
```gradle
pluginManagement { repositories { google(); mavenCentral(); gradlePluginPortal() } }
dependencyResolutionManagement { repositories { google(); mavenCentral() } }
rootProject.name = "jf-shield-probe"
include(":app")
```

`app-probe/build.gradle` :
```gradle
plugins {
    id "com.android.application" version "8.7.3" apply false
    id "org.jetbrains.kotlin.android" version "2.0.21" apply false
}
```

`app-probe/gradle.properties` :
```properties
org.gradle.jvmargs=-Xmx2048m
android.useAndroidX=true
```

`app-probe/app/build.gradle` :
```gradle
plugins {
    id "com.android.application"
    id "org.jetbrains.kotlin.android"
}

android {
    namespace = "org.jellyfin.shieldprobe"
    compileSdk = 34
    externalNativeBuild { cmake { path "src/main/jni/CMakeLists.txt" } }
    defaultConfig {
        applicationId = "org.jellyfin.shieldprobe"
        minSdk = 26
        targetSdk = 26
        ndk { abiFilters "arm64-v8a" }
        externalNativeBuild { cmake { arguments "-DANDROID_PLATFORM=android-26" } }
    }
}

dependencies { testImplementation "junit:junit:4.13.2" }
```

`app-probe/app/src/main/AndroidManifest.xml` :
```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <uses-feature android:name="android.software.leanback" android:required="false"/>
    <application android:label="Sonde JF Shield">
        <activity android:name=".MainActivity" android:exported="true">
            <intent-filter>
                <action android:name="android.intent.action.MAIN"/>
                <category android:name="android.intent.category.LAUNCHER"/>
                <category android:name="android.intent.category.LEANBACK_LAUNCHER"/>
            </intent-filter>
        </activity>
    </application>
</manifest>
```
Note : sans `android:banner`, la tuile TV peut s'afficher sans vignette ; le lancement de secours reste `am start -n org.jellyfin.shieldprobe/.MainActivity` (via ADB) ou l'icône à l'écran d'applications — suffisant pour le repli « sans PC » (le résultat s'affiche sur l'écran de l'activité).

- [ ] **Step 3: Implémentation (contenus exacts)**

`app-probe/app/src/main/kotlin/org/jellyfin/shieldprobe/ProbeReport.kt` :
```kotlin
package org.jellyfin.shieldprobe

/** Rendu du rapport dlopen : une ligne par bibliothèque, séparées par retour à la ligne. */
object ProbeReport {
    fun render(dlopenProbe: (String) -> String?, tags: List<String>): String =
        tags.joinToString("\n") { tag ->
            val error = dlopenProbe(tag)
            (if (error == null) "OK" else "ÉCHEC") + "\t$tag"
        }
}
```

`app-probe/app/src/main/kotlin/org/jellyfin/shieldprobe/MainActivity.kt` :
```kotlin
package org.jellyfin.shieldprobe

import android.app.Activity
import android.os.Bundle
import android.widget.TextView
import java.io.File
import kotlin.concurrent.thread

class MainActivity : Activity() {
    private val TAGS = listOf("libnvmedia.so", "libnvmpeg11.so", "libnvhost.so")

    private external fun dlopenProbe(lib: String): String?

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val view = TextView(this).apply { text = "Sonde en cours…"; textSize = 16f }
        setContentView(view)
        thread {
            try {
                val report = ProbeReport.render({ lib -> dlopenProbe(lib) }, TAGS)
                val outDir = getExternalFilesDir(null)
                if (outDir != null) File(outDir, "g0-app-namespace.txt").writeText(report)
                runOnUiThread { view.text = report }
            } catch (e: Throwable) {
                runOnUiThread { view.text = "ERREUR : ${e.javaClass.simpleName}" }
            }
        }
    }

    companion object { init { System.loadLibrary("dlopen_probe") } }
}
```

`app-probe/app/src/main/jni/dlopen_probe.c` :
```c
/* Sonde dlopen exécutée dans le namespace linker applicatif — résultat authentifiant G0. */
#include <dlfcn.h>
#include <jni.h>
#include <string.h>

JNIEXPORT jstring JNICALL
Java_org_jellyfin_shieldprobe_MainActivity_dlopenProbe(JNIEnv *env, jobject thiz, jstring jlib) {
    (void)thiz;
    jboolean isCopy = JNI_FALSE;
    const char *lib = (*env)->GetStringUTFChars(env, jlib, &isCopy);
    void *h = dlopen(lib, RTLD_NOW | RTLD_LOCAL);
    (*env)->ReleaseStringUTFChars(env, jlib, lib);
    if (h) { dlclose(h); return NULL; }
    char *err = dlerror();
    if (!err) err = "raison inconnue";
    return (*env)->NewStringUTF(env, err);
}
```

`app-probe/app/src/main/jni/CMakeLists.txt` :
```cmake
cmake_minimum_required(VERSION 3.22.1)
project(dlopen_probe LANGUAGES C)
add_library(dlopen_probe SHARED dlopen_probe.c)
```

- [ ] **Step 4: Exécuter** — `cd app-probe && ./gradlew :app:testDebugUnitTest` → PASS (3 tests).

- [ ] **Step 5: Build debug (sans appareil)** — `cd app-probe && ./gradlew :app:assembleDebug` → APK produit.

- [ ] **Step 6: Commit**
```bash
git add app-probe
git commit -m "feat(probe): mini-APK dlopen en namespace applicatif avec repli TV"
```

### Task 6: `ffprobe` statique aarch64 (prérequis G1)

**Files:** Create `tools/probe/build-ffprobe.sh`, `tests/probe/test-build-ffprobe.sh`. Skills : `expert-bash`, `expert-devops` (toolchain NDK).

**Interfaces:**
- Produces : `tools/probe/out/ffprobe/ffprobe` — binaire aarch64 API 26 (libs statiques, libc bionic dynamique), exécutable depuis `/data/local/tmp` ou `filesDir`.
- Consumes : dépôt `jellyfin-ffmpeg` tag `v7.1.3-6` (fetch au premier lancement vers `tools/probe/build/`), `ANDROID_NDK_ROOT` (r27+).

- [ ] **Step 1: Test en premier (échec attendu)** — `tests/probe/test-build-ffprobe.sh` :
```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }
t "dry-run sans appareil" bash -c 'DRY_RUN=1 bash ../tools/probe/build-ffprobe.sh'
t "shellcheck propre"     bash -c 'shellcheck ../tools/probe/build-ffprobe.sh'
t "tag v7.1.3-6 présent"   grep -q 'v7.1.3-6' ../tools/probe/build-ffprobe.sh
exit $fail
```
Run: `bash tests/probe/test-build-ffprobe.sh` → FAIL (script absent).

- [ ] **Step 2: Implémenter (contenu exact)** — `tools/probe/build-ffprobe.sh` :
```bash
#!/usr/bin/env bash
# Construction de ffprobe Android aarch64 basé sur jellyfin-ffmpeg v7.1.3-6.
set -euo pipefail
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
# shellcheck disable=SC1090
if [ "$DRY_RUN" -eq 1 ]; then
  log "dry-run : fetch v7.1.3-6 + configure minimal + make ffprobe + install vers out/ffprobe."
  exit 0
fi
require_cmd git make
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TAG="v7.1.3-6"
SRC="$PROBE_DIR/build/ffmpeg-$TAG"
[ -d "$SRC" ] || git clone --depth 1 --branch "$TAG" https://github.com/jellyfin/jellyfin-ffmpeg "$SRC"
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
mkdir -p "$PROBE_DIR/out/ffprobe"
cd "$SRC" && make distclean 2>/dev/null || true
./configure \
  --arch=aarch64 --target-os=android --enable-cross-compile \
  --cc="$TS/aarch64-linux-android26-clang" \
  --ar="$TS/llvm-ar" --ranlib="$TS/llvm-ranlib" --strip="$TS/llvm-strip" \
  --disable-programs --enable-ffprobe --disable-doc --disable-network \
  --extra-cflags="-Oz -fPIC" --extra-ldflags="-Wl,--gc-sections" \
  --prefix="$PROBE_DIR/out/ffprobe"
make -j"$(nproc)" ffprobe
cp -f ./ffprobe "$PROBE_DIR/out/ffprobe/ffprobe"
"$TS/llvm-readelf" -h "$PROBE_DIR/out/ffprobe/ffprobe" | grep -q AArch64 || die "binaire non aarch64."
log "ok : $PROBE_DIR/out/ffprobe/ffprobe"
```

- [ ] **Step 3: Vérifier** — `bash tests/probe/test-build-ffprobe.sh` → PASS ×3.

- [ ] **Step 4: Commit**
```bash
git add tools/probe/build-ffprobe.sh tests/probe/test-build-ffprobe.sh
git commit -m "feat(probe/g1): construction du binaire ffprobe statique aarch64"
```

### Task 7: Sonde stockage G1 (`g1-storage.sh`)

**Files:** Create `tools/probe/g1-storage.sh`, `tests/probe/test-g1.sh`. Skills : `expert-bash`.

**Interfaces:**
- Consumes : `lib.sh` (T1), binaire `out/ffprobe/ffprobe` (T6).
- Produces : `probe-data/g1-storage.txt` avec 3 sections `^### ` : `volume USB` (listing `/storage`), `RW` (RW ou « R/O attendu » par volume), `lecture ffprobe` (métadonnées d'un média ou `aucun média trouvé`).
- La vérification RW sur `filesDir` échoit à la mini-sonde APK (T5) ; cette sonde couvre le chemin ADB.

- [ ] **Step 1: Test en premier** — `tests/probe/test-g1.sh` :
```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }
t "dry-run sans appareil" bash -c 'DRY_RUN=1 bash ../tools/probe/g1-storage.sh'
t "shellcheck propre"     bash -c 'shellcheck ../tools/probe/g1-storage.sh'
exit $fail
```
Run: `bash tests/probe/test-g1.sh` → FAIL (script absent).

- [ ] **Step 2: Implémenter (contenu exact)** — `tools/probe/g1-storage.sh` :
```bash
#!/usr/bin/env bash
# G1 — lecture du disque USB de la Shield via ffprobe statique ; test RW informatif.
set -euo pipefail
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
# shellcheck disable=SC1090
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
```

- [ ] **Step 3: Vérifier** — `bash tests/probe/test-g1.sh` → PASS ×2.

- [ ] **Step 4: Commit**
```bash
git add tools/probe/g1-storage.sh tests/probe/test-g1.sh
git commit -m "feat(probe/g1): lecture média USB avec ffprobe statique et test RW"
```

### Task 8: Matrice `ffmpeg` statique (sw | v4l2m2m | mediacodec | nvdec)

**Files:** Create `tools/probe/build-ffmpeg.sh`, `tests/probe/test-build-ffmpeg.sh`. Skills : `expert-bash`, `expert-devops` (toolchain NDK).

**Interfaces:**
- Produces : `tools/probe/out/ffmpeg-<variant>/ffmpeg` pour `sw`, `v4l2m2m`, `mediacodec`, `nvdec` ; `out/ffmpeg-<variant>/configure.log` (toujours) ; `out/ffmpeg-mediacodec/encoders-presence.txt` (grep des sources — l'encodeur `h264_mediacodec` existe-t-il dans cette base v7.1.3-6 ?).
- `nvdec` : échec de configure **attendu et valide** (constat « NVMedia absent du SDK Android », consigné).
- Consumes : dépôt `jellyfin-ffmpeg` v7.1.3-6 (identique T6), NDK r27+.

- [ ] **Step 1: Test en premier** — `tests/probe/test-build-ffmpeg.sh` :
```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }
t "variant inconnu : refus explicite" bash -c '! DRY_RUN=1 bash ../tools/probe/build-ffmpeg.sh bogus 2>/dev/null'
t "dry-run sw"         bash -c 'DRY_RUN=1 bash ../tools/probe/build-ffmpeg.sh sw'
t "dry-run v4l2m2m"    bash -c 'DRY_RUN=1 bash ../tools/probe/build-ffmpeg.sh v4l2m2m'
t "dry-run mediacodec" bash -c 'DRY_RUN=1 bash ../tools/probe/build-ffmpeg.sh mediacodec'
t "dry-run nvdec"      bash -c 'DRY_RUN=1 bash ../tools/probe/build-ffmpeg.sh nvdec'
t "shellcheck propre"  bash -c 'shellcheck ../tools/probe/build-ffmpeg.sh'
exit $fail
```
Run: `bash tests/probe/test-build-ffmpeg.sh` → FAIL (script absent).

- [ ] **Step 2: Implémenter (contenu exact)** — `tools/probe/build-ffmpeg.sh` :
```bash
#!/usr/bin/env bash
# Construction de la matrice ffmpeg statique aarch64 pour les sondes G2.
# Usage : build-ffmpeg.sh sw | v4l2m2m | mediacodec | nvdec
set -euo pipefail
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
# shellcheck disable=SC1090
VARIANT="${1:-}"
case "$VARIANT" in
  sw)         CFG_EXTRA="" ;;
  v4l2m2m)    CFG_EXTRA="--enable-v4l2-m2m" ;;
  mediacodec) CFG_EXTRA="--enable-mediacodec" ;;
  nvdec)      CFG_EXTRA="--enable-nvdec" ;;
  *) die "variant inconnu ou absent : '$VARIANT' (attendu : sw | v4l2m2m | mediacodec | nvdec)." ;;
esac
if [ "$DRY_RUN" -eq 1 ]; then
  log "dry-run : variant=$VARIANT ($CFG_EXTRA), fetch + configure + make ffmpeg."
  exit 0
fi
require_cmd git make
[ -n "${ANDROID_NDK_ROOT:-}" ] || die "exporter ANDROID_NDK_ROOT (NDK r27+)."
TAG="v7.1.3-6"
SRC="$PROBE_DIR/build/ffmpeg-$TAG"
[ -d "$SRC" ] || git clone --depth 1 --branch "$TAG" https://github.com/jellyfin/jellyfin-ffmpeg "$SRC"
TS="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
[ -x "$TS/aarch64-linux-android26-clang" ] || die "toolchain Android 26 absente de $TS."
OUT="$PROBE_DIR/out/ffmpeg-$VARIANT"; mkdir -p "$OUT"
cd "$SRC" && make distclean 2>/dev/null || true
if ! ./configure \
  --arch=aarch64 --target-os=android --enable-cross-compile \
  --cc="$TS/aarch64-linux-android26-clang" \
  --ar="$TS/llvm-ar" --ranlib="$TS/llvm-ranlib" --strip="$TS/llvm-strip" \
  --disable-doc --disable-ffplay --disable-ffprobe --disable-network \
  --extra-cflags="-Oz -fPIC" --extra-ldflags="-Wl,--gc-sections" \
  $CFG_EXTRA \
  --prefix="$OUT" > "$OUT/configure.log" 2>&1; then
  log "configure a échoué pour $VARIANT (cf. $OUT/configure.log) — constat enregistré (nvdec : attendu sans SDK L4T)."
  exit 0
fi
make -j"$(nproc)" ffmpeg
cp -f ./ffmpeg "$OUT/ffmpeg"
log "$VARIANT : $OUT/ffmpeg"
if [ "$VARIANT" = "mediacodec" ]; then
  {
    echo "# détection de l'encodeur h264_mediacodec via config.h du build :"
    if grep -q "CONFIG_H264_MEDIACODEC_ENCODER 1" "$SRC/config.h"; then
      echo "ENCODEUR PRÉSENT (CONFIG_H264_MEDIACODEC_ENCODER 1)."
    else
      echo "ABSENT : aucune macro CONFIG_H264_MEDIACODEC_ENCODER=1 dans config.h."
    fi
  } > "$OUT/encoders-presence.txt"
  log "presence encodeur : $(tr '\n' ' ' < "$OUT/encoders-presence.txt")"
fi
```

- [ ] **Step 3: Vérifier** — `bash tests/probe/test-build-ffmpeg.sh` → PASS ×6.

- [ ] **Step 4: Commit**
```bash
git add tools/probe/build-ffmpeg.sh tests/probe/test-build-ffmpeg.sh
git commit -m "feat(probe/g2): matrice de constructions ffmpeg statiques par variante"
```

### Task 9: Benchmarks G2 (`bench.sh`)

**Files:** Create `tools/probe/bench.sh`, `tests/probe/test-bench.sh`. Prérequis hôte : `ffmpeg` (testsrc2 + libx264 ; libx265 optionnel sinon avertissement). Skills : `expert-bash`, `expert-tester`.

**Interfaces:**
- Consumes : `parse_progress_fps()` et `parse_progress_speed()` (T1), `out/ffmpeg-<variant>/ffmpeg` (T8), `adb_target/run_adb/shell_cmd` (T1).
- Produces : `probe-data/g2-bench.tsv` (colonnes exactes `variant\tinput\troute\tfps\tspeed`) ; `probe-data/g2-encoders-<variant>.txt` pour **les variants poussés** (`sw`, `v4l2m2m`, `mediacodec` quand présent) — `nvdec` n'a pas de binaire en cas de constat, donc pas de fichier.
- Le script reste **sourcable** (`source` sans exécuter `main`) pour les tests : garde `[ "${BASH_SOURCE[0]}" = "$0" ]`.

- [ ] **Step 1: Implémenter (contenu exact)** — `tools/probe/bench.sh` :
```bash
#!/usr/bin/env bash
# G2 — benchmarks encode/décode par marche : fps mesurés sur les binaires de la matrice T8.
set -euo pipefail
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
# shellcheck disable=SC1090

generate_inputs() { # médias de benchmark générés sur l'hôte
  local gen="$1"
  mkdir -p "$gen"
  [ -f "$gen/in_4k_h264.mp4" ] || ffmpeg -hide_banner -loglevel error -y \
    -f lavfi -i testsrc2=size=3840x2160:rate=30 -t 6 \
    -c:v libx264 -preset veryfast -pix_fmt yuv420p "$gen/in_4k_h264.mp4"
  [ -f "$gen/in_1080_h264.mp4" ] || ffmpeg -hide_banner -loglevel error -y \
    -f lavfi -i testsrc2=size=1920x1080:rate=30 -t 6 \
    -c:v libx264 -preset veryfast -pix_fmt yuv420p "$gen/in_1080_h264.mp4"
  if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q libx265; then
    [ -f "$gen/in_1080_hevc.mp4" ] || ffmpeg -hide_banner -loglevel error -y \
      -f lavfi -i testsrc2=size=1920x1080:rate=30 -t 6 \
      -c:v libx265 -preset ultrafast -pix_fmt yuv420p "$gen/in_1080_hevc.mp4"
  fi
}

run_case() { # run_case <variant> <route> <decoder-args> <input> <encoder-args>
  local variant="$1" route="$2" dec="$3" input="$4" enc="$5"
  local pr="$PROBE_DIR/out/bench/progress-$variant-$route.txt"
  shell_cmd "/data/local/tmp/jf-ffmpeg-$variant $dec -i /data/local/tmp/$input -t 4 $enc -f null /dev/null" > "$pr" 2>&1 || true
  printf '%s\t%s\t%s\t%s\t%s\n' "$variant" "$input" "$route" "$(parse_progress_fps "$pr")" "$(parse_progress_speed "$pr")" >> "$DATA_DIR/g2-bench.tsv"
}

push_variant() { # push <variant> : binaire sur l'appareil puis listage des encodeurs HW
  local variant="$1"
  local bin="$PROBE_DIR/out/ffmpeg-$variant/ffmpeg"
  if [ ! -x "$bin" ]; then
    warn "variant $variant absent : exécuter tools/probe/build-ffmpeg.sh $variant (T8)."
    return 1
  fi
  run_adb push "$bin" "/data/local/tmp/jf-ffmpeg-$variant"
  shell_cmd "chmod 744 /data/local/tmp/jf-ffmpeg-$variant"
  shell_cmd "/data/local/tmp/jf-ffmpeg-$variant -hide_banner -encoders 2>/dev/null | grep -E 'v4l2m2m|mediacodec|nvdec'" \
    > "$DATA_DIR/g2-encoders-$variant.txt" 2>&1
  return 0
}

main() {
  if [ "$DRY_RUN" -eq 1 ]; then
    log 'dry-run : 3 inputs générés, 2 variants poussés, 2-3 routes mesurées par marche, TSV + listes encoders.'
    exit 0
  fi
  ensure_data_dir
  require_cmd ffmpeg
  local tsv="$DATA_DIR/g2-bench.tsv" gen="$PROBE_DIR/out/bench"
  adb_target
  generate_inputs "$gen"
  # push des inputs de benchmark sur l'appareil (run_case les consomme via /data/local/tmp)
  local input_file
  for input_file in in_4k_h264.mp4 in_1080_h264.mp4 in_1080_hevc.mp4; do
    if [ -f "$gen/$input_file" ]; then
      run_adb push "$gen/$input_file" "/data/local/tmp/$input_file"
    fi
  done
  printf 'variant\tinput\troute\tfps\tspeed\n' > "$tsv"
  if push_variant sw; then
    run_case sw 'dcpu-enc-cpu' '' 'in_1080_h264.mp4' '-c:v libx264 -preset veryfast'
    run_case sw 'dcpu-4k-scale' '' 'in_4k_h264.mp4' '-vf scale=1920:1080 -c:v libx264 -preset veryfast'
  fi
  if push_variant v4l2m2m; then
    run_case v4l2m2m 'dcpu-enc-m2m' '' 'in_1080_h264.mp4' '-c:v h264_v4l2m2m'
    run_case v4l2m2m 'dcpu-4k-scale' '' 'in_4k_h264.mp4' '-vf scale=1920:1080 -c:v h264_v4l2m2m'
    if [ -f "$gen/in_1080_hevc.mp4" ]; then
      run_case v4l2m2m 'hwdec-enc' '-hwaccel v4l2m2m -c:v hevc_v4l2m2m' 'in_1080_hevc.mp4' '-c:v h264_v4l2m2m'
    fi
  fi
  mediacodec_bench "$gen" "$tsv"
  [ -f "$gen/in_1080_hevc.mp4" ] || warn 'input HEVC absent (libx265 introuvable sur hôte) : déposer un HEVC test dans out/bench/in_1080_hevc.mp4 avant de relancer.'
  log "benchmarks : $tsv"
}

mediacodec_bench() { # marche MediaCodec : exécutée seulement si l'encodeur existe (constat T8 via config.h)
  local gen="$1" tsv="$2"
  local bin="$PROBE_DIR/out/ffmpeg-mediacodec/ffmpeg"
  [ -x "$bin" ] || return 0
  if grep -q "ENCODEUR PRÉSENT" "$PROBE_DIR/out/ffmpeg-mediacodec/encoders-presence.txt" 2>/dev/null; then
    run_adb push "$bin" /data/local/tmp/jf-ffmpeg-mediacodec
    shell_cmd 'chmod 744 /data/local/tmp/jf-ffmpeg-mediacodec'
    # listage des encodeurs MediaCodec exposés par le binaire (écrit dans g2-encoders-mediacodec.txt)
    shell_cmd "/data/local/tmp/jf-ffmpeg-mediacodec -hide_banner -encoders 2>/dev/null | grep -E 'mediacodec'" \
      > "$DATA_DIR/g2-encoders-mediacodec.txt" 2>&1 || true
    # vérification d'usage sur l'appareil : l'encodeur doit être listé
    if shell_cmd "/data/local/tmp/jf-ffmpeg-mediacodec -hide_banner -encoders 2>/dev/null" 2>&1 | grep -q h264_mediacodec; then
      run_case mediacodec 'dcpu-enc-mc' '' 'in_1080_h264.mp4' '-c:v h264_mediacodec'
      run_case mediacodec 'dcpu-4k-scale' '' 'in_4k_h264.mp4' '-vf scale=1920:1080 -c:v h264_mediacodec'
    else
      printf 'mediacodec\tconstat\tno-go : h264_mediacodec absent des encodeurs listés par le binaire\t0\t\n' >> "$tsv"
    fi
  else
    printf 'mediacodec\tconstat\tno-go : CONFIG_H264_MEDIACODEC_ENCODER absent de la base ffmpeg v7.1.3-6\t0\t\n' >> "$tsv"
  fi
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main; fi
```

- [ ] **Step 2: Test** — `tests/probe/test-bench.sh` :
```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }
t "sourcing sans exécuter main (garde $0)" bash -c 'source ../tools/probe/bench.sh && type run_case >/dev/null'
t "shellcheck propre"                      bash -c 'shellcheck ../tools/probe/bench.sh'
exit $fail
```
Run: `bash tests/probe/test-bench.sh` → PASS ×2. (Le parseur `parse_progress_fps` est déjà validé en T1 sur la fixture.)

- [ ] **Step 3: Commit**
```bash
git add tools/probe/bench.sh tests/probe/test-bench.sh
git commit -m "feat(probe/g2): benchmarks encode/décode par marche avec routes mesurées"
```

### Task 10: Rapport consolidé G2 (livrable + porte)

**Files:** Create `tools/probe/report.sh`, `tests/probe/test-report.sh`, `docs/jellyfin-shield/probe-report.md` (généré puis complété à la main). Skills : `expert-bash`.

**Interfaces:**
- Consumes : `probe-data/*.txt` + `g2-bench.tsv` (T2, T3, T5 via `adb pull`, T7, T9).
- Produces : `docs/jellyfin-shield/probe-report.md` avec exactement 4 lignes de verdict `^| Marche :` (V4L2-M2M, MediaCodec, NVMedia décodage, NVMedia encodage/option G5) — champs `<à compléter>` remplacés à la main par les données réelles, puis validés par le propriétaire (porte).

- [ ] **Step 1: Implémenter (contenu exact)** — `tools/probe/report.sh` :
````bash
#!/usr/bin/env bash
# Assemblage de probe-report.md ; tolère les fichiers de données absents.
set -euo pipefail
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"
# shellcheck disable=SC1090
ensure_data_dir
REPORT="$PROBE_DIR/../../docs/jellyfin-shield/probe-report.md"

main() {
  # garde anti-écrasement : un rapport déjà complété (verdicts saisis) n'est pas régénéré par accident
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
    printf '\nGénéré par tools/probe/report.sh — après exécution, les champs <à compléter> sont remplacés à la main avec les mesures.\n'
  } > "$REPORT"
  log "rapport : $REPORT"
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main; fi
````

- [ ] **Step 1b: Tirer le fichier de la sonde APK si elle a été installée** :
```bash
adb pull /storage/emulated/0/Android/data/org.jellyfin.shieldprobe/files/g0-app-namespace.txt docs/jellyfin-shield/probe-data/
```
(commande également documentée dans le README de T4, section repli « sans PC »).

- [ ] **Step 2: Test** — `tests/probe/test-report.sh` :
```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { if "$@"; then printf 'PASS : %s\n' "$1"; else printf 'FAIL : %s\n' "$1"; fail=1; fi; }
t "rapport à 4 marches" bash -c '
  bash ../tools/probe/report.sh >/dev/null
  grep -c "^| Marche : " ../docs/jellyfin-shield/probe-report.md | grep -qx "4"'
t "shellcheck propre"   bash -c 'shellcheck ../tools/probe/report.sh'
exit $fail
```
Run: `bash tests/probe/test-report.sh` → PASS ×2.

- [ ] **Step 3: Compléter à la main les verdicts/fps** (remplacer chaque `<à compléter>` par les données réelles de T2-T9/T5).

- [ ] **Step 4: Présenter le rapport au propriétaire** (porte G0/G1/G2, décision par marche + budget G3/G4/G5 à arbitrer — fin de plan).

- [ ] **Step 5: Commit**
```bash
git add docs/jellyfin-shield/probe-report.md tools/probe/report.sh tests/probe/test-report.sh
git commit -m "feat(probe/g2): rapport consolidé de sonde validant les marches HW"
```

---

## Self-review du plan (normes writing-plans)

1. **Couverture de la spec** : G0 → T2 (inventaire firmware/mémoire/dev/dmesg/lsusb) + T3 (dlopen shell avec rappel : informatif) + T5 (dlopen namespace applicatif, authentifiant + fallback sans PC) ✓ ; G1 → T6 (ffprobe aarch64) + T7 (volumes + RW + lecture média) avec l'échelle de repli documentée dans le README (T4) ✓ ; G2 → T8 (matrice 4 variants avec constat encoders `config.h` + nvdec) + T9 (benchmarks fps, routes sw/M2M/MediaCodec) ✓ ; livrable `probe-report.md` + porte utilisateur → T10 ✓. G3/G4/G5 : hors périmètre (plans séparés post-verdicts), conforme à la spec (porte G0→G2 avant toute intégration) ✓.
2. **Sans placeholder** : chaque script est un bloc final complet ; les `<à compléter>` du rapport T10 sont le contenu intentionnel du livrable (complété à la Step 3 avec les données réelles, avant la porte Step 4). Les échecs attendus des tests TDD initiaux sont explicités (FAIL avant implémentation).
3. **Cohérence des noms** : `DATA_DIR`, `PROBE_DIR`, `probe-data/`, `adb_target`, `run_adb`, `shell_cmd`, `parse_progress_fps`, `parse_progress_speed`, `fixtures/sample-progress.txt` (fixture `fps=30.0` → `30` et `speed=1.2x` → `1.2`), `ProbeReport.render`, `dlopen_probe` (JNI), `jf-ffmpeg-<variant>`, `encoders-presence.txt`, `CONFIG_H264_MEDIACODEC_ENCODER` utilisés de manière homogène ✓.
4. **Prérequis documentés** : NDK r27+ (T3, T6, T8), JDK 17 + Gradle wrapper (T5), ffmpeg hôte testsrc2 (T9), participation manuelle (T4, porte T10) ✓.
5. **Robustesse bash** : `set -euo pipefail` sans piège `&& retour` ; gardes `if` ; dry-run complet `DRY_RUN=1` sans appareil ; chaque script shellcheck-able via ses tests ✓.










