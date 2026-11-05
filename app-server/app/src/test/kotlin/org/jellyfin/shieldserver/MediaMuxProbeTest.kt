package org.jellyfin.shieldserver

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Matrice du diagnostic mux : 6 cas décode HW/CPU × encapsulateurs (G4-v2). */
class MediaMuxProbeTest {

    private fun matrice() = MediaMuxProbe.matrice("/ff", "/src.mkv", "/out/live")

    @Test fun matrice_contient_les_6_cas_ordonnés() {
        val noms = matrice().map { it.nom }
        assertEquals(
            listOf("decHW-encHW-fNull", "decHW-encHW-mp4", "decHW-encHW-fmp4",
                   "decHW-encHW-ts", "decCPU-encHW-fNull", "decCPU-encHW-ts"),
            noms)
    }

    @Test fun arguments_non_vides_pour_chaque_cas() {
        assertTrue(matrice().all { it.args.isNotEmpty() })
    }

    @Test fun sorties_distinctes_par_cas_hls() {
        val sorties = matrice().filter { it.nom.endsWith("fmp4") || it.nom.endsWith("ts") }
            .map { it.args.last() }
        assertEquals(3, sorties.toSet().size)
    }

    @Test fun cas_decHW_inclut_le_decodeur_hevc_mediacodec() {
        val decHW = matrice().filter { it.nom.startsWith("decHW") }
        assertTrue(decHW.all { "hevc_mediacodec" in it.args.joinToString(" ") })
        val decCPU = matrice().filter { it.nom.startsWith("decCPU") }
        assertTrue(decCPU.none { "hevc_mediacodec" in it.args.joinToString(" ") })
    }

    @Test fun mp4_et_ts_portent_les_chemins_dedies() {
        val mp4 = matrice().first { it.nom == "decHW-encHW-mp4" }
        assertEquals("/out/live.mp4", mp4.args.last())
        val ts = matrice().first { it.nom == "decHW-encHW-ts" }
        assertEquals("/out/live.ts.m3u8", ts.args.last())
    }
}
