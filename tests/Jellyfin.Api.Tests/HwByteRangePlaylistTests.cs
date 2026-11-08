using System;
using System.Linq;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.MediaEncoding;
using Xunit;

namespace Jellyfin.Api.Tests;

/// <summary>
/// Tests du générateur de playlist HLS byte-range (G5-E v14) : timeline théorique
/// complète (barre = durée totale), type VOD, ENDLIST à la fin du job ; les blocs
/// d'avance sont servis par streaming incrémental (+ re-anchor sur seek lointain).
/// </summary>
public static class HwByteRangePlaylistTests
{
    private const long TestDurationTicks = 120_000_000_000L; // 12 000 s (200 min)
    private const long TestVideoBitrate = 1_116_000;
    private const long TestAudioBitrate = 192_000;
    private const double TestBlockSeconds = 6.0;

    private static TranscodingJob MakeJob(bool hasExited = false)
        => new(Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<TranscodingJob>>())
        {
            HasExited = hasExited,
            HwByteRange = true,
            HwByteRangeAnchorOffset = 0,
            HwByteRangeAnchorTimeSec = 0,
        };

    private static long TotalBytesForTicks(long ticks)
        => (long)(ticks / 10_000_000.0 * (TestVideoBitrate + TestAudioBitrate) / 8.0);

    [Fact]
    public static void Publie_LaDureeTotale()
    {
        // G5-E v14 : la timeline théorique complète — la barre du player couvre le film.
        var text = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", "?api=k", 0L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var extinfCount = text.Split('\n').Count(line => line.StartsWith("#EXTINF", StringComparison.Ordinal));
        Assert.Equal(2000, extinfCount); // 12 000 s / 6 s
        Assert.Contains("#EXT-X-VERSION:4", text, StringComparison.Ordinal);
        Assert.Contains("#EXT-X-PLAYLIST-TYPE:VOD", text, StringComparison.Ordinal);
        Assert.Contains("#EXT-X-TARGETDURATION:7", text, StringComparison.Ordinal);
        Assert.Contains("hwbyterange/hash.ts?api=k", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#EXT-X-ENDLIST", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void Timeline_Stable_AuxReFetch()
    {
        // Les re-fetch du playlist produisent STRICTEMENT la même liste (le VOD ne
        // mute pas, même si l'encodage a progressé — le contenu suit via le serving).
        var t1 = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", null, 0L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);
        var t2 = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", null, 29_430_000L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        Assert.Equal(t1, t2, StringComparer.Ordinal);
    }

    [Fact]
    public static void TotalEstimate_ResteLaValeurDuFilm()
    {
        var job = MakeJob();
        HwByteRangePlaylistBuilder.Build(job, "hash", null, 90_000_000L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        Assert.Equal(TotalBytesForTicks(TestDurationTicks), job.HwByteRangeTotalEstimateBytes);
    }

    [Fact]
    public static void CouvertureContinue_SansTrou()
    {
        // Les entrées se suivent sans trou : la fin d'une entrée = le début de la suivante.
        var text = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", null, TotalBytesForTicks(TestDurationTicks), TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var ranges = text.Split('\n').Where(line => line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal)).ToList();
        Assert.Equal(2000, ranges.Count); // 12 000 s / 6 s
        long lastEnd = 0;
        foreach (var range in ranges)
        {
            var spec = range["#EXT-X-BYTERANGE:".Length..];
            var at = spec.IndexOf('@', StringComparison.Ordinal);
            var len = long.Parse(spec[..at], System.Globalization.CultureInfo.InvariantCulture);
            var offset = long.Parse(spec[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(lastEnd, offset);
            Assert.True(len > 0);
            lastEnd = offset + len;
        }
    }

    [Fact]
    public static void QueryVide_PrefixedeQuestion()
    {
        // N6 : sans query (auth par header), le segment URL doit quand même avoir un
        // ? devant runtimeTicks (sinon 400 sur tous les blocs).
        var text = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", null, TotalBytesForTicks(TestDurationTicks), TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        Assert.Contains("hwbyterange/hash.ts?runtimeTicks=0&actualSegmentLengthTicks=60000000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hash.ts&runtimeTicks", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void JobTermine_EmetEndlist()
    {
        var text = HwByteRangePlaylistBuilder.Build(MakeJob(true), "hash", null, TotalBytesForTicks(TestDurationTicks), TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        Assert.Contains("#EXT-X-ENDLIST", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void Mapping_TempsOctets_Inverses()
    {
        // BytesAt et SecondsAt sont inverses l'un de l'autre.
        var offset = HwByteRangePlaylistBuilder.BytesAt(3_600_000_000L, TestVideoBitrate, TestAudioBitrate);
        var seconds = HwByteRangePlaylistBuilder.SecondsAt(offset, TestVideoBitrate, TestAudioBitrate);
        Assert.Equal(360.0, seconds, 3);
    }
}
