using System;
using System.Globalization;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Générateur de playlist HLS byte-range pour les jobs byte-range HW (G5-E) :
/// le TS continu produit par le muxer mpegts (l'unique sortie délivrant les
/// paquets de l'encodeur mediacodec) est publié via une timeline théorique
/// complète — les entrées de {blockSeconds} s couvrent toute la durée du média
/// dès le premier fetch (la barre de progression du player est correcte et le
/// seek est possible partout). Les offsets sont estimés au bitrate cible ; le
/// serving mappe l'offset global estimé vers le fichier TS courant (ancre
/// {HwByteRangeAnchorOffset/HwByteRangeAnchorTimeSec}) et re-ancre le job
/// (re-encodage -ss) pour les positions hors fenêtre.
/// </summary>
public static class HwByteRangePlaylistBuilder
{
    /// <summary>
    /// Construit le texte de la playlist pour l'état courant du job.
    /// </summary>
    /// <param name="job">Le job de transcodage byte-range (ancre, fin, timeline).</param>
    /// <param name="fileName">Le nom du transcodage (sans extension) pour l'URL du segment.</param>
    /// <param name="queryString">La chaîne de requête à reproduire sur l'URL du segment (auth).</param>
    /// <param name="availableBytes">Réservé (n'influence plus la playlist depuis la v14 : la timeline complète est publiée).</param>
    /// <param name="totalDurationTicks">La durée totale du média en ticks (1 tick = 100 ns).</param>
    /// <param name="videoBitrate">Le bitrate vidéo cible (estimation des offsets).</param>
    /// <param name="audioBitrate">Le bitrate audio cible (estimation des offsets).</param>
    /// <param name="blockSeconds">La taille temporelle des entrées publiées (s).</param>
    /// <returns>Le texte de la playlist.</returns>
    public static string Build(
        TranscodingJob job,
        string fileName,
        string? queryString,
        long availableBytes,
        long totalDurationTicks,
        long videoBitrate,
        long audioBitrate,
        double blockSeconds = 6.0)
    {
        // G5-E v13 : la fenêtre publiée = les octets RÉELLEMENT écrits — le schéma
        // v5 validé côté app Android. Publier la timeline théorique complète faisait
        // demander à ExoPlayer des blocs d'avance (rythme encodage 0,7-1,3×), le
        // buffer s'effondrait et la session tournait en roue.
        job.HwByteRangeTotalEstimateBytes = BytesAt(totalDurationTicks, videoBitrate, audioBitrate);
        var totalDurationSec = totalDurationTicks / 10_000_000.0;

        var builder = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:4\n");
        // G5-E v14 : PLAYLIST-TYPE VOD + timeline théorique complète — la barre du
        // player affiche la durée totale du film et le seek est navigable partout.
        // (Le mode EVENT avait fait sauter les players au live-edge : bloc 6462 s
        // demandé pendant que le film en était à 5 s.)
        builder.Append("#EXT-X-PLAYLIST-TYPE:VOD\n");
        builder.Append("#EXT-X-TARGETDURATION:").Append((int)Math.Ceiling(blockSeconds) + 1).Append('\n');
        builder.Append("#EXT-X-MEDIA-SEQUENCE:0\n");

        // N6 : la query peut être vide (auth par header) — sans '?', le '&' du
        // premier paramètre glisse dans le path → 400 sur tous les blocs. Le
        // séparateur du premier append devient '?' si aucune query.
        var hasQuery = !string.IsNullOrEmpty(queryString);
        var separator = hasQuery ? "&" : "?";
        var segmentUrl = $"hwbyterange/{fileName}.ts{(hasQuery ? queryString : string.Empty)}";
        var blocks = (int)Math.Ceiling(totalDurationSec / blockSeconds);
        for (var i = 0; i < blocks; i++)
        {
            var startSec = i * blockSeconds;
            var endSec = Math.Min((i + 1) * blockSeconds, totalDurationSec);
            var startOffset = BytesAt((long)(startSec * 10_000_000), videoBitrate, audioBitrate);
            var endOffset = i == blocks - 1 ? job.HwByteRangeTotalEstimateBytes : BytesAt((long)(endSec * 10_000_000), videoBitrate, audioBitrate);
            var len = endOffset - startOffset;
            if (len <= 0)
            {
                continue;
            }

            builder.Append("#EXTINF:")
                .Append((endSec - startSec).ToString("0.000", CultureInfo.InvariantCulture))
                .Append(",\n");
            builder.Append("#EXT-X-BYTERANGE:").Append(len).Append('@').Append(startOffset).Append('\n');
            // Le handler du segment exige runtimeTicks/actualSegmentLengthTicks
            // ([Required], pattern Jellyfin natif : les URLs de segments sont
            // auto-descriptives) — l'absence renvoyait un 400 sur le premier bloc
            // et faisait avorter ExoPlayer (« source error »).
            builder.Append(segmentUrl)
                .Append(separator)
                .Append("runtimeTicks=").Append((long)(startSec * 10_000_000))
                .Append("&actualSegmentLengthTicks=").Append((long)((endSec - startSec) * 10_000_000))
                .Append('\n');
        }

        if (job.HasExited)
        {
            builder.Append("#EXT-X-ENDLIST\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Convertit un temps (ticks) en offset global estimé (octets) au bitrate cible.
    /// </summary>
    /// <param name="ticks">Le temps en ticks.</param>
    /// <param name="videoBitrate">Le bitrate vidéo cible.</param>
    /// <param name="audioBitrate">Le bitrate audio cible.</param>
    /// <returns>L'offset estimé en octets.</returns>
    public static long BytesAt(long ticks, long videoBitrate, long audioBitrate)
    {
        var totalBitrate = Math.Max(videoBitrate + audioBitrate, 1);
        return (long)(ticks / 10_000_000.0 * totalBitrate / 8.0);
    }

    /// <summary>
    /// Convertit un offset global estimé (octets) en temps (s) au bitrate cible.
    /// </summary>
    /// <param name="offset">L'offset en octets.</param>
    /// <param name="videoBitrate">Le bitrate vidéo cible.</param>
    /// <param name="audioBitrate">Le bitrate audio cible.</param>
    /// <returns>Le temps en secondes.</returns>
    public static double SecondsAt(long offset, long videoBitrate, long audioBitrate)
    {
        var totalBitrate = Math.Max(videoBitrate + audioBitrate, 1);
        return offset * 8.0 / totalBitrate;
    }
}
