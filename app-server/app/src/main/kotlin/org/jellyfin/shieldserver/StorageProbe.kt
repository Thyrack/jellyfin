package org.jellyfin.shieldserver

import java.io.File
import java.io.IOException

/**
 * Sonde pure de stockage : liste un dossier du process courant et rend un
 * rapport texte exploitable par la logcat (diagnostic d'accès aux médias,
 * entrave imposée aux bibliothèques Jellyfin par le scoped storage Android 11).
 *
 * Logique volontairement sans dépendance Android (java.io.File) pour être
 * testée unitairement sur JVM.
 */
object StorageProbe {

    /** Résultat d'une sonde sur un chemin : listable ? combien de fichiers, quels noms. */
    data class Result(val chemin: String, val lecture: String, val detail: String)

    /**
     * Sonde un dossier et rend un rapport court.
     *
     * @param chemin chemin absolu à tester
     * @return résultat sous forme [Result] (jamais null, fail-safe)
     */
    fun sonder(chemin: String): Result {
        val dossier = File(chemin)
        if (!dossier.isDirectory) return Result(chemin, "NON-DOSSIER", "listFiles()=null/inexistant")
        val fichiers = dossier.listFiles()
            ?: return Result(chemin, "ACCES-REFUSE", "listFiles()=null (SELinux/scoped storage)")
        val noms = fichiers.take(3).joinToString(", ") { it.name }
        return Result(chemin, "OK", "${fichiers.size} entrées : $noms")
    }

    /**
     * Autorise uniquement les racines de stockage exposées au scan des médias
     * (le récepteur est exported : toute app pourrait lui demander de lister des
     * répertoires privés). Canonisation obligatoire puis exclusion des répertoires
     * privés des autres apps (`Android/data`, `Android/obb`).
     *
     * @param chemin chemin proposé par l'appelant
     * @return vrai si le chemin est sûr à sonder
     */
    fun cheminAutorise(chemin: String): Boolean {
        val canonique = try { File(chemin).canonicalPath } catch (_: IOException) { return false }
        if (!canonique.startsWith("/storage/")) return false
        return !cRegexpPrive.containsMatchIn(canonique)
    }

    /** Sous-répertoires privés des autres apps interdits à la sonde. */
    private val cRegexpPrive = Regex("/storage/.*/(Android/data|Android/obb)(/|$)")
}
