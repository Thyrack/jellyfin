package org.jellyfin.shieldserver

import java.io.File

/** Copie récursive pure (java.io) — migration datadir vers le volume large (Lot 3). */
object DirCopy {

    /**
     * Copie récursive du contenu de src vers dst (dst créé au besoin).
     * Fail-fast : rend false à la première erreur, arbre partiel conservé tel quel
     * (la cause est remontée via [erreurSurvenue] pour diagnostic).
     * Hypothèse documentée : arborescence sans liens symboliques (java.io les
     * suivrait → récursion infinie possible) ; aucun lien dans les données Jellyfin.
     *
     * @param src dossier source existant
     * @param dst dossier cible (nom complet, pas seulement le parent)
     * @param erreurSurvenue callback diagnostic (non bloquant) appelé à chaque échec
     * @return vrai si tout a été copié sans erreur
     */
    fun copyTree(src: File, dst: File, erreurSurvenue: (String) -> Unit = {}): Boolean {
        if (!src.isDirectory) {
            erreurSurvenue("source non dossier : ${src.path}")
            return false
        }
        if (!dst.isDirectory && !dst.mkdirs()) {
            erreurSurvenue("mkdirs échoué : ${dst.path}")
            return false
        }
        val entrees = src.listFiles() ?: run {
            erreurSurvenue("listFiles()=null (IO/permission) : ${src.path}")
            return false
        }
        entrees.forEach { entrant ->
            val cible = File(dst, entrant.name)
            if (entrant.isDirectory) {
                if (!copyTree(entrant, cible, erreurSurvenue)) return false
            } else if (!copieFichier(entrant, cible, erreurSurvenue)) return false
        }
        return true
    }

    /** Copie protégée d'un fichier individuel (false sur toute exception IO). */
    private fun copieFichier(src: File, dst: File, erreurSurvenue: (String) -> Unit): Boolean = try {
        src.inputStream().use { entree -> dst.outputStream().use { entree.copyTo(it) } }
        true
    } catch (e: Exception) {
        erreurSurvenue("${src.path} → ${e.javaClass.simpleName} ${e.message ?: ""}")
        false
    }
}
