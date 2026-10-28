package org.jellyfin.shieldserver

import java.io.File

/**
 * Choix des répertoires de données du serveur : la partition « contenu » interne
 * du Shield (VOLUME_EXTERNE, ~82 GiB libres) quand la migration y est installée,
 * sinon les chemins historiques du bundle.
 *
 * Garde anti-« bibliothèque vide » : le datadir externe n'est retenu que si
 * les DEUX gardes sont posées, à la fin complète de la migration :
 * 1. `.migration-ok` (sentinelle écrite par le récepteur COPY_DATA après avoir obtenu
 *    la copie intégrale de data et cache) ;
 * 2. `data/data/jellyfin.db` (marqueur de la base).
 * Si l'une manque : repli interne complet du bundle.
 *
 * Le choix est ré-évalué à chaque relance du serveur : les deux datadirs
 * peuvent donc diverger (l'interne reste figé à son état pré-migration, il ne
 * reçoit plus d'écriture une fois l'externe validé).
 */
object DataDirSelector {

    /** Volume large : partition « contenu » interne du Shield (229 GiB, 82 libres). */
    const val VOLUME_EXTERNE = "/storage/emulated/0"

    /** Racine des données Jellyfin migrées (visible dans l'app Files ; survit à la désinstall). */
    const val RACINE_EXTERNE = "$VOLUME_EXTERNE/JellyfinServer"

    /** Nom de la sentinelle posée après copie intégrale (Lot 3). */
    const val SENTINELLE_MIGRATION = ".migration-ok"

    /** Répertoires retenus pour le lancement du serveur. */
    data class Choix(val datadir: File, val cachedir: File, val externe: Boolean)

    /**
     * Sélection datadir/cachedir.
     *
     * @param volumeRacine racine du volume candidat (chemin absolu, injectée pour tests)
     * @param fallbackFilesDir filesDir du bundle (chemins historiques)
     * @return [Choix] jamais null : externe si sentinelle+marqueur présents, interne sinon
     */
    fun choisir(volumeRacine: String, fallbackFilesDir: File): Choix {
        val externeData = File(volumeRacine, "data")
        val externeCache = File(volumeRacine, "cache")
        val externe = File(externeData, "data/jellyfin.db").isFile &&
            File(volumeRacine, SENTINELLE_MIGRATION).isFile
        if (externe) return Choix(externeData, externeCache, externe = true)
        return Choix(sourceData(fallbackFilesDir), sourceCache(fallbackFilesDir), externe = false)
    }

    /** Choix par défaut (volume courant de la migration). */
    fun choisir(fallbackFilesDir: File): Choix = choisir(RACINE_EXTERNE, fallbackFilesDir)

    /** Datadir source historique (ce que le récepteur COPY_DATA copie vers l'externe). */
    fun sourceData(filesDir: File): File = File(filesDir, "jf-server/data")

    /** Cachedir source historique. */
    fun sourceCache(filesDir: File): File = File(filesDir, "jf-server/cache")
}
