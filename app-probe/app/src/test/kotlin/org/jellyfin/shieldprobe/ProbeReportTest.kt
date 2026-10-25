package org.jellyfin.shieldprobe

import org.junit.Assert.assertEquals
import org.junit.Test

class ProbeReportTest {
    @Test
    fun `une librairie chargée sans erreur rend OK`() {
        assertEquals("OK\tlibx.so", ProbeReport.render({ null }, listOf("libx.so")))
    }

    @Test
    fun `un dlopen en échec rend ÉCHEC suivi du nom de la librairie`() {
        assertEquals("ÉCHEC\tlibx.so", ProbeReport.render({ "motif non atteignable" }, listOf("libx.so")))
    }

    @Test
    fun `deux librairies rendent deux lignes séparées par retour à la ligne`() {
        assertEquals("OK\tL1\nOK\tL2", ProbeReport.render({ null }, listOf("L1", "L2")))
    }
}
