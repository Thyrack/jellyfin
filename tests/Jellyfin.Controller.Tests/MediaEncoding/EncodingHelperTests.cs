using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Moq;
using Xunit;

using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Jellyfin.Controller.Tests.MediaEncoding;

public class EncodingHelperTests
{
    [Fact]
    public void GetMapArgs_NoSubtitle_ExcludesAllSubs()
    {
        var state = BuildState(subtitle: null, deliveryMethod: null);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map -0:s", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-map 1:", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_InternalSrt_MapsFromPrimaryInput()
    {
        var sub = new MediaStream { Index = 2, Type = MediaStreamType.Subtitle, Codec = "srt" };
        var state = BuildState(sub, SubtitleDeliveryMethod.Embed);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 0:2", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-map 1:", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_InternalSubAtHigherIndex_MapsCorrectIndex()
    {
        var sub0 = new MediaStream { Index = 2, Type = MediaStreamType.Subtitle, Codec = "srt" };
        var sub1 = new MediaStream { Index = 3, Type = MediaStreamType.Subtitle, Codec = "ass" };
        var state = BuildState(sub1, SubtitleDeliveryMethod.Embed, additionalStreams: [sub0, sub1]);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 0:3", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_ExternalSrt_MapsFirstStreamFromInput1()
    {
        var sub = new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = "srt",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.en.srt"
        };
        var state = BuildState(sub, SubtitleDeliveryMethod.Embed);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 1:0", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_SecondExternalSrt_StillMaps1Colon0()
    {
        // Two separate .srt files — selecting the second one still maps 1:0
        // because Jellyfin feeds only the selected file as ffmpeg input 1.
        var ext1 = new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = "srt",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.en.srt"
        };
        var ext2 = new MediaStream
        {
            Index = 3,
            Type = MediaStreamType.Subtitle,
            Codec = "srt",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.fr.srt"
        };
        var state = BuildState(ext2, SubtitleDeliveryMethod.Embed, additionalStreams: [ext1, ext2]);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 1:0", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_MksFirstTrack_MapsInFileIndex0()
    {
        var mks0 = new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = "subrip",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var mks1 = new MediaStream
        {
            Index = 3,
            Type = MediaStreamType.Subtitle,
            Codec = "ass",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var state = BuildState(mks0, SubtitleDeliveryMethod.Embed, additionalStreams: [mks0, mks1]);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 1:0", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_MksSecondTrack_MapsInFileIndex1()
    {
        var mks0 = new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = "subrip",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var mks1 = new MediaStream
        {
            Index = 3,
            Type = MediaStreamType.Subtitle,
            Codec = "ass",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var mks2 = new MediaStream
        {
            Index = 4,
            Type = MediaStreamType.Subtitle,
            Codec = "subrip",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var state = BuildState(mks1, SubtitleDeliveryMethod.Embed, additionalStreams: [mks0, mks1, mks2]);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 1:1", args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SubtitleDeliveryMethod.Embed, true, "movie.idx")]
    [InlineData(SubtitleDeliveryMethod.Encode, true, "movie.idx")]
    [InlineData(SubtitleDeliveryMethod.Embed, false, "movie.sub")]
    [InlineData(SubtitleDeliveryMethod.Encode, false, "movie.sub")]
    public void GetInputArgument_VobSub_UsesCorrectPath(
        SubtitleDeliveryMethod deliveryMethod,
        bool createIdxFile,
        string expectedFilename)
    {
        var tempDir = Directory.CreateTempSubdirectory("jellyfin-test-");
        try
        {
            var subFile = Path.Combine(tempDir.FullName, "movie.sub");
            File.WriteAllText(subFile, "dummy");

            if (createIdxFile)
            {
                File.WriteAllText(Path.Combine(tempDir.FullName, "movie.idx"), "dummy");
            }

            var sub = new MediaStream
            {
                Index = 2,
                Type = MediaStreamType.Subtitle,
                Codec = "dvdsub",
                IsExternal = true,
                SupportsExternalStream = true,
                Path = subFile
            };
            var state = BuildState(sub, deliveryMethod);
            var inputArgs = CreateHelper().GetInputArgument(state, new EncodingOptions(), null);

            Assert.Contains(expectedFilename, inputArgs, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Theory]
    [InlineData("aac", 44100, 44100)] // non-opus: requested rate must be preserved (issue #17026)
    [InlineData("aac", 48000, 48000)]
    [InlineData("mp3", 22050, 22050)]
    [InlineData("flac", 96000, 96000)]
    [InlineData("opus", 44100, 48000)] // opus: must snap to a libopus-supported rate
    [InlineData("opus", 22050, 24000)]
    [InlineData("opus", 8000, 8000)]
    public void GetProgressiveAudioFullCommandLine_SampleRate_OnlyClampedForOpus(
        string audioCodec,
        int requestedSampleRate,
        int expectedSampleRate)
    {
        var state = BuildAudioState(audioCodec, requestedSampleRate);
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out");

        Assert.Contains("-ar " + expectedSampleRate, args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wav")]
    [InlineData("flac")]
    [InlineData("mp3")]
    public void GetProgressiveAudioFullCommandLine_PcmInRealContainer_KeepsContainerMuxer(string outputContainer)
    {
        // A pcm_* encoder must not drag the raw muxer into a container that writes its own header,
        // or the client gets headerless PCM behind the container's content type.
        var state = BuildAudioState("pcm_s16le", 48000, outputContainer);
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out");

        Assert.DoesNotContain("-f s16le", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProgressiveAudioFullCommandLine_PcmInPcmContainer_ForcesRawMuxer()
    {
        // The raw-PCM route added in #10321 for I2S/MCU clients must keep working.
        var state = BuildAudioState("pcm_s16le", 48000, "pcm");
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out");

        Assert.Contains("-f s16le", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProgressiveAudioFullCommandLine_PcmWithoutBitrate_EmitsNoEmptySampleRate()
    {
        // AudioBitRate is optional; it used to be emitted as `-ar <null>`, producing a bare `-ar`
        // that made ffmpeg abort with "Expected number for ar" and the request fail with HTTP 500.
        var state = BuildAudioState("pcm_s16le", 48000, "wav");
        state.BaseRequest.AudioBitRate = null;
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out");

        Assert.DoesNotContain("-ar -", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-ar  ", args, StringComparison.Ordinal);
        Assert.Contains("-ar 48000", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProgressiveAudioFullCommandLine_StereoDownmix_AppliesDownMixAlgorithm()
    {
        // Issue #18194: -ac 2 alone drops the LFE channel, the configured downmix filter must be applied.
        var state = BuildAudioState("aac", 48000);
        state.AudioStream.Channels = 6;
        state.AudioStream.ChannelLayout = "5.1";
        state.OutputAudioChannels = 2;
        var options = new EncodingOptions { DownMixStereoAlgorithm = DownMixStereoAlgorithms.Dave750, DownMixAudioBoost = 1 };
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, options, "/tmp/out");

        Assert.Contains(
            "-af \"" + DownMixAlgorithmsHelper.AlgorithmFilterStrings[(DownMixStereoAlgorithms.Dave750, "5.1")] + "\"",
            args,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GetProgressiveAudioFullCommandLine_NoDownmix_EmitsNoAudioFilter()
    {
        var state = BuildAudioState("aac", 48000);
        state.AudioStream.Channels = 2;
        state.OutputAudioChannels = 2;
        var options = new EncodingOptions { DownMixStereoAlgorithm = DownMixStereoAlgorithms.Dave750 };
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, options, "/tmp/out");

        Assert.DoesNotContain("-af", args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void GetProgressiveVideoAudioArguments_NonStereoOutput_KeepsChannelCount(int outputChannels)
    {
        // The downmix filter only applies to stereo output, so -ac must not be dropped otherwise.
        var state = BuildAudioState("aac", 48000);
        state.AudioStream.Channels = 6;
        state.AudioStream.ChannelLayout = "5.1";
        state.OutputAudioChannels = outputChannels;
        var options = new EncodingOptions { DownMixStereoAlgorithm = DownMixStereoAlgorithms.Dave750 };
        var args = CreateHelper().GetProgressiveVideoAudioArguments(state, options);

        Assert.Contains("-ac " + outputChannels, args, StringComparison.Ordinal);
        Assert.DoesNotContain("pan=", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProgressiveVideoAudioArguments_StereoDownmix_UsesFilterInsteadOfChannelCount()
    {
        var state = BuildAudioState("aac", 48000);
        state.AudioStream.Channels = 6;
        state.AudioStream.ChannelLayout = "5.1";
        state.OutputAudioChannels = 2;
        var options = new EncodingOptions { DownMixStereoAlgorithm = DownMixStereoAlgorithms.Dave750 };
        var args = CreateHelper().GetProgressiveVideoAudioArguments(state, options);

        Assert.DoesNotContain("-ac ", args, StringComparison.Ordinal);
        Assert.Contains("pan=stereo", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetVideoEncoder_Mediacodec_UsesHwEncoderWhenAvailable()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc");
        var args = CreateHelper(encoder.Object).GetVideoEncoder(state, MediacodecOptions());

        Assert.Equal("h264_mediacodec", args);
    }

    [Fact]
    public void GetVideoEncoder_Mediacodec_EncoderAbsent_FallbackX264()
    {
        // Mock SANS SupportsEncoder → SupportsEncoder=false → fallback même avec HW active
        var state = BuildVideoState("hevc");
        var args = CreateHelper().GetVideoEncoder(state, MediacodecOptions());

        Assert.Equal("libx264", args);
    }

    [Fact]
    public void GetVideoEncoder_Mediacodec_EncoderAbsent_FallbackX265()
    {
        var state = BuildVideoState("hevc");
        state.OutputVideoCodec = "hevc";
        var args = CreateHelper().GetVideoEncoder(state, MediacodecOptions());

        Assert.Equal("libx265", args);
    }

    [Fact]
    public void GetVideoEncoder_Mediacodec_EncodageHwDesactive_Fallback()
    {
        // Encodeur mocké DISPONIBLE mais EnableHardwareEncoding=false → x264 (groupe d'option)
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc");
        var options = MediacodecOptions();
        options.EnableHardwareEncoding = false;
        var args = CreateHelper(encoder.Object).GetVideoEncoder(state, options);

        Assert.Equal("libx264", args);
    }

    [Fact]
    public void GetInputVideoHwaccelArgs_MediacodecHevc_UsesValidatedG2Route()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsHwaccel("mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsDecoder("hevc_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc");
        state.VideoStream.PixelFormat = "yuv420p";
        // Route testée avec l'option explicite : le défaut G4 v1.1 la laisse INACTIVE
        // (décodage HW mediacodec + filtres SW Jellyfin ne sortent pas de segments sans hwdownload).
        var options = new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.mediacodec, HardwareDecodingCodecs = ["h264", "vc1", "hevc"] };
        var args = CreateHelper(encoder.Object).GetInputVideoHwaccelArgs(state, options);

        Assert.Contains("-hwaccel mediacodec", args, StringComparison.Ordinal);
        Assert.Contains("-c:v hevc_mediacodec", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInputVideoHwaccelArgs_MediacodecHevc10Bit_KeepsRouteWhenOptionEnabled()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsHwaccel("mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsDecoder("hevc_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc", bitDepth: 10);
        state.VideoStream.PixelFormat = "yuv420p10le";
        var options = new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.mediacodec, HardwareDecodingCodecs = ["h264", "vc1", "hevc"] };
        var args = CreateHelper(encoder.Object).GetInputVideoHwaccelArgs(state, options);

        Assert.Contains("-c:v hevc_mediacodec", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInputVideoHwaccelArgs_MediacodecH264_KeepsCpuDecode()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsHwaccel("mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsDecoder("h264_mediacodec")).Returns(true);
        var state = BuildVideoState("h264");
        var args = CreateHelper(encoder.Object).GetInputVideoHwaccelArgs(state, new EncodingOptions());

        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void GetInputVideoHwaccelArgs_MediacodecHevc_DefautV111_Vide()
    {
        // DEFAULT build Shield v1.1 : HardwareDecodingCodecs SANS hevc → pas de décodage HW HEVC
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsHwaccel("mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsDecoder("hevc_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc");
        state.VideoStream.PixelFormat = "yuv420p";
        var args = CreateHelper(encoder.Object).GetInputVideoHwaccelArgs(state, new EncodingOptions());

        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void GetInputVideoHwaccelArgs_MediacodecHevcChromaIncorrect_RouteRefusee()
    {
        // 4:4:4 / 12-bit hors capacités décodeur MediaCodec Shield (Main/Main10)
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsHwaccel("mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsDecoder("hevc_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc", bitDepth: 12);
        state.VideoStream.PixelFormat = "yuv444p10le";
        var args = CreateHelper(encoder.Object).GetInputVideoHwaccelArgs(state, MediacodecOptions());

        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void GetInputVideoHwaccelArgs_MediacodecHevc10bit_Deactive_EnArgsVides()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsHwaccel("mediacodec")).Returns(true);
        encoder.Setup(m => m.SupportsDecoder("hevc_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc", bitDepth: 10);
        state.VideoStream.PixelFormat = "yuv420p10le";
        var options = MediacodecOptions();
        options.EnableDecodingColorDepth10Hevc = false;
        var args = CreateHelper(encoder.Object).GetInputVideoHwaccelArgs(state, options);

        Assert.Equal(string.Empty, args);
    }

    private static EncodingJobInfo BuildAudioState(string audioCodec, int requestedSampleRate, string? outputContainer = null)
    {
        var audio = new MediaStream { Index = 0, Type = MediaStreamType.Audio, Codec = "flac", SampleRate = 96000 };

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            OutputContainer = outputContainer,
            MediaSource = new MediaSourceInfo
            {
                Container = "flac",
                MediaStreams = new List<MediaStream> { audio },
                Path = "/media/track.flac",
                Protocol = MediaProtocol.File,
            },
            AudioStream = audio,
            OutputAudioCodec = audioCodec,
            BaseRequest = new VideoRequestDto
            {
                AudioCodec = audioCodec,
                AudioSampleRate = requestedSampleRate,
            },
            IsVideoRequest = false,
            IsInputVideo = false,
        };
    }

    private static EncodingJobInfo BuildState(
        MediaStream? subtitle,
        SubtitleDeliveryMethod? deliveryMethod,
        MediaStream[]? additionalStreams = null)
    {
        var video = new MediaStream { Index = 0, Type = MediaStreamType.Video, Codec = "h264" };
        var audio = new MediaStream { Index = 1, Type = MediaStreamType.Audio, Codec = "aac" };
        var streams = new List<MediaStream> { video, audio };

        if (additionalStreams is not null)
        {
            streams.AddRange(additionalStreams);
        }
        else if (subtitle is not null)
        {
            streams.Add(subtitle);
        }

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            MediaSource = new MediaSourceInfo
            {
                Container = "mkv",
                MediaStreams = streams,
            },
            VideoStream = video,
            AudioStream = audio,
            SubtitleStream = subtitle,
            SubtitleDeliveryMethod = deliveryMethod ?? SubtitleDeliveryMethod.Drop,
            BaseRequest = new VideoRequestDto(),
            IsVideoRequest = true,
            IsInputVideo = true,
        };
    }

    private static EncodingHelper CreateHelper()
    {
        return CreateHelper(new Mock<IMediaEncoder>().Object);
    }

    private static EncodingHelper CreateHelper(IMediaEncoder mediaEncoder)
    {
        var appPaths = Mock.Of<IApplicationPaths>();
        var subtitleEncoder = new Mock<ISubtitleEncoder>();
        var config = new Mock<IConfiguration>();
        var configurationManager = new Mock<IConfigurationManager>();
        var pathManager = new Mock<IPathManager>();

        return new EncodingHelper(
            appPaths,
            mediaEncoder,
            subtitleEncoder.Object,
            config.Object,
            configurationManager.Object,
            pathManager.Object);
    }

    private static EncodingOptions MediacodecOptions() => new()
    {
        HardwareAccelerationType = HardwareAccelerationType.mediacodec,
    };

    [Fact]
    public void GetVideoEncoder_Mediacodec_JobHls_FallbackLibx264()
    {
        // le web (hls fmp4) ne reçoit jamais l'encodeur HW : le mux fmp4 stalle
        // l'encodeur mediacodec (matrice RUNFF, frame=0) → fallback SW x264-NEON
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc", null, TranscodingJobType.Hls);
        var args = CreateHelper(encoder.Object).GetVideoEncoder(state, MediacodecOptions());

        Assert.Equal("libx264", args);
    }

    [Fact]
    public void GetVideoEncoder_Mediacodec_JobProgressif_UsesHwEncoder()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc", null, TranscodingJobType.Progressive);
        var args = CreateHelper(encoder.Object).GetVideoEncoder(state, MediacodecOptions());

        Assert.Equal("h264_mediacodec", args);
    }

    [Fact]
    public void GetVideoEncoder_Mediacodec_JobProgressif_HwAbsent_FallbackLibx264()
    {
        var state = BuildVideoState("hevc", null, TranscodingJobType.Progressive);
        var args = CreateHelper().GetVideoEncoder(state, MediacodecOptions());

        Assert.Equal("libx264", args);
    }

    [Fact]
    public void GetVideoEncoder_None_JobProgressif_FallbackLibx264()
    {
        // le défaut HW=none : le route progressive reste SW
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(m => m.SupportsEncoder("h264_mediacodec")).Returns(true);
        var state = BuildVideoState("hevc", null, TranscodingJobType.Progressive);
        var options = new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.none };
        var args = CreateHelper(encoder.Object).GetVideoEncoder(state, options);

        Assert.Equal("libx264", args);
    }

    private static EncodingJobInfo BuildVideoState(string videoCodec, int? bitDepth = null, TranscodingJobType jobType = TranscodingJobType.Progressive)
    {
        var video = new MediaStream { Index = 0, Type = MediaStreamType.Video, Codec = videoCodec, Width = 1920, Height = 1080 };
        if (bitDepth is not null)
        {
            video.BitDepth = bitDepth;
        }

        var audio = new MediaStream { Index = 1, Type = MediaStreamType.Audio, Codec = "ac3", Channels = 6 };

        return new EncodingJobInfo(jobType)
        {
            MediaSource = new MediaSourceInfo
            {
                Container = "mkv",
                MediaStreams = [video, audio],
                Path = "/media/video.mkv",
                Protocol = MediaProtocol.File,
            },
            VideoStream = video,
            AudioStream = audio,
            OutputVideoCodec = "h264",
            OutputAudioCodec = "aac",
            BaseRequest = new VideoRequestDto(),
            IsVideoRequest = true,
            IsInputVideo = true,
        };
    }
}
