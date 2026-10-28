package org.jellyfin.shieldserver

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Tests du sélecteur datadir (stockage interne vs partition contenu 82 GiB). */
class DataDirSelectorTest {

    private fun tmpDir(nom: String) =
        File(File(System.getProperty("java.io.tmpdir")), "dds-$nom-${System.nanoTime()}").apply { mkdirs() }

    private fun racineFictive(avecMigration: Boolean): File {
        val racine = tmpDir("volume")
        File(racine, "data/data").apply { mkdirs() }
        if (avecMigration) {
            File(racine, "data/data/jellyfin.db").writeText("x")
            File(racine, DataDirSelector.SENTINELLE_MIGRATION).writeText("ok\n")
        }
        return racine
    }

    @Test fun migration_complete_choisit_part_externe() {
        val racine = racineFictive(avecMigration = true)
        val filesDir = tmpDir("fallback")
        try {
            val c = DataDirSelector.choisir(racine.absolutePath, filesDir)
            assertTrue(c.externe)
            assertEquals(File(racine, "data/data"), File(c.datadir, "data"))
            assertEquals(File(racine, "cache").absolutePath, c.cachedir.absolutePath)
        } finally { racine.deleteRecursively(); filesDir.deleteRecursively() }
    }

    @Test fun sans_sentinelle_choisit_part_interne_historique() {
        val racine = racineFictive(avecMigration = false)
        val filesDir = tmpDir("fallback")
        try {
            val c = DataDirSelector.choisir(racine.absolutePath, filesDir)
            assertFalse(c.externe)
            assertEquals(File(filesDir, "jf-server/data").absolutePath, c.datadir.absolutePath)
            assertEquals(File(filesDir, "jf-server/cache").absolutePath, c.cachedir.absolutePath)
        } finally { racine.deleteRecursively(); filesDir.deleteRecursively() }
    }

    @Test fun marqueur_sans_sentinelle_est_refuse() {
        val racine = racineFictive(avecMigration = false)
        File(racine, "data/data/jellyfin.db").writeText("db sans sentinelle")
        val filesDir = tmpDir("fallback")
        try {
            assertFalse(DataDirSelector.choisir(racine.absolutePath, filesDir).externe)
        } finally { racine.deleteRecursively(); filesDir.deleteRecursively() }
    }

    @Test fun sentinelle_sans_marqueur_est_refuse() {
        val racine = racineFictive(avecMigration = false)
        File(racine, DataDirSelector.SENTINELLE_MIGRATION).writeText("ok\n")
        val filesDir = tmpDir("fallback")
        try {
            assertFalse(DataDirSelector.choisir(racine.absolutePath, filesDir).externe)
        } finally { racine.deleteRecursively(); filesDir.deleteRecursively() }
    }

    @Test fun marqueur_dossier_est_refuse() {
        val racine = tmpDir("volume")
        File(racine, "data/data/jellyfin.db").apply { mkdirs() }
        val filesDir = tmpDir("fallback")
        try {
            assertFalse(DataDirSelector.choisir(racine.absolutePath, filesDir).externe)
        } finally { racine.deleteRecursively(); filesDir.deleteRecursively() }
    }

    @Test fun volume_absent_reste_sur_interne() {
        val filesDir = tmpDir("fallback")
        try {
            val c = DataDirSelector.choisir("/chemin/volume-inexistant-123", filesDir)
            assertFalse(c.externe)
        } finally { filesDir.deleteRecursively() }
    }
}
