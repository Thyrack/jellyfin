using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO des compteurs de sessions.
/// </summary>
public record HostSessionsDto
{
    /// <summary>
    /// Gets le nombre de sessions actives (moins de 300 s).
    /// </summary>
    public required int Active { get; init; }

    /// <summary>
    /// Gets le nombre de sessions en transcodage.
    /// </summary>
    public required int Transcoding { get; init; }

    internal static HostSessionsDto FromHostSessionMetricsInfo(HostSessionMetricsInfo model)
    {
        return new()
        {
            Active = model.Active,
            Transcoding = model.Transcoding
        };
    }
}
