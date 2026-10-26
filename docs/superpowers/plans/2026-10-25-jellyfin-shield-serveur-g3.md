# Plan d'implémentation — Jellyfin Server embarqué sur Shield (G3 : APK serveur)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** livrer le module `app-server` (APK de service) qui exécute sur la Shield le serveur Jellyfin publishé depuis ce dépôt, avec bundling jellyfin-web + binaires ffmpeg/ffprobe de la sonde, service foreground persistant, et validation on-device des critères de la spec §9.

**Architecture :** publication `dotnet publish -r android-arm64 --self-contained` du dépôt courant, empaquetée (tar.xz) dans les assets d'un module Android Kotlin (`app-server/`, même socle tooling que `app-probe/`) ; extraction au premier lancement vers `filesDir/jf-server/` ; foreground service qui lance via `ProcessBuilder` un `jellyfin` CLI avec répertoires confinés sous `filesDir` et redémarrage avec backoff ; UI de contrôle minimale (activité : état + démarrer/arrêter + note web). HW transcoding = hors périmètre (G4) ; direct play + lecture web dominent.

**Tech Stack :** Kotlin + AGP 8.7.3 / Kotlin 2.0.21 / Gradle 8.11.1 (wrapper à bootstrapper), dotnet 10 SDK (installation locale via dotnet-install.sh), JetBrains commons-compress 1.27.1 + xz 1.9 (extraction tar.xz pure Java), jellyfin-web (tag `v12.3`, build npm depuis le dépôt official), ffmpeg/ffprobe issus des builds sonde (`tools/probe/out/`).

## Global Constraints

- Spec v1.2, section 9 (`docs/superpowers/specs/2026-10-25-jellyfin-shield-hw-design.md`) : APK wrapper, `minSdk 26`/`targetSdk 26`/`compileSdk 34`, foreground + `START_STICKY` + boot receiver + exclusion batterie, répertoires serveur confinés sous `filesDir`, port 8096, bibliothèque utilisateur pointée manuellement vers `/storage/AX58U/...`.
- Restriction explicite : le bundling ffmpeg de la sonde **n'inclut pas libx264/libx265** → le transcodage logiciel x264/x265 ne fonctionnera pas au MVP ; documenter dans l'app et le README (direct play dominant).
- Codes & docs **en français** ; tests JVM unitaires pour toute logique Kotlin pure ; Conventional Commits, un commit atomique par tâche ; gate `commit-review-gate` = revue expert-review avant chaque commit (fournée par le flux subagent-driven).
- Pas de mot de passe ni secret dans le dépôt ; l'emplacement device.env/probe-data etc. reste hors version.
- Réutilisation des patterns actés de la phase de sonde : directive `# shellcheck disable=SC1090,SC1091` avant source, helper test `t` shifté, dry-run, chemins tests `../../`.
- 24 h de stabilité (critère spec §9) : test *logé* dans la tâche T6, exécuté par le propriétaire (lecture de log le lendemain), avec script de collecte automatique.

Ordre : T1 (module), T2 (assets pipeline), T3 (extraction/service), T4 (UI/boot/manifest), T5 (on-device smoke + 24 h), T6 (docs + commit final). T1/T2 indépendants ; T3+T4+T5 séquentiels.

### Task 1: Squelette du module `app-server` (Gradle + manifest minimal)

**Files:**
- Create: `app-server/settings.gradle`, `app-server/build.gradle`, `app-server/gradle.properties`
- Create: `app-server/app/build.gradle`, `app-server/app/src/main/AndroidManifest.xml`
- Create: `app-server/app/src/main/kotlin/org/jellyfin/shieldserver/MainActivity.kt` (placeholder : TextView d'installation, remplacé en T4)
- Modify: `.gitignore` (+1 ligne : `app-server/local.properties`)
- Test (JVM) : aucun dans cette tâche (le plan de la sonde applique le pattern : les logiques pures arrivent en T3)
- Skills à charger : `expert-architect`.

**Interfaces:**
- Produces : projet Gradle compilable (`./gradlew :app:assembleDebug` PASSE) avec `applicationId=org.jellyfin.shieldserver`, `minSdk/targetSdk=26`, `compileSdk=34`, arm64-v8a ; manifest avec categories launcher et leanback et permission `INTERNET` ; le service/receiver arriveront en T4 (manifest complet alors).

- [ ] **Step 1 : Initialiser le projet Gradle** — `cd app-server` après création des fichiers :
```bash
gradle wrapper --gradle-version 8.11.1   # gradle au chemin /home/bruno/tools/gradle-8.11.1/bin/gradle
printf 'sdk.dir=/home/bruno/Android/sdk\n' > local.properties   # le dossier cible = app-server/local.properties (gitignored)
```

- [ ] **Step 2 : Contenus exacts** — `settings.gradle` :
```gradle
pluginManagement { repositories { google(); mavenCentral(); gradlePluginPortal() } }
dependencyResolutionManagement { repositories { google(); mavenCentral() } }
rootProject.name = "jf-shield-server"
include(":app")
```
`build.gradle` :
```gradle
plugins {
    id "com.android.application" version "8.7.3" apply false
    id "org.jetbrains.kotlin.android" version "2.0.21" apply false
}
```
`gradle.properties` :
```properties
org.gradle.jvmargs=-Xmx2048m
android.useAndroidX=true
```
`app/build.gradle` :
```gradle
plugins { id "com.android.application"; id "org.jetbrains.kotlin.android" }

android {
    namespace = "org.jellyfin.shieldserver"
    compileSdk = 34
    buildFeatures { buildConfig = true }
    kotlinOptions { jvmTarget = "1.8" }
    defaultConfig {
        applicationId = "org.jellyfin.shieldserver"
        minSdk = 26
        targetSdk = 26
        ndk { abiFilters "arm64-v8a" }
    }
}
dependencies {
    implementation "org.apache.commons:commons-compress:1.27.1"
    implementation "org.tukaani:xz:1.9"
    testImplementation "junit:junit:4.13.2"
}
```
`AndroidManifest.xml` :
```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <uses-permission android:name="android.permission.INTERNET"/>
    <uses-feature android:name="android.software.leanback" android:required="false"/>
    <application android:label="Jellyfin Server Shield" android:icon="@android:drawable/sym_def_app_icon">
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
`MainActivity.kt` (placeholder assurant une app installable) :
```kotlin
package org.jellyfin.shieldserver

import android.app.Activity
import android.os.Bundle
import android.widget.TextView

class MainActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val view = TextView(this).apply {
            text = "Serveur Jellyfin — module en préparation (T3)."
            textSize = 16f
        }
        setContentView(view)
    }
}
```

- [ ] **Step 3 : Vérifier** — `cd app-server && ./gradlew :app:assembleDebug` → BUILD SUCCESSFUL ; app installable (`adb install -r`) → affiche le texte placeholder. `android:icon` pointe sur un drawable système (pas de ressource dédiée) ; **pas de banner leanback** : la tuile TV peut être absente du launcher, repli documenté = `am start -n org.jellyfin.shieldserver/.MainActivity` (modèle sonde G0).
- [ ] **Step 4 : Commit** — 
```bash
git add app-server .gitignore && git commit -m "feat(server): squelette du module app-server Gradle/Kotlin cible Android 8+"
```

### Task 2: Pipeline d'assets (`collect-assets.sh`) — serveur publishé + web + binaires

**Files:**
- Create: `deployment/apk-android/collect-assets.sh`
- Create: `deployment/apk-android/.env.example` (`JF_WEB_TAG=v12.3`, `DOTNET_ROOT=/home/bruno/tools/dotnet`)
- Create: `app-server/app/src/main/assets/server/.gitkeep`, `app-server/app/src/main/assets/web/.gitkeep`, `app-server/app/src/main/assets/bin/.gitkeep` (les assets produits sont **gitignorés**, regénérés)
- Create: `tests/probe/test-collect-assets.sh` (répertoire dédié aux scripts serveur, même convention `t` shifté)
- Modify: `.gitignore` (bloc : `app-server/app/src/main/assets/server/*` `!app-server/app/src/main/assets/server/.gitkeep`, idem web + bin)
- Prérequis à provisionner au lancement : dotnet 10 SDK (T2 Step 0), node/npm (présents)

**Interfaces:**
- Produces (consommés par T3/T4) :
  - `app-server/app/src/main/assets/server/jellyfin-server.tar.xz` (publish self-contained du dépôt courant)
  - `app-server/app/src/main/assets/web/jellyfin-web.tar.xz` (dist npm du tag `JF_WEB_TAG`)
  - `app-server/app/src/main/assets/bin/ffmpeg.xz`, `bin/ffprobe.xz` (builds sonde existants)
  - stamp : `assets/server/.stamp` (hash sha256 du tar **tronqué à 16 hex**, consigné pour le versioning à chaud)
- Note : `dotnet publish` du dépôt courant : `dotnet publish Jellyfin.Server/Jellyfin.Server.csproj -c Release -f net10.0 -r android-arm64 --self-contained true -o <dossier>` — l'échec éventuel de paquets tierces (SkiaSharp arm64, SQLitePCL e_sqlite3 arm64) ou surtout **du framework lui-même** (projet `Microsoft.NET.Sdk.Web` : le runtime pack `Microsoft.AspNetCore.App android-arm64` peut ne pas exister côté NuGet) est un **verdict à escalader** du diagnostic, pas une régression de la tâche ; si le RID bloque le SDK Web, la variante de repli est : publish `--self-contained false` avec runtime pack Solo `Microsoft.NETCore.App` + dépôt manuel des librairies Kestrel restantes.

- [ ] **Step 0 (hors script)** : provisionner dotnet 10 :
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/opencode/dotnet-install.sh
bash /tmp/opencode/dotnet-install.sh --channel 10.0 --install-dir /home/bruno/tools/dotnet
export DOTNET_ROOT=/home/bruno/tools/dotnet ; export PATH="$DOTNET_ROOT:$PATH"
```
- [ ] **Step 1 : Test d'abord** — `tests/probe/test-collect-assets.sh` :
```bash
#!/usr/bin/env bash
# tests du pipeline d'assets serveur — exécutables sans build réel.
set -euo pipefail
cd "$(dirname "$0")"
fail=0
t() { local name="$1"; shift; if "$@"; then printf 'PASS : %s\n' "$name"; else printf 'FAIL : %s\n' "$name"; fail=1; fi; }
t "shellcheck propre" bash -c 'shellcheck ../../deployment/apk-android/collect-assets.sh'
t "dry-run : 4 étapes listées, aucune IO" bash -c '
  DRY_RUN=1 bash ../../deployment/apk-android/collect-assets.sh 2>&1 | grep -c "^-> " | grep -qx "4"'
exit $fail
```
Run → FAIL (script absent). 
- [ ] **Step 2 : Implémentation** — `deployment/apk-android/collect-assets.sh` :
```bash
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
  printf '-> publish dotnet android-arm64 self-contained (dépôt courant)\n'
  printf '-> npm build jellyfin-web @ %s\n' "${JF_WEB_TAG:-v12.3}"
  printf '-> copie des binaires sonde (ffmpeg/ffprobe)\n'
  printf '-> empaquetage + stamp dans app-server/app/src/main/assets\n'
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
# (1) publish serveur
dotnet publish "$DEPLOY_DIR/../../Jellyfin.Server/Jellyfin.Server.csproj" -c Release \
  -f net10.0 -r android-arm64 --self-contained true -o "$WORK/server"
# (2) jellyfin-web
mkdir -p "$WORK/web-src"
curl -sL "https://github.com/jellyfin/jellyfin-web/archive/refs/tags/${JF_WEB_TAG}.tar.gz" \
  | tar -xzf - -C "$WORK/web-src" --strip-components=1
LOGS_DIR="$DEPLOY_DIR/assets-logs"; mkdir -p "$LOGS_DIR"
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
cp "$WORK/bin/ffmpeg" "$ASSETS_DIR/bin/ffmpeg"; xz -9e "$ASSETS_DIR/bin/ffmpeg"
cp "$WORK/bin/ffprobe" "$ASSETS_DIR/bin/ffprobe"; xz -9e "$ASSETS_DIR/bin/ffprobe"
sha256sum "$ASSETS_DIR/server/jellyfin-server.tar.xz" | cut -c1-16 > "$ASSETS_DIR/server/.stamp"
printf '[serveur] stamp=%s, tar.xz=%s octets\n' "$(cat "$ASSETS_DIR/server/.stamp")" "$(stat -c %s "$ASSETS_DIR/server/jellyfin-server.tar.xz")"
```
- [ ] **Step 3 : Validation réelle** — le script PASS en run réel ; metrics imprimées (tailles : server tar ≈ 90 Mo / web tar ≈ 12 Mo / bin ≈ 7+7 Mo compressés).
- [ ] **Step 4 : Commit** — `git add deployment/apk-android tests/probe/test-collect-assets.sh app-server/app/src/main/assets .gitignore && git commit -m "feat(server): pipeline de collecte des assets embarqués (dotnet, web, ffmpeg)"`

### Task 3: Runtime Kotlin — extraction + launching + watchdog (`ServerAssets`, `ServerService`)

**Files:**
- Create: `app-server/app/src/main/kotlin/org/jellyfin/shieldserver/ServerAssets.kt` (pure, testable JVM)
- Create: `app-server/app/src/main/kotlin/org/jellyfin/shieldserver/ServerService.kt`
- Test: `app-server/app/src/test/kotlin/org/jellyfin/shieldserver/ServerAssetsTest.kt`
- Skills à charger : `expert-tester` (tests JVM), `expert-architect` (service Android).

**Interfaces:**
- `ServerAssets.buildArgs(filesDir: File): List<String>` — args CLI exacts du binaire `./jellyfin` (le port est géré par défaut 8096 côté serveur, pas d'option CLI — revue plan) :
  `--datadir /filesDir/jf-server/data --configdir .../config --cachedir .../cache --logdir .../logs --webdir .../web --ffmpeg .../bin/ffmpeg` (options vérifiées dans `Jellyfin.Server/StartupOptions.cs`).
- `ServerAssets.assetFilenames(): List<String>` = ["server/jellyfin-server.tar.xz", "web/jellyfin-web.tar.xz", "bin/ffmpeg.xz", "bin/ffprobe.xz"].
- `ServerService` : foreground `dataSync`, ProcessBuilder avec env `DOTNET_GCHeapHardLimit=536870912, DOTNET_SystemGlobalizationInvariant=1, DOTNET_EnableWriteXorExecute=0` ; watchdog de relance en backoff sur sortie du process (5 s → 60 s ×2) ; notification Android channel « jf-server » (statut + Error via `MainActivity.postMessage`) ; `START_STICKY` ; extraction tar.xz via commons-compress, bit exécutable via `File.setExecutable` (sans sous-process, API 26).

- [ ] **Step 1 : Test JVM d'abord (échec attendu)** — `ServerAssetsTest.kt` :
```kotlin
package org.jellyfin.shieldserver

import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File

class ServerAssetsTest {
    @Test fun `les 4 noms d'assets attendus`() {
        assertEquals(
            listOf("server/jellyfin-server.tar.xz", "web/jellyfin-web.tar.xz", "bin/ffmpeg.xz", "bin/ffprobe.xz"),
            ServerAssets.assetFilenames())
    }
    @Test fun `les args CLI exacts du serveur`() {
        val dir = File("/tmp/x")
        assertEquals(listOf(
            "--datadir", "/tmp/x/jf-server/data",
            "--configdir", "/tmp/x/jf-server/config",
            "--cachedir", "/tmp/x/jf-server/cache",
            "--logdir", "/tmp/x/jf-server/logs",
            "--webdir", "/tmp/x/jf-server/web",
            "--ffmpeg", "/tmp/x/jf-server/bin/ffmpeg"
        ), ServerAssets.buildArgs(dir))
    }
}
```
Run `:app:testDebugUnitTest` → FAIL (ServerAssets absente).
- [ ] **Step 2 : Implémentation `ServerAssets.kt`** :
```kotlin
package org.jellyfin.shieldserver

import java.io.File

/** Catalogue des assets embarqués et construction de la ligne de commande du serveur. */
object ServerAssets {
    fun assetFilenames(): List<String> = listOf(
        "server/jellyfin-server.tar.xz",
        "web/jellyfin-web.tar.xz",
        "bin/ffmpeg.xz",
        "bin/ffprobe.xz",
    )

    fun buildArgs(filesDir: File): List<String> = listOf(
        "--datadir", "${filesDir}/jf-server/data",
        "--configdir", "${filesDir}/jf-server/config",
        "--cachedir", "${filesDir}/jf-server/cache",
        "--logdir", "${filesDir}/jf-server/logs",
        "--webdir", "${filesDir}/jf-server/web",
        "--ffmpeg", "${filesDir}/jf-server/bin/ffmpeg",
    )
}
```
Run tests → PASS.
- [ ] **Step 3 : Implémentation du service (extraction + lancement)**
`ServerService.kt` (contenu exact) :
```kotlin
package org.jellyfin.shieldserver

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.PowerManager
import android.util.Log
import org.apache.commons.compress.archivers.tar.TarArchiveEntry
import org.apache.commons.compress.archivers.tar.TarArchiveInputStream
import org.apache.commons.compress.compressors.xz.XZCompressorInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import kotlin.concurrent.thread

/** Service gère l'extraction des assets puis lance le serveur .NET, avec redémarrage en backoff. */
class ServerService : Service() {
    private var process: Process? = null
    private var running = false
    private var backoffMs = 5_000L
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        nm.createNotificationChannel(NotificationChannel(CHANNEL_ID, "Jellyfin Server", NotificationManager.IMPORTANCE_LOW))
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForeground()
        if (running) {
            return START_STICKY          // déjà en route (process OU extraction) : pas de second process — revue
        }
        running = true
        wakeLock = (getSystemService(POWER_SERVICE) as PowerManager)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "$TAG:wakelock").apply { acquire() }
        thread {
            try {
                ensureExtracted()
                startProcessLoop()
            } catch (e: Exception) {
                notifyError("démarrage : ${e.javaClass.simpleName} ${e.message ?: ""}")
            }
        }
        return START_STICKY
    }

    override fun onDestroy() {
        running = false
        process?.destroy()
        wakeLock?.release()
        super.onDestroy()
    }

    /** Notification foreground immédiate : le service reste « started » pendant l'extraction lente. */
    private fun startForeground() {
        val n = Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("Jellyfin Server Shield")
            .setContentText("Démarrage du serveur…")
            .setOngoing(true)
        startForeground(NOTIF_ID, n.build())
    }

    private fun ensureExtracted() {
        val serverDir = File(filesDir, "jf-server")
        val marker = File(serverDir, ".extracted-stamp")
        val wanted = assets.open(STAMP_ASSET).bufferedReader().readText().trim()
        if (marker.isFile && marker.readText().trim() == wanted) return
        serverDir.deleteRecursively()
        serverDir.mkdirs()
        extractTar(assets.open("server/jellyfin-server.tar.xz"), serverDir)
        extractTar(assets.open("web/jellyfin-web.tar.xz"), File(serverDir, "web"))
        ServerAssets.assetFilenames().filter { it.startsWith("bin/") }.forEach {
            val target = File(serverDir, it.removeSuffix(".xz"))
            target.parentFile?.mkdirs()          // jf-server/bin n'est créé par aucun tar : mkdirs explicite (revue)
            target.outputStream().use { out ->
                assets.open(it).use { XZCompressorInputStream(it).copyTo(out) }
            }
            target.setExecutable(true, false)
        }
        marker.writeText(wanted)
    }

    private fun extractTar(stream: java.io.InputStream, targetDir: File) {
        targetDir.mkdirs()
        TarArchiveInputStream(XZCompressorInputStream(stream)).use { tar ->
            while (true) {
                val entry: TarArchiveEntry = tar.nextTarEntry ?: break
                val out = File(targetDir, entry.name)
                if (entry.isDirectory) out.mkdirs()
                else {
                    out.parentFile?.mkdirs()
                    FileOutputStream(out).use { tar.copyTo(it) }
                    if (entry.mode and 0b001000000 != 0) out.setExecutable(true, false)
                }
            }
        }
    }

    private fun startProcessLoop() {
        backoffMs = 5_000L
        while (running) {
            val args = listOf(File(filesDir, "jf-server/jellyfin").path) + ServerAssets.buildArgs(filesDir)
            val env = mapOf(
                "DOTNET_GCHeapHardLimit" to "536870912",   // 512 Mo : RAM 3 Go constatée (G0)
                "DOTNET_SystemGlobalizationInvariant" to "1",
                "DOTNET_EnableWriteXorExecute" to "0",
            )
            try {
                process = ProcessBuilder(args).apply {
                    directory(File(filesDir, "jf-server"))
                    environment().putAll(env)
                    redirectErrorStream(true)
                }.start()
                notifyRunning()
                process!!.inputStream.bufferedReader().forEachLine { line -> Log.i(TAG, line) }
                val exit = process!!.waitFor()
                if (!running) return
                notifyError("serveur arrêté (exit $exit) — relance dans ${backoffMs / 1000} s")
                Thread.sleep(backoffMs)
                if (backoffMs < 60_000L) backoffMs *= 2
            } catch (e: Exception) {
                notifyError("lancement : ${e.message ?: e.javaClass.simpleName}")
                return
            }
        }
    }

    private fun notifyRunning() {
        val n = Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("Jellyfin Server Shield")
            .setContentText("Serveur actif sur :8096")
            .setOngoing(true)
        startForeground(NOTIF_ID, n.build())
    }

    private fun notifyError(text: String) {
        startForeground(NOTIF_ID, Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("Jellyfin Server Shield")
            .setContentText("Problème : $text")
            .setOngoing(true).build())
        Handler(Looper.getMainLooper()).post {
            MainActivity.postMessage(text)
        }
    }

    companion object {
        private const val TAG = "jf-server"
        private const val CHANNEL_ID = "jf-server"
        private const val NOTIF_ID = 1
        const val STAMP_ASSET = "server/.stamp"
    }
}
```
- [ ] **Step 4 : Tests + build** — `./gradlew :app:testDebugUnitTest` (2 tests) + `:app:assembleDebug` → SUCCESSFUL.
- [ ] **Step 5 : Commit** — `git add app-server && git commit -m "feat(server): runtime Kotlin du serveur embarqué (extraction, process, watchdog)"`

### Task 4: UI de contrôle, démarrage au boot, manifest complet

**Files:**
- Create: `app-server/app/src/main/kotlin/org/jellyfin/shieldserver/BootReceiver.kt`
- Modify: `app-server/app/src/main/kotlin/org/jellyfin/shieldserver/MainActivity.kt` (contrôle réel : état du service + bouton démarrer/arrêter + affichage IP:port + note libx264)
- Modify: `app-server/app/src/main/AndroidManifest.xml` (service, receiver, permissions)
- Test: aucun (UI éphémère, pas de logique pure — vérification manuelle en T5)

**Interfaces:**
- `BootReceiver` : `ACTION_BOOT_COMPLETED` → `context.startForegroundService(Intent(ctx, ServerService::class.java))` (au boot ≥ API 26, service context OK depuis receiver).
- `MainActivity.postMessage(text: String)` : Handler main → TextView statut (consommé par `ServerService.notifyError`).
- IP affichée via `NetworkInterface` (aucune permission requise — WifiManager rejeté par la revue).
- Manifest (bloc gated exact) :
```xml
<uses-permission android:name="android.permission.FOREGROUND_SERVICE"/>
<uses-permission android:name="android.permission.RECEIVE_BOOT_COMPLETED"/>
<uses-permission android:name="android.permission.WAKE_LOCK"/>
<application ...>
    <service android:name=".ServerService"
             android:exported="false"
             android:foregroundServiceType="dataSync"/>
    <receiver android:name=".BootReceiver" android:exported="true">
        <intent-filter>
            <action android:name="android.intent.action.BOOT_COMPLETED"/>
        </intent-filter>
    </receiver>
</application>
```
*(l'exécutant : `foregroundServiceType` ignoré sous API < 29 — n'impacte pas minSdk 26 ; `dataSync` nécessite la permission FOREGROUND_SERVICE_DATA_SYNC seulement quand targetSdk ≥ 34 — non applicable à targetSdk 26.)*

- [ ] **Step 1 : `BootReceiver.kt` (contenu exact)**
```kotlin
package org.jellyfin.shieldserver

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/** Redémarre le service du serveur au boot de la Shield. */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED) {
            context.startForegroundService(Intent(context, ServerService::class.java))
        }
    }
}
```
- [ ] **Step 2 : `MainActivity.kt` UI complète (contenu exact)**
```kotlin
package org.jellyfin.shieldserver

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.Button
import android.widget.TextView
import java.net.InetAddress
import kotlin.concurrent.thread
import java.net.NetworkInterface

class MainActivity : Activity() {
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var status: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val root = android.widget.LinearLayout(this).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            setPadding(64, 64, 64, 64)
        }
        status = TextView(this).apply { text = "Arrêté" ; textSize = 16f }
        val start = Button(this).apply { text = "Démarrer le serveur" }
        val stop = Button(this).apply { text = "Arrêter le serveur" }
        val admin = TextView(this).apply {
            text = "Limitation MVP : ffmpeg sans libx264 → transcodage logiciel x264/x265 inopérant (HW = G4)."
            textSize = 12f
        }
        root.addView(status); root.addView(start); root.addView(stop); root.addView(admin)
        setContentView(root)

        start.setOnClickListener {
            startForegroundService(Intent(this, ServerService::class.java))
            setStatus("démarrage…")
        }
        stop.setOnClickListener {
            stopService(Intent(this, ServerService::class.java))
            setStatus("Arrêté")
        }
        thread { postMessage(serveurUrl()) }   // lecture réseau IO hors thread UI (revue)
        setStatus("Arrêté")
    }

    override fun onStart() {
        super.onStart()
        mainActivity = this
        thread { postMessage(serveurUrl()) }
    }

    override fun onStop() { mainActivity = null; super.onStop() }

    private fun serveurUrl(): String {
        val ip = NetworkInterface.getNetworkInterfaces().asSequence()
            .flatMap { it.inetAddresses.asSequence() }
            .firstOrNull { !it.isLoopbackAddress && it is InetAddress && !it.isLinkLocalAddress && it.hostAddress?.contains(':') == false }
            ?.hostAddress ?: "<ip-inconnue>"
        return "UI du serveur : http://$ip:8096"
    }

    fun setStatus(text: String) {
        handler.post { status.text = text }
    }

    companion object {
        private var mainActivity: MainActivity? = null
        /** Appelé depuis ServerService (thread d'arrière-plan) pour refléter l'état à l'écran. */
        fun postMessage(text: String) { mainActivity?.setStatus(text) }
    }
}
```
- [ ] **Step 3 : Vérifier** — build + install sur l'appareil (interactif) ; après démarrage : `curl http://$JF_SHIELD_IP:8096/health` depuis le PC → HTTP 200 (curl PC, pas de curl device) ; reboot TV → service relancé au boot.
- [ ] **Step 4 : Commit** — `git add app-server && git commit -m "feat(server): interface de contrôle, boot receiver et manifest du service"`

### Task 5: Déploiement on-device (smoke test + critères spec §9)

**Files:**
- Create: `deployment/apk-android/install-server.sh` (install + start + probe santé HTTP)
- Create: `deployment/apk-android/check-stability.sh` (collecte du journal + courbes de vie, exécuté par le propriétaire après 24 h)

**Interfaces:**
- Install/production : `JF_SHIELD_IP` requis (connecté via `tools/probe/connect.sh`) ; le service est démarré par l'activité (MainActivity) ; le script installe, relance puis vérifie la santé HTTP depuis le PC.

**Checklist du smoke test (step 3, critères spec §9) — chaque point notifié au propriétaire :**
1. serveur répond sur :8096 (curl PC `http://$IP:8096/health` → HTTP 200) ;
2. UI web accessible : le propriétaire ouvre `http://$IP:8096` dans le navigateur PC et finalise l'assistant (langue, utilisateur, bibliothèques) ;
3. bibliothèque « Vidéos test » pointée sur `/storage/AX58U/TOSHIBA_EXT` (ou chemin du volume G1) : scan terminé (≥ 10 éléments visibles) ;
4. lecture direct (direct play) d'un média depuis le navigateur PC : la lecture démarre sans « Transcodage » affiché dans le dashboard.

- [ ] **Step 1 : Script d'installation (contenu exact — dry-run style sonde ; skill `expert-bash` à charger)**
```bash
#!/usr/bin/env bash
# installation et vérification du serveur embarqué sur la Shield (JF_SHIELD_IP requis).
set -euo pipefail
DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck disable=SC1090,SC1091
. "$DEPLOY_DIR/../../tools/probe/lib.sh"   # réutilisation de adb_target/shell_cmd de la sonde
if [ "${DRY_RUN:-1}" -eq 1 ]; then
  log 'dry-run : adb install, force-stop, am start MainActivity (démarrage du service par l app), curl /health PC.'
  exit 0
fi
adb_target
run_adb install -r "$(cd "$DEPLOY_DIR/../../app-server" && pwd)/app/build/outputs/apk/debug/app-debug.apk"
run_adb shell am force-stop org.jellyfin.shieldserver
run_adb shell am start -n org.jellyfin.shieldserver/.MainActivity
log "IP de la Shield : ${JF_SHIELD_IP:?}"
sleep 20
log "santé : $(curl -s -o /dev/null -w '%{http_code}' "http://${JF_SHIELD_IP:?}:8096/health" || echo indéterminée)"
log "ouvrir http://$JF_SHIELD_IP:8096 dans le navigateur PC pour l'assistant (10 fichiers test bibliothèque)."
```
- [ ] **Step 2 : Script de rapport 24 h (contenu exact)**
```bash
#!/usr/bin/env bash
# vérification de stabilité 24 h : état des process + santé HTTP ; <fichier-résultats> attendu en $1.
set -euo pipefail
DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck disable=SC1090,SC1091
. "$DEPLOY_DIR/../../tools/probe/lib.sh"
RESULTS_FILE="${1:?fichier résultats (gitignored) attendu en argument 1}"
if [ "${DRY_RUN:-1}" -eq 1 ]; then
  log 'dry-run : collecte uptime + /health du serveur.'; exit 0
fi
adb_target
{
  echo "## process Android (ps -A | grep app/serveur) ($(date +%F_%T))"
  shell_cmd 'ps -A | grep -E "org\\.jellyfin\\.shieldserver|jellyfin" || echo "(processus absent : probablement tué — constat de stabilité)"'
  echo "## santé http : $(curl -s -o /dev/null -w '%{http_code}' "http://${JF_SHIELD_IP:?}:8096/health")"
} > "$RESULTS_FILE"
log "collecte : $RESULTS_FILE"
```
- [ ] **Step 3 : Smoke test on-device** — checklist (Step 3 ci-dessus) cochée item par item avec le propriétaire.
- [ ] **Step 4 : Commit** — `git add deployment/apk-android && git commit -m "test(server): scripts d'installation et de collecte 24 h sur appareil"`

### Task 6: Documentation finale + validation portée G3

**Files:**
- Modify: `docs/jellyfin-shield/README.md` (section « serveur embarqué » : installation, enregistrement bibliothèque, exclusion batterie, réglage 24 h)
- Modify: `docs/superpowers/specs/2026-10-25-jellyfin-shield-hw-design.md` (statut : incrémente v1.3 « G3 livré : critères 24 h validés par le propriétaire, logs joints »)
- Validate disponible : logs de stabilité + `probe-data` de test (gitignored).

**Interfaces:**
- Produces : spec/amendement v1.3 ; README à jour ; G3 validé ou pivot documenté (si criterion 24 h inattendue, la spec §9 note le plan de reprise).

- [ ] **Step 1 : README (section exacte)**
```markdown
## Serveur embarqué (G3)

1. `DRY_RUN=0 bash deployment/apk-android/collect-assets.sh` (assets : serveur publishé + web + ffmpeg ; défaut = dry-run).
2. `cd app-server && ./gradlew :app:assembleDebug`
3. `bash deployment/apk-android/install-server.sh` — l'UI s'ouvre sur la TV : bouton Démarrer.
4. Ouvrez `http://<ip>:8096` sur le navigateur PC et terminez l'assistant (bibliothèque sur le volume `/storage/AX58U/...`, sous-titres externes recommandés).
5. Exclusion d'optimisation batterie : `Paramètres → Apps → Jellyfin Server Shield → Batterie` (aucune restriction) ; démarré au boot via Boot receiver ; 24 h : `bash deployment/apk-android/check-stability.sh <fichier résultats>`.
Limitation documentée : transcodage logiciel x264/x265 inopérant au MVP (ffmpeg de sonde sans libx264) ; transcodage HW = G4.
```
- [ ] **Step 2 : Vérification 24 h** — collecte + lecture des logs du propriétaire : server up, pas de kill LMK non-rémédiable ; en cas de kill : rapport pivot/consigne dans la spec v1.3, sans modification d'architecture.
- [ ] **Step 3 : Commit** — `git add docs && git commit -m "docs(shield): G3 livré — critères de stabilité et procédure utilisateur"`

## Self-review du plan (normes writing-plans)

1. **Couverture de la spec §9** : publication serveur → T2 ; bundling web + ffmpeg → T2 ; extraction + runtime + watchdog → T3 ; UI/manifest/boot → T4 ; critères on-device (8096, web UI, scan bibliothèque, direct play, 24 h) → T5 ; docs + validation → T6 ✓. Hors périmètre respecté (aucune tâche ne patch HardwareAccelerationType/EncodingHelper = G4 ; aucune exploration nvtranscode = G5).
2. **Sans placeholder** : contenus Gradle/Kotlin/bash réels fournis ; tous les blocs bash sont syntaxiquement valides (`bash -n`), le trap du test report est non destructif, les noms d'assets/dirs sont cohérents entre les tâches.
3. **Cohérence des noms** : `ServerAssets.buildArgs(filesDir)`, `ServerAssets.assetFilenames()`, `org.jellyfin.shieldserver`, assets `server/web/bin` (`.stamp` 16 Hex), dirs `jf-server/{data,config,cache,logs,web,bin}`, binaire `jellyfin` à la racine du runtime, `deployment/apk-android/collect-assets.sh|install-server.sh|check-stability.sh` identiques à travers les tâches ✓.
4. **Prérequis hôte documentés** : dotnet 10 (T2 Step 0), node/npm ✓, gradle wrapper (T1 Step 1), NDK (non requis pour app-server : pas de JNI), `$PROBE_DIR/out/*` binaires sonde consommés en **T2** (collect-assets).
5. **Tests** : tests JVM ServerAssets (T3), scripts shellcheck/dry-run (T2, T5), smoke on-device = checklist conduite avec le propriétaire (T5 step 3).






