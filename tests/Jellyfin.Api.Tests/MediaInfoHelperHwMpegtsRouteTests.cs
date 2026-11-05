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
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true);

        Assert.Equal(MediaStreamProtocol.http, si.SubProtocol);
        Assert.Equal("ts", si.Container);
    }

    [Fact]
    public static void VideoTranscode_Mediacodec_TsIncapable_Inchangé()
    {
        var si = HlsVideoStream();
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, false);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Null(si.Container);
    }

    [Fact]
    public static void VideoTranscode_HwTypeNonMediacodec_Inchangé()
    {
        var si = HlsVideoStream();
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.Transcode, HardwareAccelerationType.none, true);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Null(si.Container);
    }

    [Fact]
    public static void LectureNonTranscode_Inchangé()
    {
        var si = HlsVideoStream();
        si.PlayMethod = PlayMethod.DirectPlay;
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Video, PlayMethod.DirectPlay, HardwareAccelerationType.mediacodec, true);

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
        MediaInfoHelper.ApplyHwMpegtsRoute(si, DlnaProfileType.Audio, PlayMethod.Transcode, HardwareAccelerationType.mediacodec, true);

        Assert.Equal(MediaStreamProtocol.hls, si.SubProtocol);
        Assert.Null(si.Container);
    }

    [Fact]
    public static void Detection_ProfilTsSansFmp4_EstTsCapable()
    {
        var profiles = new[] { Profil(DlnaProfileType.Video, MediaStreamProtocol.http, "ts") };
        Assert.True(MediaInfoHelper.IsTsCapableClient(profiles));
    }

    [Fact]
    public static void Detection_ProfilTsPlusFmp4_NonTsCapable()
    {
        // le web standard : profil ts/http de fallback + profil fmp4/hls → x264-NEON fluidité
        var profiles = new[]
        {
            Profil(DlnaProfileType.Video, MediaStreamProtocol.http, "ts"),
            Profil(DlnaProfileType.Video, MediaStreamProtocol.hls, "mp4")
        };
        Assert.False(MediaInfoHelper.IsTsCapableClient(profiles));
    }

    [Fact]
    public static void Detection_ProfilVide_NonTsCapable()
    {
        Assert.False(MediaInfoHelper.IsTsCapableClient(Array.Empty<TranscodingProfile>()));
        Assert.False(MediaInfoHelper.IsTsCapableClient(null));
    }

    [Fact]
    public static void Detection_ContainerTsMpegts_EstTsCapable()
    {
        var profiles = new[] { Profil(DlnaProfileType.Video, MediaStreamProtocol.http, "ts,mpegts") };
        Assert.True(MediaInfoHelper.IsTsCapableClient(profiles));
    }
}
