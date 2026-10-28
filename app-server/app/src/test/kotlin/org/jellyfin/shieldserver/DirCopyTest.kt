package org.jellyfin.shieldserver

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Tests de la copie récursive utilisée par la migration datadir. */
class DirCopyTest {

    private fun tmpRacine(nom: String) =
        File(File(System.getProperty("java.io.tmpdir")), "dircopy-$nom-${System.nanoTime()}").apply { mkdirs() }

    @Test fun arbre_simple_est_copie_avec_contenu_identique() {
        val src = tmpRacine("src")
        val dstRacine = tmpRacine("dst")
        File(src, "film.mkv").writeText("abc")
        try {
            assertTrue(DirCopy.copyTree(src, File(dstRacine, "data")))
            assertEquals("abc", File(dstRacine, "data/film.mkv").readText())
        } finally { src.deleteRecursively(); dstRacine.deleteRecursively() }
    }

    @Test fun arbre_imbrique_sur_plusieurs_niveaux() {
        val src = tmpRacine("src")
        val dstRacine = tmpRacine("dst")
        File(src, "a/b/c").apply { mkdirs() }
        File(src, "a/b/c/db.sqlite").writeText("données")
        try {
            assertTrue(DirCopy.copyTree(src, File(dstRacine, "data")))
            assertEquals("données", File(dstRacine, "data/a/b/c/db.sqlite").readText())
        } finally { src.deleteRecursively(); dstRacine.deleteRecursively() }
    }

    @Test fun source_non_dossier_rend_false_et_ne_cree_larbo_dst() {
        val racine = tmpRacine("src")
        val sentinel = File(racine, "fichier")
        sentinel.writeText("x")
        try {
            var cause = ""
            val ok = DirCopy.copyTree(sentinel, File(racine, "data")) { cause = it }
            assertFalse(ok)
            assertFalse(cause.isEmpty())
            assertFalse(File(racine, "data").exists())
        } finally { racine.deleteRecursively() }
    }

    @Test fun destination_preexistante_fusion_sans_erreur() {
        val src = tmpRacine("src")
        val dst = tmpRacine("dst")
        File(src, "film.mkv").writeText("v2")
        File(dst, "data").mkdirs()
        File(dst, "data/film.mkv").writeText("v1")
        try {
            assertTrue(DirCopy.copyTree(src, File(dst, "data")))
            assertEquals("v2", File(dst, "data/film.mkv").readText())
        } finally { src.deleteRecursively(); dst.deleteRecursively() }
    }
}
