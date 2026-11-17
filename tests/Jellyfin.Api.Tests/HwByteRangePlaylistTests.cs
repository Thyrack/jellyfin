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
    public static void Publie_LaFenetreEncodee_Event()
    {
        // G5-D (Apple v19) : le player Apple (AVFoundation) rejette toute playlist
        // VOD dont les blocs finaux n'existent pas — la playlist ne publie que la
        // fenêtre encodée (les blocs completement écrits), EVENT + pas de ENDLIST.
        var text = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", "?api=k", 14_715_000L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var extinfCount = text.Split('\n').Count(line => line.StartsWith("#EXTINF", StringComparison.Ordinal));
        Assert.Equal(15, extinfCount); // 90 s encodées / 6 s
        Assert.Contains("#EXT-X-VERSION:4", text, StringComparison.Ordinal);
        Assert.Contains("#EXT-X-PLAYLIST-TYPE:EVENT", text, StringComparison.Ordinal);
        Assert.Contains("#EXT-X-TARGETDURATION:7", text, StringComparison.Ordinal);
        Assert.Contains("hwbyterange/hash.ts?api=k", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#EXT-X-ENDLIST", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void Fenetre_Croissante_AjouteDesEntrees()
    {
        // Les re-fetch (AVPlayer re-fetch le playlist EVENT pour lire la croissance).
        var t1 = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", null, 14_715_000L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);
        var t2 = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", null, 29_430_000L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var c1 = t1.Split('\n').Count(line => line.StartsWith("#EXTINF", StringComparison.Ordinal));
        var c2 = t2.Split('\n').Count(line => line.StartsWith("#EXTINF", StringComparison.Ordinal));
        Assert.Equal(15, c1);
        Assert.Equal(30, c2);
        // Les entrées précédentes restent stables (un append-only, jamais retirées).
        Assert.Contains(t1.Split('\n').First(line => line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal)), t2, StringComparison.Ordinal);
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
        var text = HwByteRangePlaylistBuilder.Build(MakeJob(true), "hash", null, TotalBytesForTicks(TestDurationTicks) / 2, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        Assert.Contains("#EXT-X-ENDLIST", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void Ancre_NonNulle_PremierBlocServable()
    {
        // G5-E v20 : après re-anchor ou démarrage à une position (startTimeTicks),
        // le 1er bloc publié doit être servable par la route (demandé ≥ ancre :
        // localOffset = demandé − ancre ≥ 0) et porter le temps global du contenu.
        var job = MakeJob();
        var anchorOffset = TotalBytesForTicks(400_000_000L); // 40 s au bitrate de test
        job.HwByteRangeAnchorOffset = anchorOffset;
        job.HwByteRangeAnchorTimeSec = 40.0;
        var text = HwByteRangePlaylistBuilder.Build(job, "hash", null, 10_000_000L, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var firstRange = text.Split('\n').First(line => line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal));
        Assert.EndsWith("@" + anchorOffset, firstRange, StringComparison.Ordinal);
        Assert.Contains("runtimeTicks=400000000&", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void Ancre_CouvertureContinue_Globale()
    {
        // Avec ancre, les offsets globaux publiés restent contigus : la fin d'un
        // bloc = le début du suivant (sinon trou de contenu au serving).
        var job = MakeJob();
        job.HwByteRangeAnchorOffset = TotalBytesForTicks(400_000_000L);
        job.HwByteRangeAnchorTimeSec = 40.0;
        var text = HwByteRangePlaylistBuilder.Build(job, "hash", null, TotalBytesForTicks(TestDurationTicks) / 4, TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var ranges = text.Split('\n').Where(line => line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal)).ToList();
        Assert.True(ranges.Count > 1);
        long lastEnd = job.HwByteRangeAnchorOffset;
        foreach (var range in ranges)
        {
            var spec = range["#EXT-X-BYTERANGE:".Length..];
            var at = spec.IndexOf('@', StringComparison.Ordinal);
            var len = long.Parse(spec[..at], System.Globalization.CultureInfo.InvariantCulture);
            var offset = long.Parse(spec[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(lastEnd, offset);
            lastEnd = offset + len;
        }
    }

    [Fact]
    public static void Ancre_DerniereFenetre_BlocServable()
    {
        // 🟠 revue : ancre dans les 6 dernières secondes → le floor produisait une
        // playlist VIDE (rejet AVFoundation). Le reliquat final doit être publié :
        // 1 bloc servable (demandé = ancre ⇒ localOffset = 0), temps global correct.
        var anchorTicks = TestDurationTicks - 30_000_000L; // 3 s avant la fin
        var job = MakeJob();
        job.HwByteRangeAnchorOffset = TotalBytesForTicks(anchorTicks);
        job.HwByteRangeAnchorTimeSec = (TestDurationTicks - 30_000_000L) / 10_000_000.0;
        var text = HwByteRangePlaylistBuilder.Build(job, "hash", null, TotalBytesForTicks(30_000_000L), TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var ranges = text.Split('\n').Where(line => line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal)).ToList();
        Assert.Single(ranges);
        Assert.StartsWith("#EXT-X-BYTERANGE:" + TotalBytesForTicks(30_000_000L) + "@" + job.HwByteRangeAnchorOffset, ranges[0], StringComparison.Ordinal);
        Assert.Contains("runtimeTicks=" + anchorTicks + "&", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void Ancre_AuDela_DuMedia_PlaylistSansBloc()
    {
        // Décision : ancre au-delà de la durée (demande aberrante, clampé ailleurs
        // par GetFastSeekCommandLineParameter) → aucun bloc possible, playlist vide.
        var job = MakeJob();
        job.HwByteRangeAnchorOffset = TotalBytesForTicks(TestDurationTicks) + 1000;
        job.HwByteRangeAnchorTimeSec = 12_100.0;
        var text = HwByteRangePlaylistBuilder.Build(job, "hash", null, TotalBytesForTicks(60_000_000L), TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        Assert.DoesNotContain("#EXTINF", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#EXT-X-BYTERANGE", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void Fenetre_MultipleExact_AucunBlocVide()
    {
        // Fenêtre exactement multiple de la taille de bloc : pas de reliquat, pas
        // d'entrée de longueur nulle (garde anti coïncidence clamp/offset).
        var text = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", null, TotalBytesForTicks(120_000_000L), TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var extinfCount = text.Split('\n').Count(line => line.StartsWith("#EXTINF", StringComparison.Ordinal));
        Assert.Equal(2, extinfCount);
        Assert.DoesNotContain("#EXT-X-BYTERANGE:0@", text, StringComparison.Ordinal);
    }

    [Fact]
    public static void Reliquat_NonTerminal_Refuse()
    {
        // Régime de production courant : fenêtre limitée par les octets, non
        // multiple de la taille de bloc, loin de la fin du média — le reliquat
        // n'est PAS publié (sa taille serait instable à chaque re-fetch, ce qui
        // romprait l'append-only) : 1 bloc plein, pas de bloc terminal.
        var text = HwByteRangePlaylistBuilder.Build(MakeJob(), "hash", null, TotalBytesForTicks(90_000_000L), TestDurationTicks, TestVideoBitrate, TestAudioBitrate, TestBlockSeconds);

        var ranges = text.Split('\n').Where(line => line.StartsWith("#EXT-X-BYTERANGE", StringComparison.Ordinal)).ToList();
        Assert.Single(ranges);
        Assert.Equal("#EXT-X-BYTERANGE:" + TotalBytesForTicks(60_000_000L) + "@0", ranges[0]);
        Assert.DoesNotContain("#EXT-X-BYTERANGE:0@", text, StringComparison.Ordinal);
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
