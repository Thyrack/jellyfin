using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Helpers;

/// <summary>
/// Media info helper.
/// </summary>
public class MediaInfoHelper
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ILogger<MediaInfoHelper> _logger;
    private readonly INetworkManager _networkManager;
    private readonly IDeviceManager _deviceManager;
    private readonly IServerApplicationHost _appHost;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaInfoHelper"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{MediaInfoHelper}"/> interface.</param>
    /// <param name="networkManager">Instance of the <see cref="INetworkManager"/> interface.</param>
    /// <param name="deviceManager">Instance of the <see cref="IDeviceManager"/> interface.</param>
    /// <param name="appHost">Instance of the <see cref="IServerApplicationHost"/> interface.</param>
    public MediaInfoHelper(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager serverConfigurationManager,
        ILogger<MediaInfoHelper> logger,
        INetworkManager networkManager,
        IDeviceManager deviceManager,
        IServerApplicationHost appHost)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _mediaEncoder = mediaEncoder;
        _serverConfigurationManager = serverConfigurationManager;
        _logger = logger;
        _networkManager = networkManager;
        _deviceManager = deviceManager;
        _appHost = appHost;
    }

    /// <summary>
    /// Get playback info.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="user">The user.</param>
    /// <param name="request">The current <see cref="HttpRequest"/>.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="liveStreamId">Live stream id.</param>
    /// <returns>A <see cref="Task"/> containing the <see cref="PlaybackInfoResponse"/>.</returns>
    public async Task<PlaybackInfoResponse> GetPlaybackInfo(
        BaseItem item,
        User? user,
        HttpRequest request,
        string? mediaSourceId = null,
        string? liveStreamId = null)
    {
        var result = new PlaybackInfoResponse();

        var mediaSources = await ResolvePlaybackMediaSources(item, user, mediaSourceId, liveStreamId).ConfigureAwait(false);

        if (mediaSources.Length == 0)
        {
            result.MediaSources = Array.Empty<MediaSourceInfo>();

            result.ErrorCode ??= PlaybackErrorCode.NoCompatibleStream;
        }
        else
        {
            // Since we're going to be setting properties on MediaSourceInfos that come out of _mediaSourceManager, we should clone it
            // Should we move this directly into MediaSourceManager?
            var mediaSourcesClone = JsonSerializer.Deserialize<MediaSourceInfo[]>(JsonSerializer.SerializeToUtf8Bytes(mediaSources));
            if (mediaSourcesClone is not null)
            {
                // Carry over the default audio index source.
                // This field is not intended to be exposed to API clients, but it is used internally by the server
                for (int i = 0; i < mediaSourcesClone.Length && i < mediaSources.Length; i++)
                {
                    mediaSourcesClone[i].DefaultAudioIndexSource = mediaSources[i].DefaultAudioIndexSource;
                }

                foreach (var mediaSource in mediaSourcesClone)
                {
                    RewritePublishedLiveStreamPath(mediaSource, request);
                }

                result.MediaSources = mediaSourcesClone;
            }

            result.PlaySessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        return result;
    }

    private async Task<MediaSourceInfo[]> ResolvePlaybackMediaSources(BaseItem item, User? user, string? mediaSourceId, string? liveStreamId)
    {
        if (!string.IsNullOrWhiteSpace(liveStreamId))
        {
            var mediaSource = await _mediaSourceManager.GetLiveStream(liveStreamId, CancellationToken.None).ConfigureAwait(false);

            return new[] { mediaSource };
        }

        // TODO (moved from MediaBrowser.Api) handle supportedLiveMediaTypes?
        var mediaSourcesList = await _mediaSourceManager.GetPlaybackMediaSources(item, user, true, true, CancellationToken.None).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(mediaSourceId))
        {
            return mediaSourcesList.ToArray();
        }

        return mediaSourcesList
            .Where(i => string.Equals(i.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// SetDeviceSpecificData.
    /// </summary>
    /// <param name="item">Item to set data for.</param>
    /// <param name="mediaSource">Media source info.</param>
    /// <param name="profile">Device profile.</param>
    /// <param name="claimsPrincipal">Current claims principal.</param>
    /// <param name="maxBitrate">Max bitrate.</param>
    /// <param name="startTimeTicks">Start time ticks.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="audioStreamIndex">Audio stream index.</param>
    /// <param name="subtitleStreamIndex">Subtitle stream index.</param>
    /// <param name="maxAudioChannels">Max audio channels.</param>
    /// <param name="playSessionId">Play session id.</param>
    /// <param name="userId">User id.</param>
    /// <param name="enableDirectPlay">Enable direct play.</param>
    /// <param name="enableDirectStream">Enable direct stream.</param>
    /// <param name="enableTranscoding">Enable transcoding.</param>
    /// <param name="allowVideoStreamCopy">Allow video stream copy.</param>
    /// <param name="allowAudioStreamCopy">Allow audio stream copy.</param>
    /// <param name="alwaysBurnInSubtitleWhenTranscoding">Always burn-in subtitle when transcoding.</param>
    /// <param name="ipAddress">Requesting IP address.</param>
    public void SetDeviceSpecificData(
        BaseItem item,
        MediaSourceInfo mediaSource,
        DeviceProfile? profile,
        ClaimsPrincipal claimsPrincipal,
        int? maxBitrate,
        long startTimeTicks,
        string mediaSourceId,
        int? audioStreamIndex,
        int? subtitleStreamIndex,
        int? maxAudioChannels,
        string playSessionId,
        Guid userId,
        bool enableDirectPlay,
        bool enableDirectStream,
        bool enableTranscoding,
        bool allowVideoStreamCopy,
        bool allowAudioStreamCopy,
        bool alwaysBurnInSubtitleWhenTranscoding,
        IPAddress ipAddress)
    {
        var streamBuilder = new StreamBuilder(_mediaEncoder, _logger);
        var clientName = claimsPrincipal.GetClient();
        // G5-D (Apple/le filtrage hevc) : le DeviceProfile peut venir du cache
        // GetCapabilities (la référence partagée). E1 revue s+1 : le clone est
        // CONDITIONNÉ aux clients Apple (les non-Apple gardent l'original intact
        // : muter DirectPlayProfiles/CodecProfiles/ContainerProfiles/
        // SubtitleProfiles de l'original serait une régression direct-play) et
        // le clone est FIDÈLE — TranscodingProfiles filtrés en profondeur +
        // CodecProfiles réassignés (G5-F, source HDR) : mutations toujours par
        // réassignation sur le clone, l'original du cache n'est jamais touché.
        var profileNonNull = profile is null || IsAppleClient(clientName) is false
            ? profile ?? new DeviceProfile()
            : CloneProfileForHevcFiltering(profile);

        var options = new MediaOptions
        {
            MediaSources = new[] { mediaSource },
            Context = EncodingContext.Streaming,
            DeviceId = claimsPrincipal.GetDeviceId(),
            ItemId = item.Id,
            Profile = profileNonNull,
            MaxAudioChannels = maxAudioChannels,
            AllowAudioStreamCopy = allowAudioStreamCopy,
            AllowVideoStreamCopy = allowVideoStreamCopy,
            AlwaysBurnInSubtitleWhenTranscoding = alwaysBurnInSubtitleWhenTranscoding,
        };

        if (string.Equals(mediaSourceId, mediaSource.Id, StringComparison.OrdinalIgnoreCase))
        {
            options.MediaSourceId = mediaSourceId;
            options.AudioStreamIndex = audioStreamIndex;
            options.SubtitleStreamIndex = subtitleStreamIndex;
        }

        var user = _userManager.GetUserById(userId) ?? throw new ResourceNotFoundException();

        if (!enableDirectPlay)
        {
            mediaSource.SupportsDirectPlay = false;
        }

        if (!enableDirectStream || !allowVideoStreamCopy)
        {
            mediaSource.SupportsDirectStream = false;
        }

        if (!enableTranscoding)
        {
            mediaSource.SupportsTranscoding = false;
        }

        if (item is Audio)
        {
            _logger.LogInformation(
                "User policy for {0}. EnableAudioPlaybackTranscoding: {1}",
                user.Username,
                user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding));
        }
        else
        {
            _logger.LogInformation(
                "User policy for {0}. EnablePlaybackRemuxing: {1} EnableVideoPlaybackTranscoding: {2} EnableAudioPlaybackTranscoding: {3}",
                user.Username,
                user.HasPermission(PermissionKind.EnablePlaybackRemuxing),
                user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding),
                user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding));
        }

        options.MaxBitrate = GetMaxBitrate(maxBitrate, user, ipAddress);

        if (!options.ForceDirectStream)
        {
            // direct-stream http streaming is currently broken
            options.EnableDirectStream = false;
        }

        // Beginning of Playback Determination

        // G5-D (Apple) : AVFoundation ne lit jamais le HEVC en MPEG-TS (le HLS TS
        // Apple est H.264 uniquement, le HEVC n'existe qu'en fMP4 côté Apple) — le
        // remux hevc-ts produit des segments que le player parse puis échoue
        // (rame/abandon, constat Top Gun + Le.Village). Retirer le HEVC des profils
        // de remux/transcodage des clients Apple pour que le StreamBuilder choisisse
        // le transcodage H.264 (la route byte-range HW mediacodec en aval).
        // G5-F : la décision est verrouillée en une variable pour les DEUX usages
        // (strip/keep du profil et forçage de route) : une divergence forcerait le
        // TS sur un streamInfo fMP4 HEVC (régression HEVC-TS silencieuse).
        var sourceIsHdr = IsAppleClient(clientName) && !IsSourceSdrVideo(mediaSource);
        if (IsAppleClient(clientName))
        {
            // Le strip hevc ne vaut que pour la route TS H264 (le HEVC-TS est
            // illisible par AVFoundation) — i.e. les sources SDR. Pour une source
            // HDR (PQ/HLG/DOVI), le H264 PQ produit par le passthrough HW est rejeté
            // par AVPlayer : le HLS Apple n'admet l'HDR qu'en HEVC (le profil du
            // client exige d'ailleurs SDR pour h264) — constat 17/11 : fetch annulé
            // 0,13 s après le range (OperationCanceledException côté serving, le
            // player rend son verdict au parsing du header). Pour ces sources le
            // profil hevc-fmp4 du client est CONSERVÉ et ses CodecProfiles hevc
            // (condition hvc1|dvh1, jamais vraie depuis un MKV sans CodecTag) sont
            // retirés : le StreamBuilder choisit alors le remux HEVC→fMP4 (copie,
            // charge ~nulle, 4K HDR natif) sur la route HLS standard — validé
            // on-device : `-codec:v:0 copy -tag:v:0 hvc1 -f hls -hls_segment_type
            // fmp4 -hls_playlist_type vod` (segments hvc1 3840x1608 PQ + eac3).
            if (!sourceIsHdr)
            {
                RemoveHevcVideoProfiles(profileNonNull, _logger);
            }
            else
            {
                RemoveHevcCodecProfiles(profileNonNull, _logger);
                _logger.LogInformation(
                    "G5-F (Apple) : source HDR — profil hevc-fmp4 conservé (remux HEVC→fMP4, route HLS standard)");
            }
        }

        var streamInfo = item.MediaType == MediaType.Audio
            ? streamBuilder.GetOptimalAudioStream(options)
            : streamBuilder.GetOptimalVideoStream(options);

        if (streamInfo is not null)
        {
            // G5-B2 (Shield) : pour les clients capables de lire du TS direct
            // (profil de transcodage `ts`/`http` — apps Android/TV), forcer la route
            // progressive si l'encodeur HW mediacodec est sélectionné : cet encodeur
            // ne délivre que via TS continu (endurance 90 s validée on-device),
            // les encapsulateurs HLS à frontière de segment stallent. Le web
            // (profil hls/fmp4, incapable de jouer du TS) reste sur la route SW.
            var tsCapable = IsTsCapableClient(profileNonNull.TranscodingProfiles, clientName);
            var profilsClient = profileNonNull.TranscodingProfiles
                .Where(i => i.Type == DlnaProfileType.Video)
                .Select(i => $"{i.Protocol}:{i.Container}")
                .ToList();
            _logger.LogDebug(
                "G5-B2 debug : client={Client} ; device={Device} ; profilName={PName} ; profilId={PId} ; profils=[{Profils}] ; tsCapable={TsCapable} ; subProtocol={Proto}",
                claimsPrincipal.GetClient() ?? "(aucun)",
                claimsPrincipal.GetDevice() ?? "(aucun)",
                profile?.Name ?? "(aucun)",
                profile?.Id?.ToString() ?? "(aucun)",
                string.Join(", ", profilsClient),
                tsCapable,
                streamInfo.SubProtocol);
            ApplyHwMpegtsRoute(
                streamInfo,
                item.MediaType == MediaType.Video ? DlnaProfileType.Video : DlnaProfileType.Audio,
                streamInfo.PlayMethod,
                _serverConfigurationManager.GetEncodingOptions().HardwareAccelerationType,
                tsCapable,
                clientName,
                sourceIsHdr,
                _logger);

            if (streamInfo.SubProtocol == MediaStreamProtocol.hls
                && string.Equals(streamInfo.Container, "ts", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "G5-C4 : transcodage vidéo route HLS segments TS (client ts-capable, HW mediacodec actif).");
            }
            else if (streamInfo.SubProtocol == MediaStreamProtocol.http
                && streamInfo.Container == "ts")
            {
                _logger.LogInformation(
                    "G5-B2 : transcodage vidéo route progressive TS (client ts-capable, HW mediacodec actif).");
            }

            streamInfo.PlaySessionId = playSessionId;
            streamInfo.StartPositionTicks = startTimeTicks;

            mediaSource.SupportsDirectPlay = streamInfo.PlayMethod == PlayMethod.DirectPlay;

            // Players do not handle this being set according to PlayMethod
            mediaSource.SupportsDirectStream =
                options.EnableDirectStream
                    ? streamInfo.PlayMethod == PlayMethod.DirectPlay || streamInfo.PlayMethod == PlayMethod.DirectStream
                    : streamInfo.PlayMethod == PlayMethod.DirectPlay;

            mediaSource.SupportsTranscoding =
                streamInfo.PlayMethod == PlayMethod.DirectStream
                || mediaSource.TranscodingContainer is not null
                || profileNonNull.TranscodingProfiles.Any(i => i.Type == streamInfo.MediaType && i.Context == options.Context);

            if (item is Audio)
            {
                if (!user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding))
                {
                    mediaSource.SupportsTranscoding = false;
                }
            }
            else if (item is Video)
            {
                if (!user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding)
                    && !user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding)
                    && !user.HasPermission(PermissionKind.EnablePlaybackRemuxing))
                {
                    mediaSource.SupportsTranscoding = false;
                }
            }

            if (mediaSource.IsRemote && user.HasPermission(PermissionKind.ForceRemoteSourceTranscoding))
            {
                mediaSource.SupportsDirectPlay = false;
                mediaSource.SupportsDirectStream = false;

                mediaSource.TranscodingUrl = streamInfo.ToUrl(null, claimsPrincipal.GetToken(), "&allowVideoStreamCopy=false&allowAudioStreamCopy=false");
                mediaSource.TranscodingContainer = streamInfo.Container;
                mediaSource.TranscodingSubProtocol = streamInfo.SubProtocol;
                if (streamInfo.AlwaysBurnInSubtitleWhenTranscoding)
                {
                    mediaSource.TranscodingUrl += "&alwaysBurnInSubtitleWhenTranscoding=true";
                }
            }
            else
            {
                if (!mediaSource.SupportsDirectPlay && (mediaSource.SupportsTranscoding || mediaSource.SupportsDirectStream))
                {
                    streamInfo.PlayMethod = PlayMethod.Transcode;
                    mediaSource.TranscodingUrl = streamInfo.ToUrl(null, claimsPrincipal.GetToken(), null);

                    if (!allowVideoStreamCopy)
                    {
                        mediaSource.TranscodingUrl += "&allowVideoStreamCopy=false";
                    }

                    if (!allowAudioStreamCopy)
                    {
                        mediaSource.TranscodingUrl += "&allowAudioStreamCopy=false";
                    }

                    if (streamInfo.AlwaysBurnInSubtitleWhenTranscoding)
                    {
                        mediaSource.TranscodingUrl += "&alwaysBurnInSubtitleWhenTranscoding=true";
                    }
                }
            }

            // Do this after the above so that StartPositionTicks is set
            // The token must not be null
            SetDeviceSpecificSubtitleInfo(streamInfo, mediaSource, claimsPrincipal.GetToken()!);
            mediaSource.DefaultAudioStreamIndex = streamInfo.AudioStreamIndex;
        }

        foreach (var attachment in mediaSource.MediaAttachments)
        {
            attachment.DeliveryUrl = string.Format(
                CultureInfo.InvariantCulture,
                "/Videos/{0}/{1}/Attachments/{2}",
                item.Id,
                mediaSource.Id,
                attachment.Index);
        }
    }

    /// <summary>
    /// Sort media source.
    /// </summary>
    /// <param name="result">Playback info response.</param>
    /// <param name="maxBitrate">Max bitrate.</param>
    /// <param name="preferredItemId">The id of the queried item, whose own media source must stay the default.</param>
    public void SortMediaSources(PlaybackInfoResponse result, long? maxBitrate, Guid preferredItemId = default)
    {
        var originalList = result.MediaSources.ToList();

        // The queried item's source carries the user's resume state for that version, so it must stay the
        // default the client plays. An unfavorable bitrate means transcoding it, not switching to a sibling version.
        var preferredId = preferredItemId.IsEmpty()
            ? null
            : preferredItemId.ToString("N", CultureInfo.InvariantCulture);

        result.MediaSources = result.MediaSources
            .OrderByDescending(i => preferredId is not null && string.Equals(i.Id, preferredId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(i =>
            {
                // Nothing beats direct playing a file
                if (i.SupportsDirectPlay && i.Protocol == MediaProtocol.File)
                {
                    return 0;
                }

                return 1;
            })
            .ThenBy(i =>
            {
                // Let's assume direct streaming a file is just as desirable as direct playing a remote url
                if (i.SupportsDirectPlay || i.SupportsDirectStream)
                {
                    return 0;
                }

                return 1;
            })
            .ThenBy(i =>
            {
                return i.Protocol switch
                {
                    MediaProtocol.File => 0,
                    _ => 1,
                };
            })
            .ThenBy(i =>
            {
                if (maxBitrate.HasValue && i.Bitrate.HasValue)
                {
                    return i.Bitrate.Value <= maxBitrate.Value ? 0 : 2;
                }

                return 1;
            })
            .ThenBy(originalList.IndexOf)
            .ToArray();
    }

    /// <summary>
    /// Open media source.
    /// </summary>
    /// <param name="httpContext">Http Context.</param>
    /// <param name="request">Live stream request.</param>
    /// <returns>A <see cref="Task"/> containing the <see cref="LiveStreamResponse"/>.</returns>
    public async Task<LiveStreamResponse> OpenMediaSource(HttpContext httpContext, LiveStreamRequest request)
    {
        var result = await _mediaSourceManager.OpenLiveStream(request, CancellationToken.None).ConfigureAwait(false);

        RewritePublishedLiveStreamPath(result.MediaSource, httpContext.Request);

        var profile = request.DeviceProfile;
        if (profile is null)
        {
            var clientCapabilities = _deviceManager.GetCapabilities(httpContext.User.GetDeviceId());
            if (clientCapabilities is not null)
            {
                profile = clientCapabilities.DeviceProfile;
            }
        }

        if (profile is not null)
        {
            var item = _libraryManager.GetItemById<BaseItem>(request.ItemId)
                ?? throw new ResourceNotFoundException();

            SetDeviceSpecificData(
                item,
                result.MediaSource,
                profile,
                httpContext.User,
                request.MaxStreamingBitrate,
                request.StartTimeTicks ?? 0,
                result.MediaSource.Id,
                request.AudioStreamIndex,
                request.SubtitleStreamIndex,
                request.MaxAudioChannels,
                request.PlaySessionId,
                request.UserId,
                request.EnableDirectPlay,
                request.EnableDirectStream,
                true,
                true,
                true,
                request.AlwaysBurnInSubtitleWhenTranscoding,
                httpContext.GetNormalizedRemoteIP());
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(result.MediaSource.TranscodingUrl))
            {
                result.MediaSource.TranscodingUrl += "&LiveStreamId=" + result.MediaSource.LiveStreamId;
            }
        }

        // here was a check if (result.MediaSource is not null) but Rider said it will never be null
        NormalizeMediaSourceContainer(result.MediaSource, profile!, DlnaProfileType.Video);

        return result;
    }

    /// <summary>
    /// Normalize media source container.
    /// </summary>
    /// <param name="mediaSource">Media source.</param>
    /// <param name="profile">Device profile.</param>
    /// <param name="type">Dlna profile type.</param>
    public void NormalizeMediaSourceContainer(MediaSourceInfo mediaSource, DeviceProfile profile, DlnaProfileType type)
    {
        mediaSource.Container = StreamBuilder.NormalizeMediaSourceFormatIntoSingleContainer(mediaSource.Container, profile, type);
    }

    /// <summary>
    /// G5-B2/G5-C4 (Shield) : bascule la route de transcodage vidéo pour les clients
    /// TS-capables quand l'encodeur HW mediacodec est sélectionné. G5-C4 : si le
    /// StreamBuilder a déjà retenu la route HLS segments TS (profil client `hls`+`ts`,
    /// demandée nativement par les apps Android), on la préserve — l'ExoPlayer refuse
    /// le TS progressif (constat source error) mais sait lire les segments TS ;
    /// fallback TS progressif pour les clients n'offrant que `http`+`ts`.
    /// G5-D (iPhone) : les clients Apple (AVFoundation) ne consomment pas le TS
    /// progressif — AVPlayer exige du HLS : ils sont forcés sur la route HLS
    /// byte-range (le TS continu servi par la playlist HLS du serveur).
    /// </summary>
    /// <param name="streamInfo">Le stream info construit par le StreamBuilder.</param>
    /// <param name="mediaType">Type de média de l'élément lu.</param>
    /// <param name="playMethod">Méthode de lecture retenue par le StreamBuilder.</param>
    /// <param name="hardwareAccelerationType">Type d'accélération matérielle de la config.</param>
    /// <param name="tsCapableClient">Vrai si le profil client a un profil de transcodage `ts`/`http`/`hls`.</param>
    /// <param name="clientName">Le nom du client (claims) pour le jeton Apple (AVFoundation).</param>
    /// <param name="sourceIsHdr">Vrai si la source vidéo est HDR (PQ/HLG/DOVI) — les
    /// clients Apple consomment alors le remux HEVC→fMP4 (la fMP4 du StreamBuilder
    /// est conservée) au lieu du forçage TS-H264.</param>
    /// <param name="log">Le logger de l'instance (diagnostic de route).</param>
    public static void ApplyHwMpegtsRoute(
        StreamInfo streamInfo,
        DlnaProfileType mediaType,
        PlayMethod playMethod,
        HardwareAccelerationType hardwareAccelerationType,
        bool tsCapableClient,
        string? clientName,
        bool sourceIsHdr,
        ILogger? log)
    {
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (mediaType != DlnaProfileType.Video
            || playMethod != PlayMethod.Transcode
            || hardwareAccelerationType != HardwareAccelerationType.mediacodec
            || !tsCapableClient)
        {
            log?.LogDebug(
                "G5-B2 debug : PAS de bascule ; mediaType={MT} ; playMethod={PM} ; HW={HW} ; tsCapable={TS}",
                mediaType,
                playMethod,
                hardwareAccelerationType,
                tsCapableClient);
            return;
        }

        // G5-D (iOS) : AVFoundation refuse le TS progressif ET le TS segmenté avec
        // l'encodeur HW (le muxer -f hls stalle, matrice G5-A) — AVPlayer ne
        // consomme correctement que le TS continu via une playlist VOD serveur.
        // La branche est AVANT G5-C4 : les clients Apple demandent déjà `hls`+`ts`
        // dans leurs profils, la préservation les ferait sortir trop tôt.
        // G5-F : une source HDR consomme le remux HEVC→fMP4 (le profil hevc-fmp4 du
        // client est conservé côté StreamBuilder) — le forçage TS ne vaut que pour
        // l'encodage H264 (route byte-range HW). AVFoundation lit le HEVC fMP4
        // nativement (y compris DV/HDR10, tag hvc1 posé par le muxer).
        if (IsAppleClient(clientName) && !sourceIsHdr)
        {
            log?.LogDebug(
                "G5-D debug : client Apple ({Client}) — route HLS byte-range forcée (AVFoundation)",
                clientName);
            streamInfo.SubProtocol = MediaStreamProtocol.hls;
            streamInfo.Container = "ts";
            return;
        }

        if (IsAppleClient(clientName) && sourceIsHdr)
        {
            log?.LogDebug(
                "G5-F debug : client Apple ({Client}) — source HDR : fMP4 conservé (remux HEVC, route HLS standard)",
                clientName);
            return;
        }

        // G5-C4 : route HLS segments TS préservée quand le client l'a demandée
        // elle-même (profil `hls`+`ts` — le Player Android sait la lire nativement).
        if (streamInfo.SubProtocol == MediaStreamProtocol.hls
            && string.Equals(streamInfo.Container, "ts", StringComparison.OrdinalIgnoreCase))
        {
            log?.LogDebug(
                "G5-C4 debug : route HLS TS préservée (profil client hls+ts) ; subProtocol={Proto} ; container={Cont}",
                streamInfo.SubProtocol,
                streamInfo.Container);
            return;
        }

        // Fallback TS progressif (G5-B2) : /videos/{id}/stream.ts — muxer mpegts continu
        // (l'unique sortie viable de l'encodeur HW h264_mediacodec, endurance 90 s on-device).
        streamInfo.SubProtocol = MediaStreamProtocol.http;
        streamInfo.Container = "ts";
    }

    /// <summary>
    /// Identifie les clients Apple (AVFoundation/AVPlayer) : leurs players ne
    /// consomment pas le TS progressif — uniquement du HLS.
    /// </summary>
    /// <param name="clientName">Le nom du client issu des claims.</param>
    /// <returns>Vrai pour les clients Jellyfin iOS / Apple TV / Mac.</returns>
    public static bool IsAppleClient(string? clientName)
    {
        if (string.IsNullOrEmpty(clientName))
        {
            return false;
        }

        return clientName.StartsWith("Jellyfin iOS", StringComparison.OrdinalIgnoreCase)
            || clientName.StartsWith("Jellyfin Apple", StringComparison.OrdinalIgnoreCase)
            || clientName.StartsWith("Jellyfin Mac", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clone d'un DeviceProfile pour le filtrage hevc Apple : les listes MUTÉES par
    /// le filtrage (TranscodingProfiles copiées en profondeur via le constructeur
    /// de copie natif ; CodecProfiles laissés par référence — la mutation G5-F est
    /// une RÉASSIGNATION de la propriété sur le clone, l'array partagé avec
    /// l'original du cache n'est jamais modifié sur place), les listes LUES par le
    /// StreamBuilder mais jamais mutées (DirectPlay/Container/Subtitle) restent
    /// par référence — l'original du cache ne voit aucune amputation.
    /// </summary>
    /// <param name="profile">Le DeviceProfile d'entrée (du DTO ou du cache).</param>
    /// <returns>Le clone avec les TranscodingProfiles copiés en profondeur.</returns>
    private static DeviceProfile CloneProfileForHevcFiltering(DeviceProfile profile)
    {
        var clone = new DeviceProfile
        {
            Name = profile.Name,
            Id = profile.Id,
            MaxStreamingBitrate = profile.MaxStreamingBitrate,
            MaxStaticBitrate = profile.MaxStaticBitrate,
            MusicStreamingTranscodingBitrate = profile.MusicStreamingTranscodingBitrate,
            DirectPlayProfiles = profile.DirectPlayProfiles,
            CodecProfiles = profile.CodecProfiles,
            ContainerProfiles = profile.ContainerProfiles,
            SubtitleProfiles = profile.SubtitleProfiles,
            TranscodingProfiles = profile.TranscodingProfiles
                .Select(p => new TranscodingProfile(p))
                .ToArray()
        };

        return clone;
    }

    /// <summary>
    /// Indique si la source vidéo est SDR — i.e. encodable en H264 sans conversion
    /// de couleur. Les sources HDR (PQ/HLG/DOVI) exigent soit un tonemap (absent du
    /// ffmpeg embarqué : pas de filtre `tonemapx`), soit un remux HEVC→fMP4 pour les
    /// clients Apple (le HLS Apple n'admet l'HDR qu'en HEVC). Sans flux vidéo ou
    /// sans métadonnées couleur (VideoRange calculé SDR), le traitement est
    /// conservateur (SDR → strip hevc-TS).
    /// </summary>
    /// <param name="mediaSource">La source média évaluée.</param>
    /// <returns>Vrai si la vidéo principale est SDR (ou indéterminée : conservateur).</returns>
    public static bool IsSourceSdrVideo(MediaSourceInfo? mediaSource)
    {
        var video = mediaSource?.MediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        return video is null || video.VideoRange == VideoRange.SDR;
    }

    /// <summary>
    /// Retire les CodecProfiles vidéo HEVC d'un client Apple (condition
    /// `VideoCodecTag: hvc1|dvh1` requise — jamais vraie pour un remux depuis un
    /// MKV sans CodecTag : sans retrait, le profil hevc-fmp4 conservé pour la
    /// source HDR n'est jamais éligible côté StreamBuilder).
    /// </summary>
    /// <param name="profile">Le device profile du client.</param>
    /// <param name="log">Le logger (la trace de filtrage : les CodecProfiles retirés).</param>
    public static void RemoveHevcCodecProfiles(DeviceProfile profile, ILogger? log = null)
    {
        if (profile?.CodecProfiles is not { Length: > 0 } allProfiles)
        {
            return;
        }

        var kept = allProfiles
            .Where(p => p.Type != CodecType.Video
                || (!(p.Codec ?? string.Empty).Contains("hevc", StringComparison.OrdinalIgnoreCase)
                    && !(p.Codec ?? string.Empty).Contains("h265", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (kept.Length == allProfiles.Length)
        {
            return;
        }

        log?.LogInformation(
            "G5-F (Apple) : {Removed} CodecProfiles hevc retirés (condition VideoCodecTag hvc1|dvh1 inapplicable à un remux MKV)",
            allProfiles.Length - kept.Length);
        profile.CodecProfiles = kept;
    }

    /// <summary>
    /// Retire le HEVC (orthographes `hevc`/`h265`) des profils de transcodage/remux
    /// vidéo d'un client Apple — sur un CLONE du profil (le DeviceProfile d'origine
    /// peut être la référence partagée du cache GetCapabilities : l'amputation sur
    /// l'original persisterait inter-requêtes). Les profils restants (H.264)
    /// préservent la navigation.
    /// </summary>
    /// <param name="profile">Le device profile du client.</param>
    /// <param name="log">Le logger (la trace de filtrage : les profils retirés).</param>
    public static void RemoveHevcVideoProfiles(DeviceProfile profile, ILogger? log = null)
    {
        if (profile?.TranscodingProfiles is not { Length: > 0 } allProfiles)
        {
            return;
        }

        var kept = allProfiles
            .Where(p => p.Type != DlnaProfileType.Video
                || (!(p.VideoCodec ?? string.Empty).Contains("hevc", StringComparison.OrdinalIgnoreCase)
                    && !(p.VideoCodec ?? string.Empty).Contains("h265", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (kept.Length == allProfiles.Length)
        {
            return;
        }

        log?.LogInformation(
            "G5-D (Apple) : {Removed} profils hevc Codec retirés du DeviceProfile (le HEVC-TS est illisible par AVFoundation)",
            allProfiles.Length - kept.Length);
        profile.TranscodingProfiles = kept;
    }

    /// <summary>
    /// Vérifie si le client est capable de jouer un flux TS progressif : ce sont les
    /// applications natives Android (ExoPlayer, extracteur TS intégré). Le client web
    /// (hls.js → MSE fmp4) est exclu : il ne sait pas jouer un TS brut.
    /// </summary>
    /// <param name="profiles">Liste des profils de transcodage du device client.</param>
    /// <param name="clientName">Nom du client récupéré du claim d'authentification.</param>
    /// <returns>Vrai si le client est une app native (≠ web) avec un profil vidéo `ts`.</returns>
    public static bool IsTsCapableClient(TranscodingProfile[]? profiles, string? clientName)
    {
        // Le web (hls.js, MSE fmp4) ne sait pas jouer un TS brut progressif :
        // seules les apps natives (ExoPlayer) recevront la route TS.
        if (clientName is not null
            && clientName.StartsWith("Jellyfin Web", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (profiles is null || profiles.Length == 0)
        {
            return false;
        }

        return profiles.Any(p => p.Type == DlnaProfileType.Video
            && (p.Protocol == MediaStreamProtocol.http || p.Protocol == MediaStreamProtocol.hls)
            && !string.IsNullOrEmpty(p.Container)
            && p.Container.Contains("ts", StringComparison.OrdinalIgnoreCase));
    }

    private void SetDeviceSpecificSubtitleInfo(StreamInfo info, MediaSourceInfo mediaSource, string accessToken)
    {
        var profiles = info.GetSubtitleProfiles(_mediaEncoder, false, "-", accessToken);
        mediaSource.DefaultSubtitleStreamIndex = info.SubtitleStreamIndex;

        mediaSource.TranscodeReasons = info.TranscodeReasons;

        foreach (var profile in profiles)
        {
            foreach (var stream in mediaSource.MediaStreams)
            {
                if (stream.Type == MediaStreamType.Subtitle && stream.Index == profile.Index)
                {
                    stream.DeliveryMethod = profile.DeliveryMethod;

                    if (profile.DeliveryMethod == SubtitleDeliveryMethod.External)
                    {
                        stream.DeliveryUrl = profile.Url.TrimStart('-');
                        stream.IsExternalUrl = profile.IsExternalUrl;
                    }
                }
            }
        }
    }

    private int? GetMaxBitrate(int? clientMaxBitrate, User user, IPAddress ipAddress)
    {
        var maxBitrate = clientMaxBitrate;
        var remoteClientMaxBitrate = user.RemoteClientBitrateLimit ?? 0;

        if (remoteClientMaxBitrate <= 0)
        {
            remoteClientMaxBitrate = _serverConfigurationManager.Configuration.RemoteClientBitrateLimit;
        }

        if (remoteClientMaxBitrate > 0)
        {
            var isInLocalNetwork = _networkManager.IsInLocalNetwork(ipAddress);

            _logger.LogInformation("RemoteClientBitrateLimit: {0}, RemoteIP: {1}, IsInLocalNetwork: {2}", remoteClientMaxBitrate, ipAddress, isInLocalNetwork);
            if (!isInLocalNetwork)
            {
                maxBitrate = Math.Min(maxBitrate ?? remoteClientMaxBitrate, remoteClientMaxBitrate);
            }
        }

        return maxBitrate;
    }

    /// <summary>
    /// Rewrites a Live TV media source's <see cref="MediaSourceInfo.Path"/> to the request-appropriate published
    /// URL when it points at a Jellyfin-hosted live stream buffer, so response copies never leak server-local
    /// addresses. Only opened live streams are eligible. The shared instance held by
    /// <see cref="IMediaSourceManager"/> is never touched by this method.
    /// </summary>
    /// <param name="mediaSource">The media source clone to rewrite in place.</param>
    /// <param name="request">The current <see cref="HttpRequest"/>.</param>
    private void RewritePublishedLiveStreamPath(MediaSourceInfo mediaSource, HttpRequest request)
    {
        // Opened live streams always carry a LiveStreamId; this excludes pre-open and plugin/remote sources.
        if (string.IsNullOrEmpty(mediaSource.LiveStreamId))
        {
            return;
        }

        var baseUrl = _serverConfigurationManager.GetNetworkConfiguration().BaseUrl;
        var publishedPath = GetPublishedLiveStreamPath(_appHost.GetSmartApiUrl(request), mediaSource.Path, mediaSource.Protocol, baseUrl);

        if (publishedPath is not null)
        {
            mediaSource.Path = publishedPath;
            return;
        }

        if (mediaSource.Path is not null && mediaSource.Path.Contains("/LiveTv/LiveStreamFiles/", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Not rewriting live stream path for media source {MediaSourceId}: the local path did not resolve under the request's smart API URL/BaseUrl", mediaSource.Id);
        }
    }

    /// <summary>
    /// Resolves a Jellyfin-hosted Live TV buffer path to its request-appropriate published equivalent.
    /// Returns null when the path isn't a Jellyfin-hosted <c>/LiveTv/LiveStreamFiles/</c> HTTP URL.
    /// </summary>
    /// <param name="smartApiUrl">The request-appropriate base URL, as returned by <see cref="IServerApplicationHost.GetSmartApiUrl(HttpRequest)"/>.</param>
    /// <param name="localPath">The media source's local (LAN-access) path, as built from <see cref="IServerApplicationHost.GetApiUrlForLocalAccess"/>.</param>
    /// <param name="protocol">The media source's protocol.</param>
    /// <param name="baseUrl">The server's configured BaseUrl, if any.</param>
    /// <returns>The published path, or null if the local path should be left unchanged.</returns>
    internal static string? GetPublishedLiveStreamPath(
        string smartApiUrl,
        string? localPath,
        MediaProtocol protocol,
        string baseUrl)
    {
        if (protocol != MediaProtocol.Http
            || !Uri.TryCreate(localPath, UriKind.Absolute, out var localUri))
        {
            return null;
        }

        var relativePath = localUri.PathAndQuery;
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var basePrefix = baseUrl + "/";
            if (!relativePath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            relativePath = relativePath[baseUrl.Length..];
        }

        if (!relativePath.StartsWith("/LiveTv/LiveStreamFiles/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var prefix = smartApiUrl.TrimEnd('/');
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var includesBaseUrl = Uri.TryCreate(prefix, UriKind.Absolute, out var publishedUri)
                && Uri.UnescapeDataString(publishedUri.AbsolutePath)
                    .TrimEnd('/')
                    .EndsWith(baseUrl, StringComparison.OrdinalIgnoreCase);

            if (!includesBaseUrl)
            {
                prefix += baseUrl;
            }
        }

        return prefix + relativePath;
    }
}
