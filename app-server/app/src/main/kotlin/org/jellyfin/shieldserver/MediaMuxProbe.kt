package org.jellyfin.shieldserver

import java.io.File

/**
 * Diagnostic mux G4 : matrice des tests d'encodage HW exécutés depuis le process app
 * (le contexte uid qui peut lire le média — le shell run-as lui-même est refusé).
 * Isole : décode HW/CPU × encapsulateur (null / mp4 / hls fmp4 / hls TS) × écriture FUSE.
 */
object MediaMuxProbe {

    /** Un cas de la matrice : nom, args ffmpeg, encapsulateur. */
    data class Cas(val nom: String, val args: List<String>)

    /**
     * Construit la matrice de diagnostic pour un ffmpeg + une source vidéo.
     *
     * @param ffmpeg chemin du binaire ffmpeg (bundle serveur)
     * @param source chemin du média source (lisible par le process app)
     * @param sortieM3u m3u8 de sortie (FUSE transcodes — sur le même chemin que Jellyfin)
     * @return 6 cas : décode HW/CPU × (null, mp4, fmp4, TS)
     */
    fun matrice(ffmpeg: String, source: String, sortieM3u: String): List<Cas> = listOf(
        Cas("decHW-encHW-fNull", listOf("-hwaccel", "mediacodec", "-c:v", "hevc_mediacodec", "-t", "6", "-i", source, "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "null")),
        Cas("decHW-encHW-mp4", listOf("-hwaccel", "mediacodec", "-c:v", "hevc_mediacodec", "-t", "6", "-i", source, "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-y", "$sortieM3u.mp4")),
        Cas("decHW-encHW-fmp4", listOf("-hwaccel", "mediacodec", "-c:v", "hevc_mediacodec", "-t", "8", "-i", source, "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "hls", "-hls_time", "3", "-hls_segment_type", "fmp4", "-y", "$sortieM3u.m3u8")),
        Cas("decHW-encHW-ts", listOf("-hwaccel", "mediacodec", "-c:v", "hevc_mediacodec", "-t", "8", "-i", source, "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "hls", "-hls_time", "3", "-hls_segment_type", "mpegts", "-y", "$sortieM3u.ts.m3u8")),
        Cas("decCPU-encHW-fNull", listOf("-t", "6", "-i", source, "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "null")),
        Cas("decCPU-encHW-ts", listOf("-t", "8", "-i", source, "-map", "0:v:0", "-c:v", "h264_mediacodec", "-b:v", "2M", "-f", "hls", "-hls_time", "3", "-hls_segment_type", "mpegts", "-y", "$sortieM3u.cpu.ts.m3u8"))
    )
}
