package org.jellyfin.shieldprobe

import android.app.Activity
import android.os.Bundle
import android.widget.TextView
import java.io.File
import kotlin.concurrent.thread

class MainActivity : Activity() {
    private val TAGS = listOf("libnvmedia.so", "libnvmpeg11.so", "libnvhost.so")

    private external fun dlopenProbe(lib: String): String?

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val view = TextView(this).apply { text = "Sonde en cours…"; textSize = 16f }
        setContentView(view)
        thread {
            try {
                val report = ProbeReport.render({ lib -> dlopenProbe(lib) }, TAGS)
                val outDir = getExternalFilesDir(null)
                if (outDir != null) File(outDir, "g0-app-namespace.txt").writeText(report)
                runOnUiThread { view.text = report }
            } catch (e: Throwable) {
                runOnUiThread { view.text = "ERREUR : ${e.javaClass.simpleName}" }
            }
        }
    }

    companion object { init { System.loadLibrary("dlopen_probe") } }
}
