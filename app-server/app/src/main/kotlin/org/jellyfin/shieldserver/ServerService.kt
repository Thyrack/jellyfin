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
import java.util.Date
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.concurrent.thread

/** Service gère l'extraction des assets puis lance le serveur .NET, avec redémarrage en backoff. */
class ServerService : Service() {
    private var process: Process? = null
    @Volatile private var running = false
    private var worker: Thread? = null   // revue : interruptible depuis onDestroy
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
        if (intent?.action == StorageProbeReceiver.ACTION_RUNFF) {
            if (!matriceEnCours.compareAndSet(false, true)) {
                Log.i(TAG, "matrice mux déjà en cours")
                return START_STICKY
            }
            executerMatriceMux(intent)
            return START_STICKY
        }
        if (running) {
            return START_STICKY          // déjà en route (process OU extraction) : pas de second process — revue
        }
        running = true
        wakeLock = (getSystemService(POWER_SERVICE) as PowerManager)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "$TAG:wakelock").apply { acquire() }
        worker = thread {
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
                syncTls(dir)
                syncPlugin(DataDirSelector.choisir(filesDir).datadir)
                val cibles = dir.listFiles()?.fold("") { acc, f ->
                    if (f.name.contains("e_sqlite") || f.name.contains("Skia") || f.name.endsWith("dotnet") || f.name.endsWith("jellyfin.dll") || f.name.contains("Cryptography"))
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
        worker?.interrupt()   // revue : coupe le backoff/attente du thread worker
        process?.destroy()
        pidActif = null
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

    /**
     * Substitution TLS (G4-bis) : les IL android de System.Security.Cryptography /
     * System.Net.Security (provider AndroidCrypto sans JVM → Interop.AndroidCrypto.
     * SslException) sont remplacées par les IL linux du pack runtime, appariées aux
     * deux providers natifs embarqués (dlopen BoringSSL, cf. tlsprovider/). Appelé
     * après l'extraction du tar à CHAQUE démarrage (le stamp tar ne change pas —
     * l'override doit survivre à toute ré-extraction). La comparaison contenu
     * (diff+copie) vit dans TlsSyncer (M6-revue) : on ne réécrit pas un fichier
     * déjà identique.
     */
    private fun syncTls(serverDir: File) {
        val duos = ServerAssets.assetFilenames()
            .filter { it.startsWith("tlsprovider/") }
            .map { it to it.substringAfterLast('/') }
        val tlsSyncer = TlsSyncer(
            source = { nom -> assets.open(nom) },
            lireCible = { nomCible -> File(serverDir, nomCible).takeIf(File::isFile)?.readBytes() },
            ecrireCible = { nomCible, contenu -> File(serverDir, nomCible).outputStream().use { it.write(contenu) } },
        )
        val remplaces = tlsSyncer.synchroniser(duos) { nom, e ->
            Log.w(TAG, "syncTls : $nom indisponible : ${e.message}")
        }
        remplaces.forEach {
            Log.i(TAG, "syncTls remplacé : ${it.asset.substringAfterLast('/')} (${it.taille} o)")
        }
        Log.i(TAG, "syncTls : ${remplaces.size} fichier(s) TLS remplacé(s)")
    }

    /**
     * Diffusion plugin (métriques host) : décompresse l'asset XZ embarqué et compare
     * byte-à-byte chaque entrée avec la cible <datadir>/plugins/HostMetrics/ ; réécrit
     * si le contenu diffère. Fail-open (motif TlsSyncer) : un asset indisponible n'empêche
     * jamais le démarrage du serveur.
     */
    private fun syncPlugin(datadir: File) {
        try {
            val pluginDir = File(datadir, "plugins/HostMetrics").apply { mkdirs() }
            assets.open("plugin/hostmetrics-plugin.tar.xz").use { flux ->
                TarArchiveInputStream(XZCompressorInputStream(flux)).use { tar ->
                    var ecrits = 0
                    while (true) {
                        val entry = tar.nextTarEntry ?: break
                        if (entry.isDirectory) continue
                        val cible = File(pluginDir, entry.name)
                        cible.parentFile?.mkdirs()
                        val contenu = tar.readBytes()
                        if (!cible.isFile || !cible.readBytes().contentEquals(contenu)) {
                            cible.outputStream().use { it.write(contenu) }
                            ecrits++
                        }
                    }
                    Log.i(TAG, "syncPlugin : $ecrits fichier(s) synchronisés dans ${pluginDir.path}")
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "syncPlugin : plugin métriques indisponible : ${e.message}")
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
            val choix = DataDirSelector.choisir(filesDir)
            val args = listOf(executable) + preArgs + ServerAssets.buildArgs(filesDir, choix)
            Log.i(TAG, "datadir=${choix.datadir.path} (${if (choix.externe) "externe" else "interne"})")
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
                pidActif = android.os.Process.myPid()
                lancerVeilleHw()
                // télémétrie du run : dernières lignes retenues pour un fichier lisible via run-as (diagnostic T5)
                val sb = StringBuilder()
                val runLog = File(filesDir, "jf-server/last-run.log")
                proc.inputStream.bufferedReader().forEachLine { line ->
                    Log.i(TAG, line)
                    if (sb.length < 12_000) sb.appendLine(line)
                }
                runLog.writeText(sb.toString())
                val exit = proc.waitFor()
                pidActif = null
                if (!running) return
                notifyError("serveur arrêté (exit $exit) — relance dans ${backoffMs / 1000} s")
                try {
                    Thread.sleep(backoffMs)
                } catch (_: InterruptedException) {
                    return   // onDestroy : interruption = demande d'arrêt, pas une erreur
                }
                if (backoffMs < 60_000L) backoffMs *= 2
            } catch (e: Exception) {
                pidActif = null
                notifyError("lancement : ${e.message ?: e.javaClass.simpleName}")
                return
            }
        }
    }

    /** Environnement imposé au process .NET : plafond GC 384 Mo (ramasse moins d'OOM : constat crash 20:53 — le killer mémoire Android a sacrifié le process dotnet le plus gros RSS sur MemFree 55 Mo ; Plex laissé actif par choix utilisateur), globalisation invariante (nom de variable en MAJUSCULES officiel : la forme CamelCase était ignorée par le runtime — constat smoke I), W^X désactivé (exec d'IL sous Android), racine runtime = bundle (le muxer termux résout host/fxr via DOTNET_ROOT), LD_LIBRARY_PATH = bundle (le dlopen bionic des natives P/Invoke — e_sqlite3 etc. — ne cherche PAS le répertoire courant, constat smoke II), TMPDIR = bundle/temp (le géré lit cette var POSIX ; /tmp read-only dans le sandbox app — constat bibliothèque). */
    private fun serverEnv(serverDir: File): Map<String, String> = mapOf(
        "DOTNET_GCHeapHardLimit" to "402653184",
        "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" to "1",
        "DOTNET_EnableWriteXorExecute" to "0",
        "DOTNET_ROOT" to serverDir.path,
        "LD_LIBRARY_PATH" to serverDir.path,
        "TMPDIR" to serverTempDir(filesDir).path,
    )

    /** Exécute un cas de la matrice avec timeout + journal + fichiers de sortie. */
    private fun executerCas(cas: MediaMuxProbe.Cas, journal: File, outDir: File) {
        val ffmpeg = File(File(filesDir, "jf-server"), "bin/ffmpeg")
        var proc: Process? = null
        try {
            journal.appendText("run ${cas.nom} …\n")
            Log.i(TAG, "run ${cas.nom}")
            proc = ProcessBuilder(listOf(ffmpeg.path) + cas.args)
                .redirectErrorStream(true)
                .start()
            val queue = ArrayDeque<String>()
            val lecteur = thread(name = "mux-reader", isDaemon = true) {
                proc.inputStream.bufferedReader().forEachLine { ligne ->
                    if (ligne.contains("frame=") || ligne.startsWith("[")) {
                        if (queue.size >= 3) queue.removeFirst()
                        queue.addLast(ligne.take(160))
                    }
                }
            }
            val fini = proc.waitFor(MATRICE_TIMEOUT_S, TimeUnit.SECONDS)
            if (!fini) {
                // ffmpeg bloqué (frame=0 du dead-lock mux HW) : destruction IMMÉDIATE,
                // sinon le zombie fuit CPU + instances HW et entache voisins (revue).
                proc.destroyForcibly()
                journal.appendText("STALLED (> $MATRICE_TIMEOUT_S s) — process détruit ;\n")
            } else {
                journal.appendText("exit=${proc.exitValue()} ;\n")
            }
            lecteur.join(1_000) // le lecteur mourra sur EOF après la destruction du process
            journal.appendText("dernière stat : ${queue.lastOrNull() ?: "aucune"}\n")
            journal.appendText("fichiers : ${outDir.listFiles()?.filter { it.name.startsWith("probe") }
                ?.joinToString(", ") { it.name } ?: "(vide)"}\n")
        } catch (e: Exception) {
            proc?.destroyForcibly()
            journal.appendText("exception : ${e.javaClass.simpleName} ${e.message ?: ""}\n")
            Log.e(TAG, "run ${cas.nom} : ${e.javaClass.simpleName} ${e.message ?: ""}")
        }
    }

    /** Intervalle d'échantillonnage de la veille (décision basée sur ce délai réel). */
    private val INTERVALLE_VEILLE_S = 2.0

    /** Veille anti-boucle : échantillonne les process ffmpeg de l'uid de l'app et
     *  active un kill si un usage CPU dépasse le plafond [FfmpegWatchdog.SEUIL_TICKS_PAR_SEC]
     *  (deadlock des instances HW MediaCodec concomitantes, constat run réel ~230× un cœur). */
    private fun lancerVeilleHw() {
        thread(name = "veille-hw", isDaemon = true) {
            val echantillons = mutableMapOf<Int, Long>()
            try {
                while (running && pidActif != null) {
                    try {
                        verifierProcessFfmpeg(echantillons)
                    } catch (e: Exception) {
                        Log.w(TAG, "veille-hw : itération reprise : ${e.javaClass.simpleName} ${e.message ?: ""}")
                    }
                    Thread.sleep((INTERVALLE_VEILLE_S * 1_000).toLong())
                }
            } catch (e: InterruptedException) {
                // onDestroy : sortie propre
            }
        }
    }

    /** Un passage d'échantillonnage : kill au-dedssus du plafond, purge des PIDs disparus. */
    private fun verifierProcessFfmpeg(echantillons: MutableMap<Int, Long>) {
        val pids = enfantsFfmpeg()
        for ((pid, ticks) in pids) {
            verifierPid(pid, echantillons[pid], ticks)
            echantillons[pid] = ticks
        }
        echantillons.keys.retainAll(pids.keys)
    }

    /** Décide du kill d'un PID d'après l'échantillon précédent (aucun kill sans historique). */
    private fun verifierPid(pid: Int, precedent: Long?, ticks: Long) {
        if (precedent == null) return
        if (!FfmpegWatchdog.decider(precedent, ticks, INTERVALLE_VEILLE_S)) return
        Log.w(TAG, "veille-hw : kill $pid (boucle CPU probable, ${((ticks - precedent) / INTERVALLE_VEILLE_S).toInt()} ticks/s)")
        val kill = Runtime.getRuntime().exec(arrayOf("kill", "-9", pid.toString()))
        try {
            val exit = kill.waitFor()
            if (exit != 0) Log.w(TAG, "veille-hw : kill $pid a échoué (exit $exit)")
        } finally {
            kill.destroy()
        }
    }

    /** Échantillon CPU des process `ffmpeg` visibles par l'uid de l'app → map PID → utime+stime. */
    private fun enfantsFfmpeg(): Map<Int, Long> {
        val pids = mutableMapOf<Int, Long>()
        for (dossier in File("/proc").listFiles { f -> !f.name.startsWith(".") } ?: emptyArray()) {
            val pid = dossier.name.toIntOrNull() ?: continue
            val cmdline = runCatching { File(dossier, "cmdline").readBytes() }.getOrNull() ?: continue
            val stat = runCatching { File(dossier, "stat").readText() }.getOrNull() ?: continue
            val ticks = FfmpegWatchdog.ticksProcess(stat) ?: continue
            if (FfmpegWatchdog.estProcessFfmpeg(String(cmdline, Charsets.UTF_8))) pids[pid] = ticks
        }
        return pids
    }

    /** Matrice mux/encodage HW exécutée DANS le service (lieu vivant hors broadcast, ANR-immune). */
    private fun executerMatriceMux(intent: Intent?) {
        val src = intent?.getStringExtra(StorageProbeReceiver.SRC_EXTRA) ?: StorageProbeReceiver.SRC_DEFAUT
        val journal = File(filesDir, "jf-server/matrice.log")
        thread(name = "mux-matrice", isDaemon = true) {
            try {
                val outDir = File(DataDirSelector.RACINE_EXTERNE, "cache/transcodes")
                journal.appendText("== matrice mux src=$src (${Date()}) ==\n")
                Log.i(TAG, "matrice mux : src=$src")
                for (cas in MediaMuxProbe.matrice(src, File(outDir, "probe/live").path)) {
                    executerCas(cas, journal, outDir)
                }
                journal.appendText("== fin matrice ==\n")
                Log.i(TAG, "fin matrice (cf. jf-server/matrice.log)")
            } catch (e: Exception) {
                Log.e(TAG, "matrice : ${e.javaClass.simpleName} ${e.message ?: ""}")
            } finally {
                matriceEnCours.set(false)
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
        /** Anti-doublon de la matrice de diagnostic mux (un seul exécutant à la fois). */
        private var matriceEnCours = AtomicBoolean(false)
        /** Délai maximal d'un run de la matrice de diagnostic (45 s). */
        private const val MATRICE_TIMEOUT_S = 45L
        const val STAMP_ASSET = "server/.stamp"
        /** PID du process hôte hébergeant `dotnet` (null = aucun) ; utilisé par le récepteur COPY_DATA comme garde anti copie à chaud (marque de vie posée/retirée au cycle de vie du service, pas d'API système stale). */
        @Volatile var pidActif: Int? = null
    }
}
