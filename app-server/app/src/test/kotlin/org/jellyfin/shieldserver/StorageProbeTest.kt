package org.jellyfin.shieldserver

import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Tests de la sonde stockage (logique pure, dossiers réels du runtime JVM). */
class StorageProbeTest {

    @Test fun dossier_avec_fichiers_rend_OK_avec_nombre_et_noms() {
        val racine = File(System.getProperty("java.io.tmpdir"))
        val cible = File(racine, "probe-${System.nanoTime()}").apply { mkdirs() }
        File(cible, "film1.mkv").writeText("x")
        File(cible, "film2.mkv").writeText("x")
        File(cible, "nomsolo.txt").writeText("x")
        try {
            val r = StorageProbe.sonder(cible.absolutePath)
            assertEquals("OK", r.lecture)
            assertTrue(r.detail.contains("3 entrées"))
            assertTrue(r.detail.contains("film1.mkv"))
        } finally { cible.deleteRecursively() }
    }

    @Test fun chemin_inexistant_rend_NON_DOSSIER() {
        val r = StorageProbe.sonder("/chemin/absolument/inexistant-12345")
        assertEquals("NON-DOSSIER", r.lecture)
    }

    @Test fun nom_controle_le_nombre_d_entrees() {
        val cible = File(File(System.getProperty("java.io.tmpdir")), "probe-empty-${System.nanoTime()}")
            .apply { mkdirs() }
        try {
            val r = StorageProbe.sonder(cible.absolutePath)
            assertEquals("OK", r.lecture)
            assertTrue(r.detail.startsWith("0 entrées"))
        } finally { cible.deleteRecursively() }
    }

    @Test fun chemin_donnee_prive_rejete() {
        assertFalse(StorageProbe.cheminAutorise("/data/user/0/org.jellyfin.shieldserver/files"))
    }

    @Test fun chemin_stockage_public_autorise() {
        assertTrue(StorageProbe.cheminAutorise("/storage/AX58U/TOSHIBA_EXT/videos/Films"))
        assertTrue(StorageProbe.cheminAutorise("/storage/emulated/0"))
    }

    @Test fun chemins_prives_d_autres_apps_rejetes() {
        assertFalse(StorageProbe.cheminAutorise("/storage/emulated/0/Android/data/com.autre.app/files"))
        assertFalse(StorageProbe.cheminAutorise("/storage/emulated/0/Android/obb/com.autre.app"))
        assertFalse(StorageProbe.cheminAutorise("/storage/emulated/0/Android/data"))
        assertFalse(StorageProbe.cheminAutorise("/storage/emulated/0/Android/obb/"))
        assertTrue(StorageProbe.cheminAutorise("/storage/emulated/0/Android/dataX/boucle"))
    }

    @Test fun traversée_par_points_relatifs_rejetée() {
        assertFalse(StorageProbe.cheminAutorise("/storage/emulated/0/../../../data/data"))
    }
}
