package org.jellyfin.shieldserver

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.util.Log
import java.io.File
import kotlin.concurrent.thread

/**
 * Récepteur debug de sonde stockage : lancé via `am broadcast` ADB pour tester,
 * depuis le vrai process de l'app, l'accès en lecture/écriture aux chemins
 * médias (entrave invisible depuis `run-as`, qui a son propre contexte SELinux).
 * Neutralisé si l'app n'est pas debuggable (les APK release gardent le
 * récepteur déclaré mais inopérant).
 *
 * Octroi des permissions après installation (le lot ne les demande pas à
 * l'exécution ; l'usage ADB est documenté ici) :
 * ```
 * adb shell pm grant org.jellyfin.shieldserver android.permission.READ_EXTERNAL_STORAGE
 * adb shell appops set --user 0 org.jellyfin.shieldserver MANAGE_EXTERNAL_STORAGE allow
 * ```
 *
 * Usage ADB de la sonde de lecture :
 * ```
 * adb shell am broadcast -a org.jellyfin.shieldserver.PROBE_STORAGE -f 0x01000000 \
 *   --es paths "/storage/AX58U/TOSHIBA_EXT/videos/Films"
 * ```
 * Usage ADB de la migration datadir (Lot 3 — service arrêté préalablement, cf. README) :
 * ```
 * adb shell am broadcast -a org.jellyfin.shieldserver.COPY_DATA -f 0x01000020
 * ```
 * (0x01000020 = FLAG_RECEIVER_INCLUDE_BACKGROUND | FLAG_INCLUDE_STOPPED_PACKAGES : le
 * paquet est en état stopped après le force-stop.)
 * Puis lire la logcat filtrée par `jf-storage-probe`.
 */
class StorageProbeReceiver : BroadcastReceiver() {

    /** Étiquette logcat (filtre ADB `jf-storage-probe`). */
    companion object {
        const val TAGLOG = "jf-storage-probe"
        /** Action ADB déclenchant la sonde de lecture. */
        const val ACTION = "org.jellyfin.shieldserver.PROBE_STORAGE"
        /** Action ADB déclenchant la migration datadir+cache vers le volume externe. */
        const val ACTION_COPY = "org.jellyfin.shieldserver.COPY_DATA"
        /** Action ADB exécutant la matrice de diagnostic mux/encodage HW (G4-v2). */
        const val ACTION_RUNFF = "org.jellyfin.shieldserver.RUNFF"
        /** Nom de l'extra contenant le chemin média source (défaut : Le.Village). */
        const val SRC_EXTRA = "src"
        /** Chemin média par défaut pour la matrice (debug : Le.Village). */
        const val SRC_DEFAUT = "/storage/AX58U/TOSHIBA_EXT/videos/Films/Le.Village.2004.MULTi.VF2.1080p.BluRay.10.bits.AC3.5.1.x265-Winks.mkv"
        /** Plafond de chemins sondés par intent : la boucle tourne sur le thread principal. */
        private const val MAX_CHEMINS = 10
    }

    /** Migration data+cache : copie sur le volume large avec journalisation de progression.
     *  Budget goAsync : l'ANR du récepteur frappe ~60 s — passer au-delà sur un support
     *  lent laisse un arbre partiel SANS sentinelle (repli sûr sur l'interne). */
    private fun copierDonnees(context: Context, resultat: PendingResult) {
        thread(start = true) {
            try {
                if (ServerService.pidActif != null) {
                    Log.e(TAGLOG, "COPIE REFUSÉE : le service tourne (PID ${ServerService.pidActif}, base SQLite ouverte)")
                    return@thread
                }
                val racine = File(DataDirSelector.RACINE_EXTERNE)
                if (!racine.isDirectory && !racine.mkdirs()) {
                    Log.e(TAGLOG, "ÉCHEC mkdir $racine (écriture refusée ?)")
                    return@thread
                }
                val filesDir = context.filesDir
                val data = DataDirSelector.sourceData(filesDir)
                val cache = DataDirSelector.sourceCache(filesDir)
                Log.i(TAGLOG, "copie data : ${data.path} → $racine")
                val okData = DirCopy.copyTree(data, File(racine, "data")) { Log.e(TAGLOG, "data : $it") }
                Log.i(TAGLOG, "copie data → $okData")
                val okCache = DirCopy.copyTree(cache, File(racine, "cache")) { Log.e(TAGLOG, "cache : $it") }
                Log.i(TAGLOG, "copie cache → $okCache")
                if (okData && okCache) {
                    // Ré-fenêtre TOCTOU : si le service a démarré PENDANT la copie, les
                    // données copiées peuvent être déchirées → pas de sentinelle.
                    if (ServerService.pidActif != null) {
                        Log.e(TAGLOG, "service apparu pendant la copie → sentinelle refusée (repli interne)")
                    } else {
                        File(racine, DataDirSelector.SENTINELLE_MIGRATION).writeText("ok\n")
                        Log.i(TAGLOG, "sentinelle ${DataDirSelector.SENTINELLE_MIGRATION} posée")
                    }
                } else {
                    Log.e(TAGLOG, "migration incomplète → PAS de sentinelle (le datadir externe restera ignoré)")
                }
            } finally {
                resultat.finish()
            }
        }
    }

    /** Répartiteur : sonde de lecture ou migration datadir. */
    override fun onReceive(context: Context, intent: Intent) {
        if (context.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE == 0) {
            Log.w(TAGLOG, "IGNORÉ (build non debuggable)")
            return
        }
        if (intent.action == ACTION_COPY) copierDonnees(context, goAsync())
        else if (intent.action == ACTION_RUNFF) {
            // délégation au service (le broadcast goAsync est limité à ~60 s — la matrice dure plus)
            context.startForegroundService(Intent(context, ServerService::class.java).setAction(ACTION_RUNFF)
                .putExtra(SRC_EXTRA, intent.getStringExtra(SRC_EXTRA)))
        } else sonderChemins(intent)
    }

    /** Sonde de lecture : liste chaque chemin (au plus 10, séparés par ',') en logcat. */
    private fun sonderChemins(intent: Intent) {
        val paths = intent.getStringExtra("paths") ?: "/storage/emulated/0"
        for (chemin in paths.split(',').take(MAX_CHEMINS)) {
            if (!StorageProbe.cheminAutorise(chemin)) {
                Log.w(TAGLOG, "REJETÉ $chemin (hors racines /storage/ sûres)")
                continue
            }
            val r = StorageProbe.sonder(chemin)
            Log.i(TAGLOG, "${r.lecture} ${r.chemin} → ${r.detail}")
        }
    }
}
