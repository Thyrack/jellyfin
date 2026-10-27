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
import java.io.FileOutputStream
import java.io.IOException
import kotlin.concurrent.thread

/** Service gère l'extraction des assets puis lance le serveur .NET, avec redémarrage en backoff. */
class ServerService : Service() {
    private var process: Process? = null
    @Volatile private var running = false
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
                val stat = android.os.StatFs(filesDir.path)
                Log.i(TAG, "démarrage : espace libre ${(stat.availableBytes / 1_048_576)} Mo, stamp assets tiroir")
                ensureExtracted()
                Log.i(TAG, "extraction OK : ${File(filesDir, "jf-server").listFiles()?.size ?: -1} entrées")
                val dir = File(filesDir, "jf-server")
                // TMPDIR : Path.GetTempPath() du géré lit cette var POSIX ; /tmp est read-only
                // dans le sandbox App Android (constat bibliothèque : IOException « Read-only file
                // system : '/tmp' » sur le Dynamic Image Provider).
                val tempDir = serverTempDir(filesDir)
                if (!tempDir.mkdirs() && !tempDir.isDirectory) {
                    throw IOException("Création du répertoire temp impossible : ${tempDir.path}")
                }

                syncNatives(dir)
                val cibles = dir.listFiles()?.fold("") { acc, f ->
                    if (f.name.contains("e_sqlite") || f.name.contains("Skia") || f.name.endsWith("dotnet") || f.name.endsWith("jellyfin.dll"))
                        acc + ";" + f.name + ":" + f.length()
                    else acc
                }
                Log.i(TAG, "syncNatives OK — fichiers clés : $cibles")
                startProcessLoop()
            } catch (e: Exception) {
                Log.e(TAG, "échec de démarrage", e)
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
        Log.i(TAG, "extraction fresh : ${serverDir.listFiles()?.size ?: 0} entrées, ${serverDir.walkTopDown().count()} fichiers")
    }

    /**
     * Extraction des natives embarquées : Android 11 interdit de lister `nativeLibraryDir`
     * (constat smoke III : listFiles renvoie null), on lit donc directement l'APK (ZipInputStream)
     * et on copie chacune des extensions point-shared-object du répertoire lib arm64-v8a vers le
     * répertoire du runtime, à côté de jellyfin.dll (CoreCLR sonde le répertoire de l'application).
     */
    /** Répertoire temporaire du serveur : source unique pour la création et TMPDIR (revue). */
    private fun serverTempDir(filesDir: File): File = File(filesDir, "jf-server/temp")

    private fun syncNatives(serverDir: File) {
        serverDir.mkdirs()
        var copies = 0
        java.util.zip.ZipInputStream(File(applicationInfo.sourceDir).inputStream()).use { zip ->
            while (true) {
                val entry = zip.nextEntry ?: break
                if (entry.name.startsWith("lib/arm64-v8a/") && entry.name.endsWith(".so")) {
                    val target = File(serverDir, File(entry.name).name)
                    target.outputStream().use { zip.copyTo(it) }
                    Log.i(TAG, "syncNatives copie ${target.name} (${target.length()} o)")
                    copies++
                }
                zip.closeEntry()
            }
        }
        Log.i(TAG, "syncNatives : $copies natives copiées depuis l'APK")

        // Substitution crypto : le .so officiel exige une JVM attachée (SIGSEGV sans, constat smoke T5
        // tombstone) ; stub v2 embarqué : empreintes/HMAC/PBKDF2 via BoringSSL, RNG via /dev/urandom,
        // échec propre ailleurs.
        try {
            val stub = assets.open("cryptostub/libSystem.Security.Cryptography.Native.Android.so")
            File(serverDir, "libSystem.Security.Cryptography.Native.Android.so").outputStream().use { out ->
                stub.use { input -> input.copyTo(out) }
            }
            Log.i(TAG, "cryptostub v2 déployé : empreintes/HMAC/PBKDF2 via BoringSSL, RNG via /dev/urandom")
        } catch (e: Exception) {
            Log.w(TAG, "cryptostub indisponible : ${e.message} — le .so original jettera SIGSEGV au premier usage crypto")
        }
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
            val serverDir = File(filesDir, "jf-server")
            val (executable, preArgs) = ServerAssets.entryCommand(serverDir)
            val args = listOf(executable) + preArgs + ServerAssets.buildArgs(filesDir)
            Log.i(TAG, "ProcessBuilder : $executable $preArgs")
            val env = serverEnv(serverDir)
            try {
                process = ProcessBuilder(args).apply {
                    directory(File(filesDir, "jf-server"))
                    environment().putAll(env)
                    redirectErrorStream(true)
                }.start()
                notifyRunning()
                val proc = process!!
                // télémétrie du run : dernières lignes retenues pour un fichier lisible via run-as (diagnostic T5)
                val sb = StringBuilder()
                val runLog = File(filesDir, "jf-server/last-run.log")
                proc.inputStream.bufferedReader().forEachLine { line ->
                    Log.i(TAG, line)
                    if (sb.length < 12_000) sb.appendLine(line)
                }
                runLog.writeText(sb.toString())
                val exit = proc.waitFor()
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

    /** Environnement imposé au process .NET : plafond GC 512 Mo (RAM 3 Go constatée en G0), globalisation invariante (nom de variable en MAJUSCULES officiel : la forme CamelCase était ignorée par le runtime — constat smoke I), W^X désactivé (exec d'IL sous Android), racine runtime = bundle (le muxer termux résout host/fxr via DOTNET_ROOT), LD_LIBRARY_PATH = bundle (le dlopen bionic des natives P/Invoke — e_sqlite3 etc. — ne cherche PAS le répertoire courant, constat smoke II), TMPDIR = bundle/temp (le géré lit cette var POSIX ; /tmp read-only dans le sandbox app — constat bibliothèque). */
    private fun serverEnv(serverDir: File): Map<String, String> = mapOf(
        "DOTNET_GCHeapHardLimit" to "536870912",
        "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" to "1",
        "DOTNET_EnableWriteXorExecute" to "0",
        "DOTNET_ROOT" to serverDir.path,
        "LD_LIBRARY_PATH" to serverDir.path,
        "TMPDIR" to serverTempDir(filesDir).path,
    )

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
