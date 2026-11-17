#!/usr/bin/env bash
# Collecte des assets embarqués de l'APK serveur :
#   (1) publish dotnet self-contained android-arm64 (dépôt courant),
#   (2) build jellyfin-web (npm) au tag JF_WEB_TAG,
#   (3) binaires ffmpeg/ffprobe du build server-live (tools/probe/build-ffmpeg.sh),
#   (4) empaquetage tar.xz + stamp dans app-server/app/src/main/assets/.
# Défaut = dry-run ; un run réel exige DRY_RUN=0 (5-15 min au premier passage) — harmonisé avec le README T6.
set -euo pipefail
DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
PROBE_DIR="$(cd "$DEPLOY_DIR/../../tools/probe" && pwd)"
ASSETS_DIR="$(cd "$DEPLOY_DIR/../../app-server/app/src/main/assets" && pwd)"
mkdir -p "$HOME/.cache"
WORK="$(mktemp -d "$HOME/.cache/jf-assets-XXXXXX")"   # disque dur : /tmp (tmpfs 5,6 Go) a fait OOM-killer sur le build npm (constat collect3)
trap 'rm -rf "$WORK"' EXIT
if [ "${DRY_RUN:-1}" -eq 1 ]; then
  printf '%s\n' '-> publish dotnet en deux étapes (IL linux-arm64 + runtime android-arm64), fusion + apphost termux dotnet-host-10.0'
  printf '%s\n' "-> npm build jellyfin-web @ ${JF_WEB_TAG:-v12.3}"
  printf '%s\n' '-> copie des binaires ffmpeg/ffprobe du build server-live (réseau+TLS+x264, cf. tools/probe/build-ffmpeg.sh)'
  printf '%s\n' '-> empaquetage + stamp dans app-server/app/src/main/assets'
  printf '%s\n' '-> publish du plugin page host-metrics (IL net10.0, tar.xz assets/plugin)'
  exit 0
fi
# (0) .env optionnel : variables par défaut surchargées par l'environnement du shell
ENV_FILE="${JF_ENV_FILE:-$DEPLOY_DIR/.env}"
if [ -f "$ENV_FILE" ]; then
  # shellcheck disable=SC1090,SC1091
  . "$ENV_FILE"
fi
JF_WEB_TAG="${JF_WEB_TAG:-v12.3}"   # défaut appliqué si ni le shell ni .env n'ont fixé la valeur (revue cycle 4)
export DOTNET_ROOT="${DOTNET_ROOT:-/home/bruno/tools/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
LOGS_DIR="$DEPLOY_DIR/assets-logs"; mkdir -p "$LOGS_DIR"
MERGE_LOG="$LOGS_DIR/merge.log"
# (1) publish serveur — repli NETSDK1082 : il n'existe pas de runtime pack
#     Microsoft.AspNetCore.App pour android-arm64. Stratégie en deux étapes :
#     (A) publish linux-arm64 self-contained → DLL AspNetCore/app, pur IL,
#         exécutables sous le runtime Mono du pack android ;
#     (B) publish console minimal android-arm64 → RUNTIME NETCore.App android ;
#     (C) fusion : base runtime android + IL linux absent, log dans merge.log.
# (A) publish IL : AspNetCore linux-arm64 (pack existant)
dotnet publish "$DEPLOY_DIR/../../Jellyfin.Server/Jellyfin.Server.csproj" -c Release \
  -r linux-arm64 --self-contained true -o "$WORK/server-linux"
[ -d "$WORK/server-linux" ] || {
  printf 'VERDICT : publish linux-arm64 absent — fusion IL impossible, escalade requise.\n' >&2
  exit 1
}
# (B) publish runtime : projet console minimal car le FrameworkReference Web du
#     projet réel rend tout publish android-arm64 direct impossible (NETSDK1082)
dotnet new console -o "$WORK/console" -f net10.0
dotnet publish "$WORK/console" -c Release -r android-arm64 --self-contained true \
  -o "$WORK/server-android"
# (1c-contrôle) cohérence des versions : les deux packs runtime doivent être
#     strictement identiques (bande SDK 10.0), sinon verdict + escalade
NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
version_pack() { find "$NUGET_PACKAGES/$1" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' 2>/dev/null | sort -V | tail -1; }
NETCORE_ANDROID_VER="$(version_pack microsoft.netcore.app.runtime.android-arm64)"
ASPNETCORE_LINUX_VER="$(version_pack microsoft.aspnetcore.app.runtime.linux-arm64)"
: > "$MERGE_LOG"
printf '[merge] pack runtime NETCore.App android-arm64=%s, pack AspNetCore linux-arm64=%s\n' \
  "$NETCORE_ANDROID_VER" "$ASPNETCORE_LINUX_VER" >> "$MERGE_LOG"
if [ -z "$NETCORE_ANDROID_VER" ] || [ -z "$ASPNETCORE_LINUX_VER" ] \
  || [ "$NETCORE_ANDROID_VER" != "$ASPNETCORE_LINUX_VER" ]; then
  printf 'VERDICT : incohérence de version des packs runtime (android=%s vs linux=%s) — escalade requise\n' \
    "$NETCORE_ANDROID_VER" "$ASPNETCORE_LINUX_VER" >&2
  exit 1
fi
# (C) fusion : tout le contenu runtime android sert de base…
mkdir -p "$WORK/server"
cp -a "$WORK/server-android/." "$WORK/server/"
# …complété par le contenu de server-linux absent du runtime android :
# DLL managées, deps/runtimeconfig de l'entry assembly `jellyfin`,
# ET répertoires de contenu du serveur (ServerSetupApp/, etc. — constat smoke I :
# DirectoryNotFoundException sur ServerSetupApp/index.mstemplate.html) ;
# natives linux *.so exclues : incompatibles bionic.
while IFS= read -r -d '' f; do
  rel="${f#"$WORK/server-linux/"}"
  case "$rel" in
    *.so) continue ;;                      # natives linux : incompatibles bionic, non copiées
  esac
  if [ -e "$WORK/server/$rel" ]; then continue; fi
  mkdir -p "$WORK/server/$(dirname "$rel")"
  cp "$f" "$WORK/server/$rel"
  printf '[merge] %s\n' "$rel" >> "$MERGE_LOG"
done < <(find "$WORK/server-linux" -type f -print0)
printf '[merge] total=%s fichiers copiés depuis server-linux (IL + contenus)\n' \
  "$(grep -c '^\[merge\] ' "$MERGE_LOG")" >> "$MERGE_LOG"
# (D) apphost bionic mergé (plan-réplique) : le pack NuGet android n'embarque
#     aucun hôte exécutable (muxer/apphost absents, ni libhostfxr — cf. rapport T3) ;
#     le muxer `dotnet` du paquet Termux dotnet-host-10.0 (ELF bionic, NEEDED
#     système uniquement) est copié à la racine du bundle, à côté de jellyfin.dll.
HOST_VER="10.0.13"
HOST_URLS=(
  "https://mirror.accum.se/mirror/termux.dev/apt/termux-main/pool/main/d/dotnet-host-10.0/dotnet-host-10.0_${HOST_VER}_aarch64.deb"
  "https://termux.librehat.com/apt/termux-main/pool/main/d/dotnet-host-10.0/dotnet-host-10.0_${HOST_VER}_aarch64.deb"
)
if ! command -v ar >/dev/null 2>&1; then
  printf 'VERDICT : binutils/ar absent — dépaquetage de l apphost termux impossible — escalade requise\n' >&2
  exit 1
fi
HOST_DEB="$WORK/dotnet-host-10.0_${HOST_VER}_aarch64.deb"
HOST_MIRROR=""
for u in "${HOST_URLS[@]}"; do
  if curl -fL --retry 2 -o "$HOST_DEB" "$u"; then HOST_MIRROR="$u"; break; fi
  rm -f "$HOST_DEB"
done
# Repli cache local (.apphost-cache/ — layout versionné, cf. README du déploiement) :
# le pool termux épure les versions passées — les copies d'un bundle précédent
# (contrôlées ci-dessous : ELF, NEEDED, chemin versionné HOST_VER/RT_VER, les
# mêmes que le paquet) évitent l'impasse réseau sur des artefacts stables.
HOST_CACHE_DIR="$DEPLOY_DIR/.apphost-cache/$HOST_VER"
HOST_FROM_CACHE=0
if [ -z "$HOST_MIRROR" ] && [ -f "$HOST_CACHE_DIR/dotnet" ]; then
  HOST_FROM_CACHE=1
elif [ -z "$HOST_MIRROR" ]; then
  printf 'VERDICT : téléchargement de l apphost termux impossible (miroirs accum.se et librehat) — escalade requise\n' >&2
  exit 1
fi
if [ "$HOST_FROM_CACHE" -eq 1 ]; then
  cp "$HOST_CACHE_DIR/dotnet" "$WORK/server/dotnet"
  chmod 744 "$WORK/server/dotnet"
else
  mkdir -p "$WORK/hostdata"
  ar p "$HOST_DEB" data.tar.xz > "$WORK/host-data.tar.xz"
  tar -xJf "$WORK/host-data.tar.xz" -C "$WORK/hostdata"
  # NB vérifié sur ce paquet : bin/dotnet est un wrapper shell aux chemins termux
  # (inutilisable tel quel) ; le muxer ELF bionic est la copie libexec ci-dessous
  APPHOST_SRC="$WORK/hostdata/data/data/com.termux/files/usr/lib/dotnet/dotnet"
  if [ ! -f "$APPHOST_SRC" ]; then
    printf 'VERDICT : muxer ELF introuvable dans le paquet termux (layout changé ?) — escalade requise\n' >&2
    exit 1
  fi
  cp "$APPHOST_SRC" "$WORK/server/dotnet"
  chmod 744 "$WORK/server/dotnet"
fi
: > "$LOGS_DIR/host-apphost.log"   # log par run (miroir du pattern merge.log)
{
  if [ "$HOST_FROM_CACHE" -eq 1 ]; then
    printf '[host-apphost] cache local=%s (miroirs indisponibles)\n' "$HOST_CACHE_DIR/dotnet"
  else
    printf '[host-apphost] miroir=%s\n' "$HOST_MIRROR"
  fi
  printf '[host-apphost] taille=%s octets\n' "$(stat -c %s "$WORK/server/dotnet")"
  printf '[host-apphost] sha256=%s\n' "$(sha256sum "$WORK/server/dotnet" | cut -d' ' -f1)"
  printf '[host-apphost] ELF: '
  file -b "$WORK/server/dotnet"
  readelf -h "$WORK/server/dotnet" 2>/dev/null | grep -E 'Class:|Machine:'
  readelf -d "$WORK/server/dotnet" 2>/dev/null | grep NEEDED
} >> "$LOGS_DIR/host-apphost.log" 2>&1
if readelf -d "$WORK/server/dotnet" 2>/dev/null | grep NEEDED | grep -qi termux; then
  printf 'VERDICT : dépendance termux dans NEEDED de l apphost — escalade requise\n' >&2
  exit 1
fi
# (D-bis) libs hôte runtime : le pack NuGet android n'embarque ni libhostfxr ni
#     libhostpolicy ; ils proviennent de DEUX paquets Termux séparés :
#     dotnet-hostfxr-10.0 (host/fxr) et dotnet-runtime-10.0 (libhostpolicy) —
#     version EXACTE alignée au host (10.0.13) exigée au listing des miroirs ;
#     si absente : journal runtime-listing.log + VERDICT (stop légitime, pas de
#     contournement par une autre version).
RT_VER="10.0.13"
RT_URLS=(
  "https://mirror.accum.se/mirror/termux.dev/apt/termux-main/pool/main/d/"
  "https://termux.librehat.com/apt/termux-main/pool/main/d/"
)
fetch_termux_deb() {
  local pkg="$1" extract="$2" ver="$3" base listing cand
  for base in "${RT_URLS[@]}"; do
    listing="$(curl -fsS --retry 2 "${base}${pkg}/" 2>/dev/null || true)"
    if [ -z "$listing" ]; then continue; fi
    # nom de fichier EXACT version ver (points échappés) ; le « || true » évite
    # que grep sans match ne tue le script via pipefail avant le VERDICT
    cand="$(printf '%s\n' "$listing" | grep -oE "${pkg}_${ver//./\\.}_aarch64\\.deb" | head -1 || true)"
    if [ -z "$cand" ]; then
      # version exacte absente au listing : journalisé, miroir suivant ;
      # VERDICT si les deux miroirs l'ignorent (pas de repli d'autre version)
      printf '[runtime-listing] %s%s : version %s absente\n' "$base" "$pkg/" "$ver" >> "$LOGS_DIR/runtime-listing.log"
      printf '%s\n' "$listing" >> "$LOGS_DIR/runtime-listing.log"
      continue
    fi
    if curl -fL --retry 2 -o "$WORK/$cand" "${base}${pkg}/${cand}"; then
      mkdir -p "$extract"
      ar p "$WORK/$cand" data.tar.xz > "$extract/data.tar.xz"
      tar -xJf "$extract/data.tar.xz" -C "$extract"
      FETCHED="$(printf '%s|%s' "${base}${pkg}/" "$cand")"
      return 0
    fi
  done
  return 1
}
: > "$LOGS_DIR/runtime-listing.log"   # listings sans la version exigée (par run)
FXR_FROM_CACHE=0
if ! fetch_termux_deb "dotnet-hostfxr-10.0" "$WORK/rtdata-fxr" "$RT_VER"; then
  if [ ! -f "$HOST_CACHE_DIR/host/fxr/$RT_VER/libhostfxr.so" ]; then
    printf 'VERDICT : deb dotnet-hostfxr-10.0_%s_aarch64 indisponible (version absente au listing — cf. assets-logs/runtime-listing.log — ou miroirs inaccessibles) — escalade requise\n' "$RT_VER" >&2
    exit 1
  fi
  FXR_FROM_CACHE=1
fi
if [ "$FXR_FROM_CACHE" -eq 1 ]; then
  FXR_MIRROR="cache-local"
  FXR_DEB="$HOST_CACHE_DIR/host/fxr/$RT_VER/libhostfxr.so"
else
  FXR_MIRROR="${FETCHED%%|*}"; FXR_DEB="${FETCHED##*|}"
fi
FETCHED=""
RT_FROM_CACHE=0
if ! fetch_termux_deb "dotnet-runtime-10.0" "$WORK/rtdata" "$RT_VER"; then
  if [ ! -f "$HOST_CACHE_DIR/libhostpolicy.so" ]; then
    printf 'VERDICT : deb dotnet-runtime-10.0_%s_aarch64 indisponible (version absente au listing — cf. assets-logs/runtime-listing.log — ou miroirs inaccessibles) — escalade requise\n' "$RT_VER" >&2
    exit 1
  fi
  RT_FROM_CACHE=1
fi
if [ "$RT_FROM_CACHE" -eq 1 ]; then
  RT_MIRROR="cache-local"
  RT_DEB="$HOST_CACHE_DIR/libhostpolicy.so"
else
  RT_MIRROR="${FETCHED%%|*}"; RT_DEB="${FETCHED##*|}"
fi
mkdir -p "$WORK/rtdata-fxr" "$WORK/rtdata"
find "$WORK/rtdata-fxr" "$WORK/rtdata" > "$LOGS_DIR/rtextraction.list"   # layout réel journalisé (arborescence complète)
HOSTFXR_SRC="$(find "$WORK/rtdata-fxr" -type f -name libhostfxr.so | head -1)"
HOSTPOLICY_SRC="$(find "$WORK/rtdata" -type f -name libhostpolicy.so | head -1)"
CORECLR_RT_SRC="$(find "$WORK/rtdata" -type f -name libcoreclr.so | head -1)"   # repéré, jamais mergé
# Repli cache : copies directes vers les emplacements finaux (pas de layout de
# paquet dans $WORK) — les contrôles (taille, sha, NEEDED) portent sur les
# mêmes fichiers finaux que la voie réseau.
if [ "$FXR_FROM_CACHE" -eq 1 ]; then
  HOSTFXR_SRC="$FXR_DEB"
fi
if [ "$RT_FROM_CACHE" -eq 1 ]; then
  HOSTPOLICY_SRC="$RT_DEB"
fi
if [ -z "$HOSTFXR_SRC" ] || [ -z "$HOSTPOLICY_SRC" ]; then
  printf 'VERDICT : libhostfxr.so ou libhostpolicy.so introuvable dans les paquets (layout changé ?) — escalade requise\n' >&2
  exit 1
fi
RT_VERSION="$(basename "$(dirname "$HOSTFXR_SRC")")"   # ex. 10.0.13 (le layout du cache le reproduit)
mkdir -p "$WORK/server/host/fxr/$RT_VERSION"
cp "$HOSTFXR_SRC" "$WORK/server/host/fxr/$RT_VERSION/libhostfxr.so"
# hostpolicy en self-contained muxé : chargé au niveau du bundle (racine du tar),
# une copie dans host/fxr serait une ressource redondante — placement unique
cp "$HOSTPOLICY_SRC" "$WORK/server/libhostpolicy.so"
# NOT merge des libs framework (CoreCLR etc.) : notre 10.0.14 NuGet reste en place
: > "$LOGS_DIR/rtfetch.log"   # log par run (miroir du pattern host-apphost.log)
{
  if [ "$FXR_FROM_CACHE" -eq 1 ]; then
    printf '[rtfetch] url_hostfxr=cache-local (%s), sha256=%s\n' \
      "$FXR_DEB" "$(sha256sum "$FXR_DEB" | cut -d' ' -f1)"
  else
    printf '[rtfetch] url_hostfxr=%s%s, sha256_deb=%s\n' \
      "$FXR_MIRROR" "$FXR_DEB" "$(sha256sum "$WORK/$FXR_DEB" | cut -d' ' -f1)"
  fi
  if [ "$RT_FROM_CACHE" -eq 1 ]; then
    printf '[rtfetch] url_runtime=cache-local (%s), sha256=%s\n' \
      "$RT_DEB" "$(sha256sum "$RT_DEB" | cut -d' ' -f1)"
  else
    printf '[rtfetch] url_runtime=%s%s, sha256_deb=%s\n' \
      "$RT_MIRROR" "$RT_DEB" "$(sha256sum "$WORK/$RT_DEB" | cut -d' ' -f1)"
  fi
  printf '[rtfetch] version_fxr=%s\n' "$RT_VERSION"
  if [ -n "$CORECLR_RT_SRC" ]; then
    printf '[rtfetch] libcoreclr.so paquet runtime : %s — non mergé (10.0.14 NuGet du bundle conservé)\n' "$CORECLR_RT_SRC"
  else
    printf '[rtfetch] libcoreclr.so : absent du paquet runtime — non mergé (10.0.14 NuGet du bundle conservé)\n'
  fi
  printf '[rtfetch] libhostfxr.so taille=%s octets, sha256=%s\n' \
    "$(stat -c %s "$WORK/server/host/fxr/$RT_VERSION/libhostfxr.so")" \
    "$(sha256sum "$WORK/server/host/fxr/$RT_VERSION/libhostfxr.so" | cut -d' ' -f1)"
  printf '[rtfetch] libhostpolicy.so taille=%s octets, sha256=%s\n' \
    "$(stat -c %s "$WORK/server/libhostpolicy.so")" \
    "$(sha256sum "$WORK/server/libhostpolicy.so" | cut -d' ' -f1)"
} >> "$LOGS_DIR/rtfetch.log" 2>&1
# (2) jellyfin-web
mkdir -p "$WORK/web-src"
curl -sL "https://github.com/jellyfin/jellyfin-web/archive/refs/tags/${JF_WEB_TAG}.tar.gz" \
  | tar -xzf - -C "$WORK/web-src" --strip-components=1
# (2-bis) patch web G4 : l'option MediaCodec (Android) dans le formulaire de transcodage ;
# sans elle, la sauvegarde de la page réinitialiserait HardwareAccelerationType=mediacodec.
WEB_PATCH_DIR="$DEPLOY_DIR/web-patch"
[ -f "$WEB_PATCH_DIR/g4-mediacodec-${JF_WEB_TAG}.patch" ] \
  || { printf '%s\n' "patch web G4 absent pour $JF_WEB_TAG (cf. $WEB_PATCH_DIR/README.md)" >&2; exit 1; }
patch -d "$WORK/web-src" -p1 --batch < "$WEB_PATCH_DIR/g4-mediacodec-${JF_WEB_TAG}.patch" \
  || { printf '%s\n' "application du patch web G4 échouée (cf. $WEB_PATCH_DIR/README.md)" >&2; exit 1; }
if ! grep -q "mediacodec" "$WORK/web-src/src/apps/dashboard/routes/playback/transcoding.tsx" \
   || ! grep -q "mediacodec" "$WORK/web-src/src/apps/dashboard/features/playback/constants/codecs.ts"; then
  printf '%s\n' "vérification post-patch web G4 échouée" >&2
  exit 1
fi
( cd "$WORK/web-src" && npm install > "$LOGS_DIR/npm.log" 2>&1 && npm run build:production > "$LOGS_DIR/npm-build.log" 2>&1 )
# NB (revue plan) : build:production requiert les devDependencies (webpack/cross-env) → npm install COMPLET ;
# logs dans $LOGS_DIR : valeur courante (shell ou .env) préservée, défauts appliqués sinon.
# (3) binaires ffmpeg/ffprobe du build « server-live » (Lot LiveTV) :
#     réseau https via openssl statique + libx264 (transcodage LiveTV) + h264_mediacodec
#     (G4). Les binaires sonde (sans réseau) ne servaient qu'aux benchmarks G2.
#     Pré-requis : bash tools/probe/build-openssl.sh && build-x264.sh && build-ffmpeg.sh server-live
mkdir -p "$WORK/bin"
[ -f "$PROBE_DIR/out/ffmpeg-server-live/ffmpeg" ] || { printf '%s\n' 'ffmpeg server-live absent : lancer tools/probe/build-ffmpeg.sh server-live' >&2; exit 1; }
[ -f "$PROBE_DIR/out/ffmpeg-server-live/ffprobe" ] || { printf '%s\n' 'ffprobe server-live absent : lancer tools/probe/build-ffmpeg.sh server-live' >&2; exit 1; }
cp "$PROBE_DIR/out/ffmpeg-server-live/ffmpeg" "$WORK/bin"
cp "$PROBE_DIR/out/ffmpeg-server-live/ffprobe" "$WORK/bin"
# (4) empaquetage + assets + stamp
# Exclusion des archives de liens statiques (*.a : libcoreclr_static le plus gros,
# libbrotli*, libSystem.*) — artefacts de build jamais chargés au runtime (preuve :
# NEEDED de libSystem.IO.Compression.Native.so = libz/libm/libdl/libc, brotli
# statique intégré) ; économie constatée sur le tar : 114,7 → 45,5 Mo compressés.
mkdir -p "$ASSETS_DIR/server" "$ASSETS_DIR/web" "$ASSETS_DIR/bin"
tar -C "$WORK/server" -cJf "$ASSETS_DIR/server/jellyfin-server.tar.xz" --exclude '*.a' .
tar -C "$WORK/web-src/dist" -cJf "$ASSETS_DIR/web/jellyfin-web.tar.xz" .
cp "$WORK/bin/ffmpeg" "$ASSETS_DIR/bin/ffmpeg"; xz -9e -f "$ASSETS_DIR/bin/ffmpeg"
cp "$WORK/bin/ffprobe" "$ASSETS_DIR/bin/ffprobe"; xz -9e -f "$ASSETS_DIR/bin/ffprobe"
sha256sum "$ASSETS_DIR/server/jellyfin-server.tar.xz" | cut -c1-16 > "$ASSETS_DIR/server/.stamp"
printf '[serveur] stamp=%s, tar.xz=%s octets\n' "$(cat "$ASSETS_DIR/server/.stamp")" "$(stat -c %s "$ASSETS_DIR/server/jellyfin-server.tar.xz")"
# (5) plugin page de configuration « Métriques host » : publish IL pur net10.0,
#     renommé HostMetrics.dll + manifeste meta.json aligné sur la version serveur
SERVER_VERSION="$(grep -oPm1 'Version\("\K[0-9.]+' SharedVersion.cs)"
TARGET_ABI="${SERVER_VERSION%.*}"
PLUGIN_NAME="Jellyfin.Plugin.HostMetrics"
mkdir -p "$WORK/plugin"
dotnet publish "$DEPLOY_DIR/../../$PLUGIN_NAME/$PLUGIN_NAME.csproj" -c Release -o "$WORK/plugin-publish"
cp "$WORK/plugin-publish/$PLUGIN_NAME.dll" "$WORK/plugin/HostMetrics.dll"
{
  printf '{\n'
  printf '  "id": "0b2e4b3e-31ea-4f6b-9d5a-e6f66b7c0b81",\n'
  printf '  "name": "Métriques host",\n'
  printf '  "version": "%s",\n' "$SERVER_VERSION"
  printf '  "owner": "Thyrack",\n'
  printf '  "category": "General",\n'
  printf '  "targetAbi": "%s",\n' "$TARGET_ABI"
  printf '  "status": "Active",\n'
  printf '  "assemblies": ["HostMetrics.dll"],\n'
  printf '  "autoUpdate": false\n'
  printf '}\n'
} > "$WORK/plugin/meta.json"
mkdir -p "$ASSETS_DIR/plugin"
tar -C "$WORK/plugin" -cJf "$ASSETS_DIR/plugin/hostmetrics-plugin.tar.xz" HostMetrics.dll meta.json
printf '[plugin] version=%s, targetAbi=%s\n' "$SERVER_VERSION" "$TARGET_ABI"
