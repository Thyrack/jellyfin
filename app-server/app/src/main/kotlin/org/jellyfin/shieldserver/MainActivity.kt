package org.jellyfin.shieldserver

import android.app.Activity
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.util.Log
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

    companion object {
        private val mainHandler = Handler(Looper.getMainLooper())

        /** Poste un message de statut du service sur le thread principal (affichage réel en T4). */
        fun postMessage(text: String) {
            mainHandler.post { Log.i("jf-server", "statut : $text") }
        }
    }
}
