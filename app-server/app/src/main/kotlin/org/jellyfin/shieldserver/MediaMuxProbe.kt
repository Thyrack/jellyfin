package org.jellyfin.shieldserver

/**
 * Diagnostic de mux G5-A : matrice des tests d'encodage HW exécutés depuis le process
 * app (le contexte uid qui peut lire le média — le shell run-as lui-même est refusé).
 * Isole : encodage HW continu (routes fonctionnelles (RF)) / frontières de segments
 * (stall au même instant, ~3-5 s, hors matroska qui est un conteneur mono-fichier).
 */
object MediaMuxProbe {

    /** Un cas de la matrice : nom, arguments ffmpeg. */
    data class Cas(val nom: String, val args: List<String>)

    /**
     * Construit la matrice de diagnostic pour une source vidéo.
     *
     * @param source chemin du média source (lisible par le process app)
     * @param sortieM3u m3u8 de sortie (FUSE transcodes — sur le même chemin que Jellyfin)
     * @return 6 cas : 2 continus (RF = routes fonctionnelles) + 4 frontières bloquantes
     */
    fun matrice(source: String, sortieM3u: String): List<Cas> = listOf(
        Cas("continu-h264(RF)", listOf("-i", source, "-t", "5", "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "h264", "-y", "$sortieM3u.cont.h264")),
        Cas("continu-mpegts(RF)", listOf("-i", source, "-t", "5", "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "mpegts", "-y", "$sortieM3u.cont.ts")),
        Cas("frontiere-hls-fmp4", listOf("-i", source, "-t", "7", "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "hls", "-hls_time", "3", "-hls_segment_type", "fmp4", "-y", "$sortieM3u.m3u8")),
        Cas("frontiere-hls-ts", listOf("-i", source, "-t", "7", "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "hls", "-hls_time", "3", "-hls_segment_type", "mpegts", "-y", "$sortieM3u.ts.m3u8")),
        Cas("frontiere-mpegts-segmenté", listOf("-i", source, "-t", "9", "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "segment", "-segment_time", "3", "-segment_format", "mpegts", "-segment_start_number", "0", "-y", "$sortieM3u%03d.ts")),
        Cas("frontiere-matroska", listOf("-i", source, "-t", "5", "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "matroska", "-y", "$sortieM3u.mkv"))
    )
}
