package org.jellyfin.shieldserver

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Matrice du diagnostic mux (G5-A) : routes d'encodage HW face aux frontières de segments. */
class MediaMuxProbeTest {

    private fun matrice() = MediaMuxProbe.matrice("/src.mkv", "/out/live")

    @Test fun matrice_contient_les_6_cas_ordonnés() {
        val noms = matrice().map { it.nom }
        assertEquals(
            listOf("continu-h264(RF)", "continu-mpegts(RF)", "frontiere-hls-fmp4",
                   "frontiere-hls-ts", "frontiere-mpegts-segmenté", "frontiere-matroska"),
            noms)
    }

    @Test fun arguments_non_vides_pour_chaque_cas() {
        assertTrue(matrice().all { it.args.isNotEmpty() })
    }

    @Test fun tout_cas_se_termine_par_un_chemin_de_sortie() {
        // -y suivi du chemin de sortie : sans ce chemin, ffmpeg refuserait l'écriture
        for (cas in matrice()) {
            val end = cas.args.takeLast(2)
            val estArgs = end[0] == "-y"
            val estChemin = end[1].isNotEmpty() && !end[1].startsWith("-")
            assertTrue("$cas", estArgs && estChemin)
        }
    }

    @Test fun encodeurHW_dans_tout_cas() {
        for (cas in matrice()) {
            assertTrue("$cas", "h264_mediacodec" in cas.args.joinToString(" "))
        }
    }

    @Test fun playlists_m3u8_distinctes_par_cas_hls() {
        val sorties = matrice().filter { it.nom.contains("hls") }
            .map { it.args.last() }
        assertEquals(2, sorties.toSet().size)
    }
}
