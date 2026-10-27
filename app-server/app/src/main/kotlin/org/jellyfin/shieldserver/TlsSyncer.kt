package org.jellyfin.shieldserver

import java.io.InputStream

/** Fichier TLS remplacé : nom de l'asset source et taille du contenu écrit. */
internal data class TlsFichierRemplace(val asset: String, val taille: Int)

/**
 * Diffusion TLS (G4-bis, M6-revue) : lecture conditionnelle des assets
 * tlsprovider/ et copie vers le répertoire du runtime, seulement si le contenu
 * change. Class pur (aucune dépendance Android) — l'IO (sources assets, cibles
 * fichiers) est injecté, ce qui le rend testable par JUnit sans appareil.
 *
 * Règle fail-open (revue) : un asset indisponible n'est jamais une erreur
 * bloquante ; le démarrage du serveur ne dépend pas de la multicast réussie.
 */
internal class TlsSyncer(
    private val source: (nomSource: String) -> InputStream,
    private val lireCible: (nomCible: String) -> ByteArray?,
    private val ecrireCible: (nomCible: String, contenu: ByteArray) -> Unit,
) {
    /**
     * Compare puis copie chaque duo (nomAsset → nomCible simple).
     * Retourne les orderly remplacés ; les absents passent par [faireObjection].
     */
    fun synchroniser(
        duos: List<Pair<String, String>>,
        faireObjection: (nomAsset: String, exception: Exception) -> Unit = { _, _ -> },
    ): List<TlsFichierRemplace> {
        val remplaces = mutableListOf<TlsFichierRemplace>()
        for ((asset, cible) in duos) {
            try {
                val nouveau = source(asset).use(InputStream::readBytes)
                val ancien = lireCible(cible)
                if (ancien == null || !ancien.contentEquals(nouveau)) {
                    ecrireCible(cible, nouveau)
                    remplaces += TlsFichierRemplace(asset, nouveau.size)
                }
            } catch (e: Exception) {
                faireObjection(asset, e)
            }
        }
        return remplaces
    }
}
