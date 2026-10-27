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
    bash tools/probe/report.sh                   # G2 — rapport consolidé (refusé si probe-report.md existe déjà : ajouter --force pour régénérer)
Plus la mini-sonde APK (Task 5) : `cd app-probe && ./gradlew :app:assembleDebug` puis
`adb install app-probe/app/build/outputs/apk/debug/app-debug.apk`.

## Si ADB réseau est indisponible (repli « sonde APK » sans PC — amendement v1.1)
1. Copier `app-probe/app/build/outputs/apk/debug/app-debug.apk` sur une clé USB.
2. Sur la TV, via un explorateur de fichiers (ex. « File Manager Shield ») : autoriser
   « sources inconnues » pour cet explorateur, puis installer l'APK.
3. Lancer « Sonde JF Shield » : les résultats dlopen s'affichent à l'écran ; un fichier
   `g0-app-namespace.txt` est écrit dans le dossier externe de l'application ;
   pour le récupérer sur le PC : `adb pull /storage/emulated/0/Android/data/org.jellyfin.shieldprobe/files/g0-app-namespace.txt docs/jellyfin-shield/probe-data/`.

## Serveur embarqué (G3) — « Jellyfin Server Shield »

Le serveur Jellyfin complet (net10.0 android-arm64) tourne dans un APK en foreground service,
**sans Termux et sans root**.

### Construction et installation (depuis le PC)
1. Activer le débogage réseau ADB sur la Shield (section ci-dessus, une fois).
2. Provisionner dotnet 10 :
   `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /home/$USER/tools/dotnet`
   puis `export DOTNET_ROOT=/home/$USER/tools/dotnet PATH="$DOTNET_ROOT:$PATH"`.
3. Collecter les assets (publie le serveur, build jellyfin-web, ffmpeg sonde ; le stub crypto est
   pré-construit et versionné dans `app-server/app/src/main/assets/cryptostub/`, re-buildable via :
   `ANDROID_NDK_ROOT=… DRY_RUN=0 bash deployment/apk-android/build-cryptostub.sh`) :

       DRY_RUN=0 bash deployment/apk-android/collect-assets.sh
4. `cd app-server && JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64 ./gradlew :app:assembleDebug`
5. `export JF_SHIELD_IP=<ip> ; bash tools/probe/connect.sh ; DRY_RUN=0 bash deployment/apk-android/install-server.sh`

### Démarrage SANS télécommande
- `adb shell am start -n org.jellyfin.shieldserver/.MainActivity` → l'app démarre le service automatiquement.
- OU ouvrir l'icône « Jellyfin Server Shield » sur la TV (auto-start à l'ouverture).
- OU redémarrer la TV : le boot receiver relance le service au boot ; le service est
  START_STICKY (relancé par le système s'il est tué).
- UI admin : `http://<ip>:8096` depuis le navigateur PC.

### Assistant initial (navigateur PC uniquement)
1. Compte administrateur ; bibliothèque « Vidéos » pointée sur le volume réseau monté
   (`/storage/AX58U/TOSHIBA_EXT` constat G1 — lecture RO attendue pour les fichiers média).
2. Exclusion batterie recommandée : `Paramètres → Apps → Jellyfin Server Shield → Batterie` (aucune restriction).

### Rapport post-24 h (critère de stabilité G3)
    bash deployment/apk-android/check-stability.sh <fichier-résultats (gitignored)>
Collecte : app Android vivante (process hôte) + /health ; à lancer le lendemain du
premier démarrage (le process serveur lui-même s'appelle `dotnet`, non capté par le grep).

### Limitations MVP documentées
- Transcodage logiciel x264/x265 inopérant (ffmpeg de la sonde sans libx264) → direct play dominant ;
  transcodage HW = chantier G4.
- Crypto : le stub (RNG urandom + hash/HMAC/PBKDF2 via BoringSSL système) couvre MD5/hashs/HMAC/PBKDF2 ;
  AES/X509/RSA/DES → CryptographicException attrapables (clés DataProtection inopérantes, tokens auth à re-créer
  après redémarrage).
- TLS sortant : opérationnel depuis le G4-bis — les IL android (pont AndroidCrypto exigeant une JVM, inexistante
  dans ce process free-running) sont remplacées à chaque démarrage par les IL linux (assets `tlsprovider/`) et
  un provider natif `CryptoNative_*` maison via BoringSSL système (libcrypto/libssl) ; providers limités au client
  TLS (pas de HTTPS serveur local, pas de client-cert) ; RSA/ECDSA/DSA/PKCS7/OCSP/ChaCha en échec propre.
  Canari automatique : cycle « Update Plugins » (~4 s, sans [ERR] manifest). Voir `deployment/apk-android/tlsprovider/`.
