using System;
using Jellyfin.Api.Helpers;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Api.Tests;

/// <summary>
/// Tests de la détection TS-capable (G5-B2) : la détection doit classer les apps
/// TS-capables (profil ts/http sans hls fmp4) et exclure le web standard.
/// </summary>
public static class MediaInfoHelperHwMpegtsRouteTests
{
    private static StreamInfo HlsVideoStream() => new()
    {
        ItemId = Guid.NewGuid(),
        DeviceProfile = new DeviceProfile(),
        SubProtocol = MediaStreamProtocol.hls,
        Container = null,
        PlayMethod = PlayMethod.Transcode
    };

    private static TranscodingProfile Profil(DlnaProfileType type, MediaStreamProtocol proto, string container) => new()
    {
        Type = type,
        Protocol = proto,
        Container = container
    };

    [Fact]
    public static void VideoTranscode_Mediacodec_TsCapable_ForceProgressifTs()
    {
        var si = HlsVideoStream();
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true, "Jellyfin for Android", null);

        Assert.Equal(MediaStreamProtocol.http, si.SubProtocol);
        Assert.Equal("ts", si.Container);
    }

    [Fact]
    public static void VideoTranscode_Mediacodec_HlsTsProfil_PreserveHlsTs()
    {
        // G5-C4 : le client a un profil hls+ts (StreamBuilder l'a retenu) → route HLS
        // segments TS préservée ; l'ExoPlayer refuse le TS progressif (source error).
        var si = HlsVideoStream();
        si.Container = "ts";
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true, "Jellyfin for Android", null);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Equal("ts", si.Container);
    }

    [Fact]
    public static void VideoTranscode_Mediacodec_HlsFmp4Profil_TsCapable_BasculeProgressifTs()
    {
        // hls fmp4 (web-like ou fallback) + client ts-capable → pas de route TS HLS
        // demandée : le fallback TS progressif première route (G5-B2) s'applique.
        var si = HlsVideoStream();
        si.Container = "mp4";
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true, "Jellyfin for Android", null);

        Assert.Equal(MediaStreamProtocol.http, si.SubProtocol);
        Assert.Equal("ts", si.Container);
    }

    [Fact]
    public static void VideoTranscode_Mediacodec_TsIncapable_Inchangé()
    {
        var si = HlsVideoStream();
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, false, "Jellyfin for Android", null);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Null(si.Container);
    }

    [Fact]
    public static void VideoTranscode_HwTypeNonMediacodec_Inchangé()
    {
        var si = HlsVideoStream();
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.none, true, "Jellyfin for Android", null);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Null(si.Container);
    }

    [Fact]
    public static void LectureNonTranscode_Inchangé()
    {
        var si = HlsVideoStream();
        si.PlayMethod = PlayMethod.DirectPlay;
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.DirectPlay, HardwareAccelerationType.mediacodec, true, "Jellyfin for Android", null);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Null(si.Container);
    }

    [Fact]
    public static void AudioMediacodec_TsCapable_Inchangé()
    {
        var si = new StreamInfo
        {
            ItemId = Guid.NewGuid(),
            DeviceProfile = new DeviceProfile(),
            SubProtocol = MediaStreamProtocol.hls,
            Container = null,
            PlayMethod = PlayMethod.Transcode
        };
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Audio, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true, "Jellyfin for Android", null);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Null(si.Container);
    }

    [Fact]
    public static void Detection_ProfilTsSansFmp4_EstTsCapable()
    {
        var profiles = new[] { Profil(DlnaProfileType.Video, MediaStreamProtocol.http, "ts") };
        Assert.True(MediaInfoHelper.IsTsCapableClient(profiles, "Jellyfin for Android"));
    }

    [Fact]
    public static void Detection_AppNatif_TsPlusFmp4_EstTsCapable()
    {
        // une app native (ExoPlayer) avec fallback fmp4 reste TS-capable
        var profiles = new[]
        {
            Profil(DlnaProfileType.Video, MediaStreamProtocol.http, "ts"),
            Profil(DlnaProfileType.Video, MediaStreamProtocol.hls, "mp4")
        };
        Assert.True(MediaInfoHelper.IsTsCapableClient(profiles, "Jellyfin for Android"));
    }

    [Fact]
    public static void Detection_WebExcluParNomClient()
    {
        // le web standard : profil ts/http de fallback + profil fmp4/hls → x264-NEON fluidité
        var profiles = new[]
        {
            Profil(DlnaProfileType.Video, MediaStreamProtocol.http, "ts"),
            Profil(DlnaProfileType.Video, MediaStreamProtocol.hls, "mp4")
        };
        Assert.False(MediaInfoHelper.IsTsCapableClient(profiles, "Jellyfin Web")); // exclu par le nom, pas par le profil
    }

    [Fact]
    public static void Detection_ProfilVide_NonTsCapable()
    {
        Assert.False(MediaInfoHelper.IsTsCapableClient(Array.Empty<TranscodingProfile>(), "Jellyfin for Android"));
        Assert.False(MediaInfoHelper.IsTsCapableClient(null, "Jellyfin for Android"));
        // le web est exclu par le nom, même avec profil ts :
        Assert.False(MediaInfoHelper.IsTsCapableClient(
            new[] { Profil(DlnaProfileType.Video, MediaStreamProtocol.http, "ts") }, "Jellyfin Web"));
    }

    [Fact]
    public static void Detection_AppNatif_ContainerTs_EstTsCapable()
    {
        var profiles = new[] { Profil(DlnaProfileType.Video, MediaStreamProtocol.http, "ts,mpegts") };
        Assert.True(MediaInfoHelper.IsTsCapableClient(profiles, "Jellyfin Android TV"));
    }
}
