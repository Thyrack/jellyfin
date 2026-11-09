using Jellyfin.Api.Helpers;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Api.Tests;

/// <summary>
/// G5-D (Apple) : le HEVC est retiré des profils de remux/transcodage — AVFoundation
/// ne lit pas le HEVC en MPEG-TS (le HLS TS Apple = H.264 uniquement).
/// </summary>
public static class RemoveHevcVideoProfilesTests
{
    private static DeviceProfile AppleProfile() => new()
    {
        TranscodingProfiles =
        [
            new TranscodingProfile { Type = DlnaProfileType.Video, Protocol = MediaStreamProtocol.hls, Container = "ts", VideoCodec = "h264", AudioCodec = "aac" },
            new TranscodingProfile { Type = DlnaProfileType.Video, Protocol = MediaStreamProtocol.hls, Container = "ts", VideoCodec = "hevc", AudioCodec = "ac3" },
            new TranscodingProfile { Type = DlnaProfileType.Video, Protocol = MediaStreamProtocol.hls, Container = "mp4", VideoCodec = "hevc", AudioCodec = "ac3" }
        ]
    };

    [Fact]
    public static void ProfilHevcTs_Retire()
    {
        var profile = AppleProfile();
        MediaInfoHelper.RemoveHevcVideoProfiles(profile, null);

        // Heure : les 2 profils hevc (ts et mp4) sont retirés — le streaming HLS TS
        // H.264 et le fallback restent ; l'Apple ne reçoit plus de HEVC-TS.
        Assert.Single(profile.TranscodingProfiles!);
        Assert.Equal("h264", profile.TranscodingProfiles[0].VideoCodec);
    }

    [Fact]
    public static void ProfilSansHevc_Inchangené()
    {
        var profile = new DeviceProfile
        {
            TranscodingProfiles =
            [
                new TranscodingProfile { Type = DlnaProfileType.Video, Protocol = MediaStreamProtocol.hls, Container = "ts", VideoCodec = "h264" }
            ]
        };
        MediaInfoHelper.RemoveHevcVideoProfiles(profile, null);

        Assert.Single(profile.TranscodingProfiles!);
    }
}
