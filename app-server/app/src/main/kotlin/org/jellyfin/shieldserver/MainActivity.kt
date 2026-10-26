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
