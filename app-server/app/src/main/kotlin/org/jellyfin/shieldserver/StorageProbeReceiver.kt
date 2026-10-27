package org.jellyfin.shieldserver

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.util.Log

/**
 * Récepteur debug de sonde stockage : lancé via `am broadcast` ADB pour tester,
 * depuis le vrai process de l'app, l'accès en lecture aux chemins médias
 * (armure invisible depuis `run-as`, qui a son propre contexte SELinux).
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
 * Usage ADB de la sonde :
 * ```
 * adb shell am broadcast -a org.jellyfin.shieldserver.PROBE_STORAGE -f 0x01000000 \
 *   --es paths "/storage/AX58U/TOSHIBA_EXT/videos/Films"
 * ```
 * Puis lire la logcat filtrée par `jf-storage-probe`.
 */
class StorageProbeReceiver : BroadcastReceiver() {

    /** Action ADB déclenchant la sonde. */
    companion object {
        const val ACTION = "org.jellyfin.shieldserver.PROBE_STORAGE"
        /** Plafond de chemins sondés par intent : la boucle tourne sur le thread principal. */
        private const val MAX_CHEMINS = 10
    }

    /** Sonde chaque chemin (au plus 10, séparés par ',') et journalise en logcat. */
    override fun onReceive(context: Context, intent: Intent) {
        if (context.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE == 0) {
            Log.w("jf-storage-probe", "IGNORÉ (build non debuggable)")
            return
        }
        val paths = intent.getStringExtra("paths") ?: "/storage/emulated/0"
        for (chemin in paths.split(',').take(MAX_CHEMINS)) {
            if (!StorageProbe.cheminAutorise(chemin)) {
                Log.w("jf-storage-probe", "REJETÉ $chemin (hors racines /storage/ sûres)")
                continue
            }
            val r = StorageProbe.sonder(chemin)
            Log.i("jf-storage-probe", "${r.lecture} ${r.chemin} → ${r.detail}")
        }
    }
}
