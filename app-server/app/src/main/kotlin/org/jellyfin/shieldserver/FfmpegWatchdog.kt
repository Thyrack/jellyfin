package org.jellyfin.shieldserver

import java.io.File

/**
 * Garde anti-boucle pour les process ffmpeg du serveur embarqué.
 *
 * Constat run réel G4 : quand deux jobs accélérés MediaCodec coexistent, un ffmpeg
 * peut partir en boucle utilisateur (~230× un cœur) sans produire de segment — le
 * kill est le seul moyen de rendre la main au TranscodeManager.
 *
 * Le seuil (1 500 ticks/s, soit 15 cœurs) est très au-dessus de tout usage légitime
 * (encodage HW mesuré ≈ 2,5 cœurs ; x264 logiciel saturé ≈ 8 cœurs) : il ne frappe
 * que les boucles du pilote MediaCodec sous contention.
 */
object FfmpegWatchdog {

    /** Seuil par défaut : ticks CPU (100 par seconde et par cœur) au-delà du quel on kill. */
    const val SEUIL_TICKS_PAR_SEC = 1_500L

    /**
     * Décide du kill d'un process selon sa consommation entre deux échantillons.
     *
     * @param ticks1 total utime+stime du premier échantillon (/proc/pid/stat)
     * @param ticks2 total de l'échantillon suivant
     * @param secondes durée entre les échantillons (doit être > 0)
     * @param seuilTicksParSec plafond d'usage CPU toléré (défaut 15 cœurs)
     * @return vrai si le process dépasse le plafond (boucle suspecte)
     */
    fun decider(ticks1: Long, ticks2: Long, secondes: Double, seuilTicksParSec: Long = SEUIL_TICKS_PAR_SEC): Boolean {
        if (secondes <= 0.0) return false
        val delta = (ticks2 - ticks1).coerceAtLeast(0L)   // pid réutilisé → delta négatif ignoré
        return delta / secondes > seuilTicksParSec
    }

    /**
     * Vérifie que la ligne de commande d'un process est bien notre binaire ffmpeg.
     * Exigence d'exactitude : cmdline se termine par NUL et le premier token est argv[0].
     *
     * @param cmdline contenu brut de /proc/pid/cmdline
     * @return vrai si argv[0] est un ffmpeg
     */
    fun estProcessFfmpeg(cmdline: String): Boolean {
        val argv0 = cmdline.substringBefore('\u0000')
        return File(argv0).name == "ffmpeg"
    }

    /**
     * Extrait le total CPU (utime+stime) d'un contenu /proc/pid/stat.
     *
     * @param stat contenu brut de /proc/pid/stat
     * @return ticks cumulés, ou null si le format est inattendu
     */
    fun ticksProcess(stat: String): Long? {
        if (!stat.contains(')')) return null
        val champs = stat.substringAfterLast(") ").split(' ')
        val utime = champs.getOrNull(11)?.toLongOrNull() ?: return null   // champ 14 (utime)
        val stime = champs.getOrNull(12)?.toLongOrNull() ?: return null   // champ 15 (stime)
        return utime + stime
    }
}
