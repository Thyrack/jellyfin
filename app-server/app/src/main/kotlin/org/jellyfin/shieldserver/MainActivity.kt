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
