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
