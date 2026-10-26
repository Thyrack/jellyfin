# Spec — Jellyfin Server sur NVIDIA Shield (2015/2017) avec sondage d'accélération Tegra

- **Date** : 2026-10-25
- **Projet** : exécuter un Jellyfin Server sur NVIDIA Shield Android TV 16 Go (2015/2017, modèle « foster », Tegra X1, Android 8.0) en *serveur local maison*, avec un accès progressif à l'accélération matérielle du Tegra.
- **Statut** : approuvé par le propriétaire (plan v1) — priorité : sondage puis APK, **sans Termux**. Amendement v1.1 : sondages pilotables sans contact physique (ADB réseau prioritaire, sonde APK en repli). Amendement v1.2 (2026-10-25) : sondes G0/G1/G2 exécutées avec succès sur l'appareil (cf. `docs/jellyfin-shield/probe-report.md`) — **G0 PASS** (Android 11/RQ1A.210105.003, RAM 3 016 Mo, 7 libs NVIDIA whitelisted dlopen OK en namespace app), **G1 PASS** (volume `/storage/AX58U` lisible RO, ffprobe OK), **G2 : GO MediaCodec** (encodeur h264_mediacodec actif ; décodage HW HEVC + encodage HW H.264 = 108 fps / 3,55× ; 1080p→H.264 = 116 fps / 3,82× ; V4L2-M2M no-go démontré) ; périmètre G3 confirmé (APK serveur embarqué), HW transcoding profond reporté à G4 (patch C# `HardwareAccelerationType` + `EncodingHelper`).

---

## 1. Contexte et motivation

Le serveur Jellyfin officiel cible Linux, Windows, macOS et Docker ; il n'existe pas de paquet pour Android ni pour NVIDIA Shield (Plex le fait via sa pile NVMedia). Le client Jellyfin for Android TV est compatible Shield de longue date ; la correction actuelle du direct play Dolby Vision Profile 7 (PR #5652) concerne les Shield 2019 (mdarcy) — sur notre modèle foster, le client reste pleinement fonctionnel en H.264/HEVC/HDR10. Le propriétaire dispose d'une Shield 2015/2017 (16 Go, 2 Go RAM, dernière mise à jour NVIDIA : Android 8.0) et veut **un serveur Jellyfin installé sur cet appareil**, avec la possibilité d'activer l'accélération matérielle Tegra selon un chemin d'escalade **V4L2-M2M / MediaCodec → NVMedia décodage → NVMedia encodage (parité Plex, chantier séparé)**.

### Faits saillants de l'étude

| Thème | Conclusion |
|---|---|
| Termux | Exclu par le propriétaire (le paquet `jellyfin-server` existe toutefois dans termux-packages, v10.11.x, aarch64 — utilisé comme référence de recette). |
| Serveur .NET sur Android | Faisabilité confirmée (issue jellyfin #3696) : .NET publie `android-arm64` (bionic) en self-contained. |
| Plex sur Shield | Pipeline **NVMedia** : encodage HW H.264 + décodage HW H.264/HEVC/MPEG-2 ; typiquement 2-3 transcodages simultanés *(affirmation officielle Plex, à confirmer dans notre contexte par le sondage)* ; *mais le burn-in de sous-titres bascule en logiciel* ; fragilités relevées sur les cycles 2024-2026. **Le modèle 2019 « tube » (P3430) n'est pas concerné — le nôtre (foster) l'est** (PMS Shield via Play Store, 1.43.x encore supporté en 2026). |
| Performances attendues sans HW | Plafond logiciel (CPU X1 seul) : direct play excellent ; 1080p H.264 ~temps réel ; 4K retranscodé hors de portée ; un seul transcodage logiciel à la limite. |
| Sous-titres burn-in | Jamais accéléré matériellement (aucune marche) → règle d'usage : sous-titres externes côté bibliothèque. |

---

## 2. Architecture cible

**APK wrapper Kotlin** (pas de Termux, pas de root) :

- `minSdk 26` (Android 8.0), `targetSdk 26` (préservation des autorisations de stockage *legacy* ; les restrictions W^X d'exécution de binaire arrivent avec Android 10).
- **Foreground service** + `START_STICKY` + boot receiver + exclusion d'optimisation batterie.
- **Serveur embarqué** : `Jellyfin.Server` publishé **depuis ce dépôt** en `-r android-arm64 --self-contained` (net10.0) ; binaires extraits de l'APK vers `filesDir` puis exécutés (exécution autorisée sous API 26).
- **ffmpeg embarqué** : `jellyfin-ffmpeg` buildé pour Android (référence : la recette `jellyfin-ffmpeg` de termux-packages, cible Android), placé dans `filesDir` et référencé au serveur via l'option CLI `--ffmpeg`.
- **UI admin** : servie par le serveur lui-même ; bundle `jellyfin-web` (dist) embarqué dans l'APK et attaché au serveur via `--webdir` (répertoire web explicite).
- **Stockage** : données / config / cache / métadonnées / logs dans `filesDir` ; bibliothèque média = **disque USB de la Shield**, fichiers en lecture seule attendue (autorisation de stockage *legacy* à valider en G1) ; le disque doit être monté par Android sous `/storage/<UUID>`. Si G1 établit par ailleurs que ce disque est accessible **en écriture**, le répertoire temporaire de transcodage pourra y être déplacé (option explicitée en G1) ; sinon, tout reste sur le stockage interne.
- **Port** : 8096 par défaut ; la logistique privilégie **ADB en réseau** (`adb connect <ip>`, activé dans les options développeur de la Shield) pour l'installation de l'APK et le shell à distance ; si ADB réseau n'est pas activable, repli « sonde APK » autonome sans PC (installation par clé USB + explorateur de fichiers de la TV). Les binaires de sondage s'exécutent depuis `filesDir` de l'APK de sonde, ou dans `/data/local/tmp` si ADB est actif.

## 3. Jalons (portes de décision)

Chaque jalon est une *porte* : validation explicite du propriétaire avant passage à la suivante. Les sondages G0-G1-G2 sont **non invasifs** (aucune altération système ; tous les fichiers de test résident dans `filesDir` de l'APK de sonde, ou `/data/local/tmp` si ADB réseau est actif).

### G0 — Audit de l'appareil
- Contenu : `getprop` (API 26, ABI aarch64, RAM), `cat /proc/meminfo`, listing de `/dev` (nœuds `video*` / `nvhost*`), `cat /system/etc/public.libraries.txt` (recherche `nvmedia|nvmpeg|nvidia`), micro-binaire statique testant `dlopen` sur `libnvmedia.so`, `libnvmpeg11.so`, `libnvhost.so`. **Les tests `dlopen` s'exécutent dans le namespace linker applicatif (mini-sonde APK)**, plus fidèle au contexte cible que le contexte `adb shell` ; diagnostics bonus si ADB réseau est actif : `dmesg`, `lsusb`.
- Critère : faisabilité numérique par marche (M2M / MediaCodec / NVMedia) documentée + disponibilité d'ADB réseau tranchée (actif / repli « sonde APK »).

### G1 — Accès au stockage de la bibliothèque
- Contenu : `ffprobe` statique embarqué dans l'APK de sonde (ou poussé via ADB dans `/data/local/tmp`) ; lecture d'un fichier média sur `/storage/<UUID>` ; vérification de l'écriture dans `filesDir` de l'APK de sonde.
- Critère : ffprobe lit un média du disque USB + scan de bibliothèque sur 10 fichiers test.
- Échelle de repli si la lecture échoue :
  1. Répertoires SAF persistés via le wrapper + pont de fichiers vers `filesDir` (solution partielle, débit limité) ;
  2. Bibliothèque restreinte au stockage interne (petit ensemble, pour test) ;
  3. Pivot documenté du projet (« serveur ailleurs »), issu de la validation du propriétaire.

### G2 — Sondage HW (cœur du travail)
- Builds croisés de `ffmpeg` NDK **statiques** de test (exécutables depuis `filesDir` de l'APK de sonde, ou depuis `/data/local/tmp` si ADB réseau est actif) :
  - a) `--enable-v4l2-m2m` + profils `h264_v4l2m2m`, `hevc_v4l2m2m` ;
  - b) `--enable-mediacodec` : hwaccel de décodage + vérification de la présence éventuelle d'un encodeur `h264_mediacodec` dans la base ffmpeg retenue ;
  - c) hwaccel `nvdec` (NVMedia-Jetson) si les `libnvmpeg11*` sont présentes.
- Cas de mesure, en fps : décode 4K HEVC → encode 1080p H.264 ; décode 1080p H.264 → encode 1080p H.264 ; décode 1080p HEVC → encode 1080p H.264.
- Livrable : **rapport data go/no-go par marche** + recommandation de la marche la plus haute atteignable dans `docs/jellyfin-shield/probe-report.md`.

### G3 — APK MVP (indépendant du verdict de G2)

Un no-go HW en G2 n'annule pas l'APK MVP : seul G4 dépend de G2.
- Contenu : chaîne Gradle (projet séparé ou module `deployment/apk-android`), `dotnet publish` de `Jellyfin.Server` en `android-arm64` self-contained, extracteur de binaire + lancement par le service foreground, bundle `jellyfin-web`, règles keep-alive, config d'encodage pointant `ffmpeg`/`ffprobe` de `filesDir`, port 8096, participation aux 10 fichiers test média.
- Critère : serveur répond sur :8096, bibliothèque scannée sur les 10 fichiers test, exécution stable mesurée après 24 h sans kill.
- Garantie de fiabilité : relance automatique après kill (log), mais le juge de référence reste l'absence de kill.

### G4 — Intégration HW (selon verdict de G2)
- Si `/dev/video*` M2M est disponible et fonctionnel : le serveur Jellyfin expose déjà l'option d'accélération « V4L2-M2M » (enum `HardwareAccelerationType.v4l2m2m` dans `MediaBrowser.Model`) → **quantité de patch ≈ zéro**, configuration seulement.
- Sinon : adaptations **dans ce dépôt**, vraisemblablement sur plusieurs projets : nouveau membre d'enum `HardwareAccelerationType` (et exposition UI) dans `MediaBrowser.Model`, logique des arguments ffmpeg dans `MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs`, détection de la marche dans `MediaBrowser.MediaEncoding/Encoder/EncoderValidator.cs` ; la contribution upstream est envisageable (fork de ce dépôt), sous réserve d'accords préalables du collectif jellyfin.
- Critère : transcodage matériel visible (dashboard, vitesse mesurée au-dessus du temps réel) ou retour explicite « aucune marche atteignable sans G5 ».

### G5 — NVMedia encode (optionnel ; budget distinct à arbitrer)
- Conception nécessaire : wrapper JNI/NDK (en-têtes JetPack, liaison à `libnvmedia*` de l'appareil), pipeline natif d'encodage H.264 à la Plex, profils d'accélération intégrés côté serveur C#.
- Cible : encodage H.264 1080p supérieur au temps réel ; l'absence d'**encodage HEVC sur le X1** est à confirmer lors du sondage (G2/G5) ; le tonemapping 4K HDR reste hors de portée (accès GPU non accessible depuis l'écosystème applicatif) — documenté, non promis.

## 4. Risques et mitigations

| Risque | Mitigation |
|---|---|
| Compatibilité plateforme : dotnet / exécutables / stockage sur Android 8 (API 26) | `targetSdk 26` (comportements legacy conservés), publish self-contained, exécution depuis `filesDir` ; repli si noexec : extraction des binaires vers `/data/local/tmp` via ADB au premier lancement |
| RAM 2 Go (pression LMK, process fauchés) | Foreground service + `START_STICKY` + wake lock + exclusion d'optimisation batterie + `DOTNET_GCHeapHardLimit` ; test de stabilité 24 h (G3) |
| USB illisible | Échelle de repli G1 (SAF / interne restreint / pivot) |
| 16 Go internes saturés (logs / cache transcodage) | Rotation de logs, nettoyage de cache, répertoire transcodage sur le disque USB uniquement si la RW est établie en G1 (cf. §2) ; sinon interne avec garde-fou d'espace disque libre (alerte + purge du cache transcodage) documenté dans l'UI |
| Exposition réseau / EOS Android 8.0 (plus de correctifs NVIDIA) | Usage LAN strict : aucun port forwarding, accès distant interdit dans ce périmètre, compte administrateur protégé par mot de passe fort ; réévaluation faite à l'issue de G3 |
| Encodage HW `mediacodec` fragile selon build ffmpeg | Portes de décision par benchmarks G2 + retour facile au logiciel (réglage par défaut si doute) |
| Burn-in de sous-titres en HW | Hors objet ; règle d'usage : sous-titres externes |
| Aucun clavier connecté sur la TV | Installation possible via `adb install` en réseau (sans clavier) ou par clé USB + explorateur TV en repli ; génération de saisies via `adb input text` si ADB est actif ; procédure d'activation du mode développeur documentée |

## 5. Critères de succès

1. **G0-G2 mesurés** : verdicts booléens + fps pour chaque marche dans `docs/jellyfin-shield/probe-report.md` ; chaque porte validée par le propriétaire.
2. **Serveur utilisé** : l'APK embarque le serveur, la bibliothèque scanne, direct play en lecture sur le client Jellyfin Android TV (Shield) + un deuxième client de test défini (navigateur sur PC).
3. **HW au niveau atteignable** : intégration de la marche la plus haute atteignable ; au moins un transcodage HW actif si G2 le permet, avec contrôles fps documentés.
4. **Aucun root, aucun Termux** — état système initial respecté.

## 6. Pistes écartées (et pourquoi)

| Piste | Raison de l'abandon |
|---|---|
| Termux (paquets officiels `jellyfin-server`) | Rejet explicite du propriétaire (pas de Termux) ; la recette reste une référence de build |
| UserLAnd / proot Debian en solution finale | Overhead I/O proot ; usage TV peu ergonomique ; conservé uniquement comme repli documenté, pas comme cible |
| Parité Plex NVMedia en première intention | Coût élevé sans garantie de faisabilité ; le sondage honest-first est retenu |
| PMS Plex installé sur la Shield | Trahirait l'objectif écosystème Jellyfin ; conservé comme source utile de mesures NVMedia lors du sondage |
| Serveur Jellyfin sur un autre matériel + Shield simple client | Contradiction avec la volonté du propriétaire : le serveur doit tenir sur la Shield |
| Root / Linux natif sur Shield | Hors périmètre : fragilité, perte de l'écran de démarrage, risque de briquage |

## 7. Références

- Plex — « Limitations When Running Plex Media Server on NVIDIA SHIELD » (support.plex.tv, article 221099648) : détails encode/décode HW + contrainte de stockage temporaire sur le modèle 16 Go.
- Issue jellyfin #3696 : confirmation du support .NET sur Android.
- termux-packages `packages/jellyfin-server/build.sh` : recette de référence de `jellyfin-ffmpeg` et `jellyfin-server` ciblant Android + dépendances natives `libskiasharp`, `libesqlite3`.
- Existant le plus proche : `ClmentCch/JellyfinAndroid-Server` (https://github.com/ClmentCch/JellyfinAndroid-Server, branche `android-build`, créé 2026-10-23) — POC d'APK wrapper comparable.
- Client : README `jellyfin-androidtv` (Shield citée explicitement) et PR #5652 (Dolby Vision Profile 7 sur Shield 2019).

## 8. Contraintes générales

- Tous les documents et commentaires en **français** (directive globale).
- Chaque nouvelle classe/méthode est accompagnée de ses tests unitaires ; à l'exécution, charger les skills expert-* pertinentes (expert-bash pour les scripts de sondage `.sh`, expert-devops si CI, expert-tester pour les tests).
- Projet isolé : branche dédiée `feat/jellyfin-shield-apk` (déjà créée), éventuel worktree si nécessaire.

## 9. Périmètre G3 (plan exécution serveur embarqué — suit les verdicts G0-G2)

Le serveur tourne **dans un APK wrapper** (nouveau module `app-server`, fonds communs avec la sonde `app-probe`) :

- **Publication serveur** : `dotnet publish Jellyfin.Server -f net10.0 -r android-arm64 --self-contained` (binaire + libs extraites des assets vers `filesDir/jf-server/` au premier lancement, `chmod +x`) ; rappel : exécution depuis `filesDir` autorisée sous `targetSdk 26` (W^X ne s'applique pas à `targetSdk<29` sur Android 11).
- **Bundlings obligatoires** : `jellyfin-web` (dist de la release correspondante) pointé via `--webdir` ; binaires `ffmpeg`/`ffprobe` (builds sonde aarch64) placés dans `filesDir/jf-server/bin/` et référencés au serveur via `--ffmpeg`. Limitation documentée : le build sonde ne contient pas libx264/libx265 → le transcodage **logiciel** x264/x265 du serveur ne fonctionnera pas au MVP ; direct play dominant + transcodage HW (G4) restent la voie.
- **Service** : foreground service `dataSync` + `START_STICKY` + boot receiver + wake lock + exclusion batterie ; ProcessBuilder avec `DOTNET_*` variables GC durcies ; redémarrage automatique sur sortie du process ; notification avec statut + lien vers `http://<ip>:8096`.
- **Répertoires serveur** : `--datadir/--configdir/--cachedir/--logdir` sous `filesDir` (confine, lisible) ; bibliothèque pointée manuellement par l'utilisateur vers `/storage/AX58U/...` (RO constatée G1).
- **Critères de succès G3** : le serveur démarre sur :8096 ; UI web accessible depuis le PC ; bibliothèque scannée (≥ 10 fichiers du volume) ; lecture direct sur le navigateur PC ; survie ≥ 24 h sans kill (log documenté).
- **Hors périmètre G3** : patchs serveur pour l'enum/le profil « mediacodec » (= G4), explore NVMedia/nvtranscode (= G5), Play Store.
