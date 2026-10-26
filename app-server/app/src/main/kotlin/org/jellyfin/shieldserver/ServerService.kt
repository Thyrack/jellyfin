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
                ensureExtracted()
                syncNatives(File(filesDir, "jf-server"))
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

    /**
     * Recopie les natives android-arm64 embarquées via jniLibs (extraites par l'installateur
     * vers le répertoire natif de l'APK) à la racine du bundle serveur : le processus enfant
     * lancé par ProcessBuilder ne résout pas ses DllImport dans le répertoire natif de l'APK,
     * alors que CoreCLR sonde le répertoire de l'application (e_sqlite3, SkiaSharp, HarfBuzz).
     */
    private fun syncNatives(serverDir: File) {
        val nativeDir = applicationInfo.nativeLibraryDir ?: return
        serverDir.mkdirs()
        File(nativeDir).listFiles { f -> f.isFile && f.name.endsWith(".so") }?.forEach { so ->
            val target = File(serverDir, so.name)
            if (!target.isFile || target.length() != so.length()) {
                so.inputStream().use { input -> target.outputStream().use { input.copyTo(it) } }
            }
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
            val env = serverEnv(serverDir)
            try {
                process = ProcessBuilder(args).apply {
                    directory(File(filesDir, "jf-server"))
                    environment().putAll(env)
                    redirectErrorStream(true)
                }.start()
                notifyRunning()
                val proc = process!!
                proc.inputStream.bufferedReader().forEachLine { line -> Log.i(TAG, line) }
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

    /** Environnement imposé au process .NET : plafond GC 512 Mo (RAM 3 Go constatée en G0), globalisation invariante (ICU non embarquée), W^X désactivé (exec d'IL sous Android), racine runtime = bundle (le muxer termux résout host/fxr via DOTNET_ROOT). */
    private fun serverEnv(serverDir: File): Map<String, String> = mapOf(
        "DOTNET_GCHeapHardLimit" to "536870912",
        "DOTNET_SystemGlobalizationInvariant" to "1",
        "DOTNET_EnableWriteXorExecute" to "0",
        "DOTNET_ROOT" to serverDir.path,
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
