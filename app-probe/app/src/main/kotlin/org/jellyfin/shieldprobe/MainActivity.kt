package org.jellyfin.shieldprobe

import android.app.Activity
import android.os.Bundle
import android.widget.TextView
import java.io.File
import kotlin.concurrent.thread

class MainActivity : Activity() {
    // Liste étendue d'après le constat de la sonde G0 on-device (/vendor/etc/public.libraries.txt) :
    // libnvmedia est le contrôle (non listée), nvtranscode/libnvos sont whitelisted côté vendor.
    private val TAGS = listOf(
        "libnvmedia.so",
        "libnvmpeg11.so",
        "libnvhost.so",
        "nvtranscode.so",
        "libnvos.so",
        "libnvgr.so",
        "libnvcpl_vendor.so",
        "libnvsi_ll_2.so",
        "libndkbinderutil.so",
        "libtsec_wrapper.so",
    )

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
