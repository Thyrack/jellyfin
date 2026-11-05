using System;
using System.Reflection;
using MediaBrowser.MediaEncoding.Encoder;
using Xunit;

namespace Jellyfin.MediaEncoding.Tests.Encoder;

/// <summary>
/// Verrouille la whitelist d'encoders/décodeurs introduite pour le build Shield (G4) :
/// sans l'entrée h264_mediacodec, <c>SupportsEncoder</c> rendant false, le serveur
/// recourait silencieusement à libx264 malgré l'option HW active (constat run réel).
/// </summary>
public class EncoderValidatorWhitelistTests
{
    private static string[] GetWhitelist(string fieldName)
    {
        var champ = typeof(EncoderValidator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        var listen = Assert.IsType<string[]>(champ?.GetValue(null));
        return listen;
    }

    [Fact]
    public void WhitelistEncoders_ContientH264Mediacodec()
    {
        Assert.Contains("h264_mediacodec", GetWhitelist("_requiredEncoders"));
    }

    [Fact]
    public void WhitelistEncoders_ExclutHevcMediacodec_JamaisValideOnDevice()
    {
        Assert.DoesNotContain("hevc_mediacodec", GetWhitelist("_requiredEncoders"));
    }

    [Fact]
    public void WhitelistDecoders_ContientHevcMediacodec()
    {
        Assert.Contains("hevc_mediacodec", GetWhitelist("_requiredDecoders"));
    }
}
