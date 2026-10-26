#!/usr/bin/env bash
# Collecte des assets embarqués de l'APK serveur :
#   (1) publish dotnet self-contained android-arm64 (dépôt courant),
#   (2) build jellyfin-web (npm) au tag JF_WEB_TAG,
#   (3) binaires ffmpeg/ffprobe de la sonde,
#   (4) empaquetage tar.xz + stamp dans app-server/app/src/main/assets/.
# Défaut = dry-run ; un run réel exige DRY_RUN=0 (5-15 min au premier passage) — harmonisé avec le README T6.
set -euo pipefail
DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
PROBE_DIR="$(cd "$DEPLOY_DIR/../../tools/probe" && pwd)"
ASSETS_DIR="$(cd "$DEPLOY_DIR/../../app-server/app/src/main/assets" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
if [ "${DRY_RUN:-1}" -eq 1 ]; then
  printf '%s\n' '-> publish dotnet en deux étapes (IL linux-arm64 + runtime android-arm64) puis fusion'
  printf '%s\n' "-> npm build jellyfin-web @ ${JF_WEB_TAG:-v12.3}"
  printf '%s\n' '-> copie des binaires sonde (ffmpeg/ffprobe)'
  printf '%s\n' '-> empaquetage + stamp dans app-server/app/src/main/assets'
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
# …complété par le pur IL de server-linux absent du runtime android
# (DLL managées + deps/runtimeconfig de l'entry assembly `jellyfin` ;
# natives linux *.so exclues : incompatibles bionic)
for f in "$WORK/server-linux/"*; do
  name="$(basename "$f")"
  case "$name" in
    *.dll | *.deps.json | *.runtimeconfig.json) ;;
    *) continue ;;
  esac
  if [ -e "$WORK/server/$name" ]; then continue; fi
  cp "$f" "$WORK/server/"
  printf '[merge] %s\n' "$name" >> "$MERGE_LOG"
done
printf '[merge] total=%s fichiers IL copiés depuis server-linux\n' \
  "$(grep -c '^\[merge\] ' "$MERGE_LOG")" >> "$MERGE_LOG"
# (2) jellyfin-web
mkdir -p "$WORK/web-src"
curl -sL "https://github.com/jellyfin/jellyfin-web/archive/refs/tags/${JF_WEB_TAG}.tar.gz" \
  | tar -xzf - -C "$WORK/web-src" --strip-components=1
( cd "$WORK/web-src" && npm install > "$LOGS_DIR/npm.log" 2>&1 && npm run build:production > "$LOGS_DIR/npm-build.log" 2>&1 )
# NB (revue plan) : build:production requiert les devDependencies (webpack/cross-env) → npm install COMPLET ;
# logs dans $LOGS_DIR : valeur courante (shell ou .env) préservée, défauts appliqués sinon.
# (3) binaires sonde
mkdir -p "$WORK/bin"
cp "$PROBE_DIR/out/ffmpeg-mediacodec/ffmpeg" "$WORK/bin"
cp "$PROBE_DIR/out/ffprobe/ffprobe" "$WORK/bin"
# (4) empaquetage + assets + stamp
mkdir -p "$ASSETS_DIR/server" "$ASSETS_DIR/web" "$ASSETS_DIR/bin"
tar -C "$WORK/server" -cJf "$ASSETS_DIR/server/jellyfin-server.tar.xz" .
tar -C "$WORK/web-src/dist" -cJf "$ASSETS_DIR/web/jellyfin-web.tar.xz" .
cp "$WORK/bin/ffmpeg" "$ASSETS_DIR/bin/ffmpeg"; xz -9e -f "$ASSETS_DIR/bin/ffmpeg"
cp "$WORK/bin/ffprobe" "$ASSETS_DIR/bin/ffprobe"; xz -9e -f "$ASSETS_DIR/bin/ffprobe"
sha256sum "$ASSETS_DIR/server/jellyfin-server.tar.xz" | cut -c1-16 > "$ASSETS_DIR/server/.stamp"
printf '[serveur] stamp=%s, tar.xz=%s octets\n' "$(cat "$ASSETS_DIR/server/.stamp")" "$(stat -c %s "$ASSETS_DIR/server/jellyfin-server.tar.xz")"
