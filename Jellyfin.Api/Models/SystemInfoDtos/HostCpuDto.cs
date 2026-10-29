using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO de la métrique CPU globale du host.
/// </summary>
public record HostCpuDto
{
    /// <summary>
    /// Gets le pourcentage d'occupation CPU global, null si non mesurable.
    /// </summary>
    public required double? Percent { get; init; }

    /// <summary>
    /// Gets le nombre de cœurs logiques.
    /// </summary>
    public required int CoreCount { get; init; }

    /// <summary>
    /// Gets la charge moyenne sur 1 minute.
    /// </summary>
    public required double? Load1 { get; init; }

    /// <summary>
    /// Gets la charge moyenne sur 5 minutes.
    /// </summary>
    public required double? Load5 { get; init; }

    internal static HostCpuDto FromCpuHostMetricsInfo(CpuHostMetricsInfo model)
    {
        return new()
        {
            Percent = model.Percent,
            CoreCount = model.CoreCount,
            Load1 = model.Load1,
            Load5 = model.Load5
        };
    }
}
