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
