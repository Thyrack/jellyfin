using System;
using System.Linq;
using Jellyfin.Api.Helpers;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Api.Tests;

/// <summary>
/// Les tests G5-D (clients Apple) : AVFoundation refuse le TS progressif — la route
/// Apple est forcée vers le HLS byte-range (le TS continu de la playlist serveur).
/// </summary>
public static class ApplyHwMpegtsRouteAppleClientTests
{
    private static StreamInfo ProgressiveTsVideoStream() => new()
    {
        ItemId = Guid.NewGuid(),
        DeviceProfile = new DeviceProfile(),
        SubProtocol = MediaStreamProtocol.http,
        Container = "ts",
        PlayMethod = PlayMethod.Transcode
    };

    private static StreamInfo AppleHlsTsVideoStream() => new()
    {
        ItemId = Guid.NewGuid(),
        DeviceProfile = new DeviceProfile(),
        SubProtocol = MediaStreamProtocol.hls,
        Container = "ts",
        PlayMethod = PlayMethod.Transcode
    };

    [Fact]
    public static void VideoTranscode_Mediacodec_AppIos_ForceByteRangeHls()
    {
        // AVPlayer refuse le TS progressif ET le TS segmenté avec le muxer hls
        // (stalle avec l'encodeur HW, matrice G5-A ; les 2 jobs tués en ~4-15 s) :
        // l'iPhone reçoit la playlist HLS byte-range du serveur. La branch Apple
        // est AVANT la préservation G5-C4 (l'app Apple demande déjà hls+ts).
        var si = AppleHlsTsVideoStream();
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true, "Jellyfin iOS", null);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Equal("ts", si.Container);
    }

    [Fact]
    public static void VideoTranscode_Mediacodec_AppAppleTv_ForceByteRangeHls()
    {
        var si = AppleHlsTsVideoStream();
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true, "Jellyfin Apple TV", null);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Equal("ts", si.Container);
    }

    [Fact]
    public static void VideoTranscode_Mediacodec_AppAndroid_GardeProgressif()
    {
        // Le ExoPlayer Android consomme le TS progressif (validé 4,85×) : inchangé.
        var si = ProgressiveTsVideoStream();
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true, "Jellyfin for Android", null);

        Assert.Equal(MediaStreamProtocol.http, si.SubProtocol);
        Assert.Equal("ts", si.Container);
    }

    [Fact]
    public static void RemoveHevcVideoProfiles_H265Ortho_Retire()
    {
        // G5-D revue (N1) : l'orthographe h265 est vivante dans la codebase
        // (GetHwDecoderName, DynamicHlsHelper) — un profil h265 doit être filtré
        // comme hevc, sinon le remux hevc-ts réapparaît.
        var profile = new DeviceProfile
        {
            TranscodingProfiles =
            [
                new TranscodingProfile { Type = DlnaProfileType.Video, Protocol = MediaStreamProtocol.hls, Container = "ts", VideoCodec = "h265" }
            ]
        };
        MediaInfoHelper.RemoveHevcVideoProfiles(profile, null);

        Assert.Empty(profile.TranscodingProfiles!);
    }

    [Fact]
    public static void RemoveHevcVideoProfiles_CloneSansMutationDuCache()
    {
        // G5-D revue (N2) : le DeviceProfile peut être la référence partagée du
        // cache GetCapabilities — le filtrage doit agir sur un CLONE (l'original
        // conserve ses profils hevc pour les autres requêtes du device).
        var original = new DeviceProfile
        {
            TranscodingProfiles =
            [
                new TranscodingProfile { Type = DlnaProfileType.Video, Protocol = MediaStreamProtocol.hls, Container = "ts", VideoCodec = "h264", AudioCodec = "aac" },
                new TranscodingProfile { Type = DlnaProfileType.Video, Protocol = MediaStreamProtocol.hls, Container = "ts", VideoCodec = "hevc", AudioCodec = "ac3" }
            ]
        };

        var clone = CloneForFiltering(original);
        MediaInfoHelper.RemoveHevcVideoProfiles(clone, null);

        Assert.Single(clone.TranscodingProfiles!);
        // L'original n'est PAS muté : le cache conserve ses profils intacts.
        Assert.Equal(2, original.TranscodingProfiles!.Length);
    }

    /// <summary>
    /// Le clonage surface-copy de SetDeviceSpecificData (le même code que le helper
    /// inlined ; extrait ici pour couvrir l'invariant cache-non-muté)).
    /// </summary>
    private static DeviceProfile CloneForFiltering(DeviceProfile profile)
    {
        return new DeviceProfile
        {
            Name = profile.Name,
            Id = profile.Id,
            MaxStreamingBitrate = profile.MaxStreamingBitrate,
            MaxStaticBitrate = profile.MaxStaticBitrate,
            MusicStreamingTranscodingBitrate = profile.MusicStreamingTranscodingBitrate,
            TranscodingProfiles = profile.TranscodingProfiles
                .Select(p => new TranscodingProfile
                {
                    Type = p.Type,
                    Protocol = p.Protocol,
                    Container = p.Container,
                    VideoCodec = p.VideoCodec,
                    AudioCodec = p.AudioCodec
                })
                .ToArray()
        };
    }
}
