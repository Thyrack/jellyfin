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

### Données du serveur : migration vers la partition contenu (82 GiB)
- La partition interne « contenu » (`/storage/emulated/0`, ~82 GiB libres) héberge
  `JellyfinServer/` (datadir + cache), libérant la partition app 11 GiB (vérification
  2 GiB de Jellyfin au boot) et survivant à la désinstallation de l'apk.
- Migration (service arrêté préalablement) :
  ```
  adb shell am force-stop org.jellyfin.shieldserver
  adb shell am broadcast -a org.jellyfin.shieldserver.COPY_DATA -f 0x01000020
  ```
  (budget goAsync ≈ 60 s : sur un support lent, une interception laisse un arbre
  partiel SANS sentinelle → repli interne sûr, migration à re-tenter.)
  La copie pose une sentinelle `.migration-ok` à la fin intégrale ; tant que la
  sentinelle manque, le serveur repart du datadir interne (aucune base neuve à l'aveugle).
- ⚠ La ré-extraction du bundle (changement de `server/.stamp`) efface `files/jf-server/**`
  — donc le datadir **interne**. Le datadir externe migré n'est jamais effacé.
  Pensez à refaire la copie après une ré-extraction si vous repartez de l'interne.
- L'USB TOSHIBA_EXT (1,8 T, CIFS) reste réservé aux fichiers médias (lecture seule
  par Jellyfin) : jamais la base SQLite (locks SMB non fiables → corruption).

### Transcodage HW MediaCodec (G4-v2 — construit, HW OFF en défaut)
- Défaut du build : **`none`** (encodeur logiciel libx264, optimum après le rebuild
  x264 avec asm NEON ARM64). La config peut être retrournée à `mediacodec` dans l'UI
  (dropdown « MediaCodec (Android) » ajouté au web) pour un actif de test.
- Route candidate : **décodage CPU → encodage `-c:v h264_mediacodec`**
  (`HardwareAccelerationType=mediacodec` on-device), gardée en whitelist — mais
  le run réel (matrice) a prouvé l'encodeur HW **bloque dès qu'un vrai muxer est
  branché** (mp4/fmp4/TS, 0 frame), cf. bloc « Chantier G4-v2 ».
- Détail : décodage HW HEVC (`hevc_mediacodec`) : fonctionne (`exit=0`) mais
  les frames HW → libx264 tombent à 7 fps (conversion userspace, `hwdownload`
  non implémenté). À déprioriser : chantier séparé filtres + ffmpeg (G5).
- x264 : rebuild avec **asm NEON ARM64** (`HAVE_NEON 1` en config) — l'encodage
  logiciel de 1080p tient le temps réel, fin des saccades (stutter).
- Fallback libx264/libx265 automatique si l'encodeur HW est indisponible
  (`SupportsEncoder=false` ou `EnableHardwareEncoding=false`).
- Consommation mesurée pendant lecture réelle : ffmpeg HW ≈ **1,25 cœur** (la sonde G2
  donnait ≈ 2,5 cœurs ; x264 logiciel saturerait 3-4 cœurs).
- Limites : 4K → transcodage non temps réel (26 fps mesurés avec scale) ; HEVC 10-bit
  couleur revalidée au run G4 (sortie décodeur NV12) ; si l'encodeur HW échoue à
  l'exécution, Jellyfin marque le job en échec (re-demande en SW via le réglage qualité
  client).

### Limitations MVP documentées
- ffmpeg embarqué = build « server-live » (Lot LiveTV) : jellyfin-ffmpeg v7.1.3-6 + réseau
  http/https (openssl statique) + **libx264 avec asm NEON ARM64** (`HAVE_NEON 1` : nasm
  n'est requis que pour l'asm x86 ; le build arm64 utilise l'assembleur intégré clang) +
  **h264_mediacodec** enc/dec (présent ; le mapping auto est livré — usage HW bloqué au
  muxer, cf. bloc G4-v2). Pré-requis build : `tools/probe/build-openssl.sh`,
  `build-x264.sh`, `build-ffmpeg.sh server-live` (pkgconf local `/home/bruno/tools/pkgconf`,
  cf. scripts). Le ffmpeg « sonde » (sans réseau) ne sert plus qu'aux benchmarks G2.
- LiveTV/IPTV : les chaînes https fonctionnent (preuve ffprobe on-device) ; certains flux du
  m3u sont caducs côté fournisseur (403 Amagi constatée depuis n'importe quel client).
- GC .NET plafonné à 384 Mo = 402653184 octets (constat OOM 20:53 : le killer mémoire Android a tué le process
  dotnet le plus gros RSS ; le serveur est relancé automatiquement par la boucle du service).
- Cultures requêtes invariantes : en mode globalization-invariant, `RequestLocalizationConfigurator`
  réduit les cultures supportées à la seule invariante (le header Accept-Language des clients ne peut plus
  poser `fr-FR` sur un thread de requête — cause de `CultureNotFoundException`/ICU4N constatée sur les
  GET /Items). `DefaultThreadCurrentCulture` est aussi posée (le patch initial ne couvrait que l'UI culture).
- Translitération (tri alphabétique) désactivée sous invariant : le static-ctor d'ICU4N tente
  `new CultureInfo("en")` (fallback du bundle) — interdit en mode invariant même avec une hygiène culture
  parfaite (constat 02:0x). `Transliterated` renvoie le texte brut ; les diacritiques restent gérés par
  `RemoveDiacritics` (chemin indépendant d'ICU4N).
- Licence du ffmpeg embarqué : **GPLv3** (`--enable-gpl --enable-version3` : x264 GPL, OpenSSL Apache-2.0) —
  usage interne sur le device, pas de redistribution du binaire.
- ⚠ La ré-extraction du bundle (changement de `server/.stamp`) efface aussi `files/jf-server/logs`
  — les journaux précédents disparaissent à chaque changement de stamp.
- Crypto : le stub (RNG urandom + hash/HMAC/PBKDF2 via BoringSSL système) couvre MD5/hashs/HMAC/PBKDF2 ;
  AES/X509/RSA/DES → CryptographicException attrapables (clés DataProtection inopérantes, tokens auth à re-créer
  après redémarrage).
- TLS sortant : opérationnel depuis le G4-bis — les IL android (pont AndroidCrypto exigeant une JVM, inexistante
  dans ce process free-running) sont remplacées à chaque démarrage par les IL linux (assets `tlsprovider/`) et
  un provider natif `CryptoNative_*` maison via BoringSSL système (libcrypto/libssl) ; providers limités au client
  TLS (pas de HTTPS serveur local, pas de client-cert) ; RSA/ECDSA/DSA/PKCS7/OCSP/ChaCha en échec propre.
  Canari automatique : cycle « Update Plugins » (~4 s, sans [ERR] manifest). Voir `deployment/apk-android/tlsprovider/`.

### Chantier G4-v2 — décodage/encodage HW : état factuel (matrice run réel)
- Encodage HW `h264_mediacodec` : **112-116 fps en `-f null`**, mais **0 frame dès qu'un
  vrai muxer est branché** (mp4/fmp4/hls-TS testés séparément via le processus app) —
  bug du build jellyfin-ffmpeg v7.1.3-6 ; Jellyfin enclenche donc une roue infinie avec HW.
- Décodage HW `hevc_mediacodec` : fonctionne (`exit=0`) mais les frames HW alimentant
  libx264 tombent à **7 fps** (buffer copy userspace) ; `hwdownload` échoue
  (`Function not implemented`) sur AV_PIX_FMT_MEDIACODEC.
- Mode stable actuel : **HW OFF** (`HardwareAccelerationType=none`) —
  **SW transcodage avec x264 asm NEON : 33 fps / speed=1.08× (temps réel mesuré
  on-device, incl. fmp4 HLS segments)** ; décodage CPU du HEVC consomme 2-3 cœurs.
- Piste G5 pour réactiver le HW : le ffmpeg de Termux ne contient pas mediacodec
  (vérifié) ; réactiver exigera soit un patch jellyfin-ffmpeg (mux + hwdownload),
  soit une source alternative compilée avec mediacodec + surface pipeline.

### G5-A (fait) — frontière de segments : la seule limite restante du HW
- Matrice v2 dans RUNFF (continu × frontière) : l'encodage HW **fournit 150 frames**
  via `-f mpegts` (TS en fichier unique) et `-f h264` (raw annex-B continu) → **temps
  réel validé** ; les 4 muxers à frontière de la matrice (hls fmp4 / hls TS / segment
  mpegts / matroska) **stallent au même instant (~3-5 s)** — matroska étant un
  conteneur mono-fichier, la limite est le créneau, pas la frontière — reproductibles
  en sessions fraîches séparées (mp4 et nut, hors matrice, au même motif observé).
- Interprétation : l'encodeur HW accepte le flux continu mais se bloque à la création
  d'un AUTRE contexte de sortie (re-liaison d'attente de l'API MediaCodec) ;
  l'analyse du runtime libav/mediacodec reste à produire (G5-B1).
- Le « TS en fichier unique » marche on-device : un patch serveur « TS continu + écriture
  de playlists HLS par le serveur .NET » est la piste B2 concrète pour réactiver le HW
  sans modifier jellyfin-ffmpeg (cf. la note G5-B2 ci-dessous).

### Note G5-B2 (piste retenue pour l'intégration serveur)
- Le serveur exécuterait l'encodeur HW via `-f mpegts` (fichier continu) avec une
  écriture de playlist HLS dynamique par le serveur .NET (chaque segment complété
  est indexé côté serveur, hors le muxer ffmpeg). Piste serveur pour contourner
  le stall des muxers à frontière, à implémenter en G5-B2.

### Veille anti-boucle ffmpeg (HW MediaCodec)
- Constat run réel : **deux jobs HW MediaCodec concomitants** (client à deux profils de
  qualité, session ancienne non refermée) → l'un des ffmpeg boucle en userland
  (~230× un cœur, aucune sortie segment). Comportement interne au driver/LibAV
  MediaCodec sous contention d'instances HW.
- Remède embarqué (G4) : le service échantillonne les CPU ticks des ffmpeg (2 s) ;
  au-delà de 15 cœurs (1 500 ticks/s, inatteignable en usage légitime),
  le kill libère la file — le TranscodeManager relance à la prochaine demande client.
- Usage : une lecture HW à la fois — si une session précédente traîne, le redémarrage
  de l'apk la purge (force-stop + relaunch).
