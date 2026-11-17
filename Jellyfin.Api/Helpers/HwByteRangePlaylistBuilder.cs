using System;
using System.Globalization;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Générateur de playlist HLS byte-range pour les jobs byte-range HW (G5-E v19) :
/// le TS continu produit par le muxer mpegts (l'unique sortie délivrant les
/// paquets de l'encodeur mediacodec) est publié EVENT — la fenêtre = les blocs
/// de {blockSeconds} s complètement écrits (la playlist croît à chaque re-fetch,
/// ENDLIST à la fin du job). AVFoundation rejette toute playlist dont les blocs
/// finaux n'existent pas (la VOD théorique v14 a été rejetée au parse). La barre
/// des players Apple vient du metadata RunTimeTicks de la session ; les offsets
/// réagissent au serving via l'ancre du job et le re-anchor gère le seek
/// lointain pour les clients non-Apple (le TS progressatif ExoPlayer).
/// </summary>
public static class HwByteRangePlaylistBuilder
{
    /// <summary>
    /// Construit le texte de la playlist pour l'état courant du job.
    /// </summary>
    /// <param name="job">Le job de transcodage byte-range (ancre, fin, timeline).</param>
    /// <param name="fileName">Le nom du transcodage (sans extension) pour l'URL du segment.</param>
    /// <param name="queryString">La chaîne de requête à reproduire sur l'URL du segment (auth).</param>
    /// <param name="availableBytes">Les octets réellement écrits du TS continu — la fenêtre publiée (AVFoundation exige que chaque bloc annoncé existe).</param>
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

        // G5-E v20 : l'ancre du job porte la position GLOBALE du début du fichier TS
        // courant (démarrage à une position via startTimeTicks, re-anchor seek). Les
        // offsets et les runtimeTicks publiés sont globaux : la route hwbyterange les
        // ramène au fichier courant via localOffset = demandé − ancre (sinon 404).
        // NB : pas de clamp ici — la route utilise l'ancre brute ; un clamp divergerait.
        var anchorOffsetBytes = job.HwByteRangeAnchorOffset;
        var anchorTimeSec = job.HwByteRangeAnchorTimeSec;

        var builder = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:4\n");
        // G5-D (Apple v19) : la playlist EVENT ne publie que la fenêtre encodée —
        // AVFoundation exige que chaque bloc annoncé existe : la timeline théorique
        // VOD (v14) a été rejetée au parse par AVPlayer (0 fetch de range, constat
        // session 16:57). La fenêtre croît à chaque re-fetch (EVENT append-only,
        // ENDLIST à la fin du job). La barre des players Apple vient du metadata
        // RunTimeTicks de la session, pas du manifeste.
        builder.Append("#EXT-X-PLAYLIST-TYPE:EVENT\n");
        builder.Append("#EXT-X-TARGETDURATION:").Append((int)Math.Ceiling(blockSeconds) + 1).Append('\n');
        builder.Append("#EXT-X-MEDIA-SEQUENCE:0\n");

        // N6 : la query peut être vide (auth par header) — sans '?', le '&' du
        // premier paramètre glisse dans le path → 400 sur tous les blocs. Le
        // séparateur du premier append devient '?' si aucune query.
        var hasQuery = !string.IsNullOrEmpty(queryString);
        var separator = hasQuery ? "&" : "?";
        var segmentUrl = $"hwbyterange/{fileName}.ts{(hasQuery ? queryString : string.Empty)}";
        var availableSec = availableBytes * 8.0 / Math.Max(videoBitrate + audioBitrate, 1);
        var publishedSec = Math.Min(availableSec, Math.Max(totalDurationSec - anchorTimeSec, 0));
        var blocksPublished = (int)Math.Floor(publishedSec / blockSeconds);
        for (var i = 0; i < blocksPublished; i++)
        {
            var startSec = anchorTimeSec + (i * blockSeconds);
            var endSec = Math.Min(anchorTimeSec + ((i + 1) * blockSeconds), totalDurationSec);
            var localStartBytes = BytesAt((long)((startSec - anchorTimeSec) * 10_000_000), videoBitrate, audioBitrate);
            var localEndBytes = Math.Min(BytesAt((long)((endSec - anchorTimeSec) * 10_000_000), videoBitrate, audioBitrate), availableBytes);
            if (localEndBytes <= localStartBytes)
            {
                continue;   // garde anti bloc vide (coïncidence exacte clamp/offset)
            }

            AppendRangeEntry(
                builder,
                segmentUrl,
                separator,
                startSec,
                endSec,
                anchorOffsetBytes + localStartBytes,
                anchorOffsetBytes + localEndBytes);
        }

        // G5-E v20 : le reliquat final (< blockSeconds) est publié tel quel — sinon
        // la fin du média est injoignable via la playlist, et une ancre dans la
        // dernière fenêtre produisait une playlist VIDE (0 entrée EXTINF, rejet
        // AVFoundation immédiat, cf. v19). Émission uniquement si la fenêtre couvre
        // la fin du média (bloc terminal : taille stable, l'append-only est tenu) ;
        // la garde blocksPublished==0 est un filet défensif — après l'attente du
        // 1er chunk (≥ 7 s), une fenêtre non terminale produit toujours ≥ 1 bloc.
        var remainderStartSec = anchorTimeSec + (blocksPublished * blockSeconds);
        var remainderEndSec = Math.Min(anchorTimeSec + publishedSec, totalDurationSec);
        var coversMediaEnd = remainderEndSec >= totalDurationSec - 0.001;
        if ((blocksPublished == 0 || coversMediaEnd) && remainderEndSec > remainderStartSec)
        {
            var localRemStart = BytesAt((long)((remainderStartSec - anchorTimeSec) * 10_000_000), videoBitrate, audioBitrate);
            var localRemEnd = Math.Min(BytesAt((long)((remainderEndSec - anchorTimeSec) * 10_000_000), videoBitrate, audioBitrate), availableBytes);
            if (localRemEnd > localRemStart)
            {
                AppendRangeEntry(
                    builder,
                    segmentUrl,
                    separator,
                    remainderStartSec,
                    remainderEndSec,
                    anchorOffsetBytes + localRemStart,
                    anchorOffsetBytes + localRemEnd);
            }
        }

        if (job.HasExited)
        {
            builder.Append("#EXT-X-ENDLIST\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Émet une entrée de bloc (durée, range global, URL auto-descriptive).
    /// </summary>
    /// <param name="builder">Le flux de texte de la playlist.</param>
    /// <param name="segmentUrl">L'URL du segment (sans les paramètres de position).</param>
    /// <param name="separator">'?' si la query est vide, sinon '&amp;'.</param>
    /// <param name="startSec">Temps global de début du bloc (s).</param>
    /// <param name="endSec">Temps global de fin du bloc (s).</param>
    /// <param name="startOffset">Offset global de début du bloc (octets).</param>
    /// <param name="endOffset">Offset global de fin du bloc (octets).</param>
    private static void AppendRangeEntry(
        StringBuilder builder,
        string segmentUrl,
        string separator,
        double startSec,
        double endSec,
        long startOffset,
        long endOffset)
    {
        builder.Append("#EXTINF:")
            .Append((endSec - startSec).ToString("0.000", CultureInfo.InvariantCulture))
            .Append(",\n");
        builder.Append("#EXT-X-BYTERANGE:").Append(endOffset - startOffset).Append('@').Append(startOffset).Append('\n');
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
