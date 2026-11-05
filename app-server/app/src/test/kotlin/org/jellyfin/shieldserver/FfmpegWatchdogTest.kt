package org.jellyfin.shieldserver

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Garde anti-boucle ffmpeg : le deadlock des instances HW MediaCodec concurrentes
 *  (constat run réel : job zombie à ~23 000 % CPU quand 2 jobs HW coexistent). */
class FfmpegWatchdogTest {

    @Test fun delta_fort_depasse_le_seuil_sous_limite_concurrente() {
        // 23 000 ticks en 2 s ≈ 11 500 ticks/s = 115× un cœur : boucle probable
        assertTrue(FfmpegWatchdog.decider(0, 23_000, 2.0))
    }

    @Test fun usage_legitime_hw_ne_declenche_pas_le_kill() {
        // encodage HW mesuré ≈ 2,5 cœurs = 250 ticks/s ; x264 SW saturé ≈ 800 ticks/s (8 cœurs)
        assertFalse(FfmpegWatchdog.decider(0, 250, 1.0))
        assertFalse(FfmpegWatchdog.decider(0, 800, 1.0))
    }

    @Test fun secondes_non_positives_interdisent_la_décision() {
        assertFalse(FfmpegWatchdog.decider(0, 23_000, 0.0))
        assertFalse(FfmpegWatchdog.decider(0, 23_000, -2.0))
    }

    @Test fun pid_reutilisé_delta_négatif_est_ignoré() {
        assertFalse(FfmpegWatchdog.decider(500_000, 1_000, 2.0))
    }

    @Test fun cmdline_ffmpeg_est_reconnu_selon_le_premier_token() {
        val cmdline = "/data/user/0/org.jellyfin.shieldserver/files/jf-server/bin/ffmpeg\u0000-analyzeduration\u0000"
        assertTrue(FfmpegWatchdog.estProcessFfmpeg(cmdline))
        assertFalse(FfmpegWatchdog.estProcessFfmpeg("/system/bin/dotnet\u0000-i\u0000"))
        assertFalse(FfmpegWatchdog.estProcessFfmpeg("/system/bin/sh\u0000"))
    }

    @Test fun cmdline_sans_nul_terminal_est_toléré_si_argv0_est_ffmpeg() {
        assertTrue(FfmpegWatchdog.estProcessFfmpeg("/data/.../bin/ffmpeg"))      // límite : détecté sans NUL
        assertTrue(FfmpegWatchdog.estProcessFfmpeg("/data/.../bin/ffmpeg\u0000"))
    }

    @Test fun stat_ffmpeg_rend_utime_plus_stime() {
        val stat = "23818 (ffmpeg) S 1102 23818 0 0 -1 4194560 0 0 0 0 6500 300 0 0 20 5 20 0 12345 0 0 0 0 0 0 0 0 0 0 0 0"
        assertEquals(6800L, FfmpegWatchdog.ticksProcess(stat))
    }

    @Test fun stat_malformé_rend_null() {
        assertNull(FfmpegWatchdog.ticksProcess("23818 (ff"))
        assertNull(FfmpegWatchdog.ticksProcess("23818 (ffmpeg) S 1 2 3"))
    }
}
