using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO des métriques du host (CPU, RAM, process, thermique, sessions).
/// </summary>
public record HostMetricsDto
{
    /// <summary>
    /// Gets l'horodatage UTC du snapshot.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets le CPU global du host.
    /// </summary>
    public required HostCpuDto Cpu { get; init; }

    /// <summary>
    /// Gets la mémoire totale du host en KiB.
    /// </summary>
    public required long? MemoryTotalKiB { get; init; }

    /// <summary>
    /// Gets la mémoire disponible du host en KiB.
    /// </summary>
    public required long? MemoryAvailableKiB { get; init; }

    /// <summary>
    /// Gets la swap totale du host en KiB.
    /// </summary>
    public required long? SwapTotalKiB { get; init; }

    /// <summary>
    /// Gets la swap libre du host en KiB.
    /// </summary>
    public required long? SwapFreeKiB { get; init; }

    /// <summary>
    /// Gets la mémoire du processus serveur.
    /// </summary>
    public required HostProcessDto Process { get; init; }

    /// <summary>
    /// Gets la durée de vie du host en secondes.
    /// </summary>
    public required double? HostUptimeSeconds { get; init; }

    /// <summary>
    /// Gets les zones thermiques du host.
    /// </summary>
    public required IReadOnlyList<ThermalZoneDto> ThermalZones { get; init; }

    /// <summary>
    /// Gets les compteurs de sessions.
    /// </summary>
    public required HostSessionsDto Sessions { get; init; }

    internal static HostMetricsDto FromHostMetricsInfo(HostMetricsInfo model)
    {
        return new()
        {
            Timestamp = model.Timestamp,
            Cpu = HostCpuDto.FromCpuHostMetricsInfo(model.Cpu),
            MemoryTotalKiB = model.MemoryTotalKiB,
            MemoryAvailableKiB = model.MemoryAvailableKiB,
            SwapTotalKiB = model.SwapTotalKiB,
            SwapFreeKiB = model.SwapFreeKiB,
            Process = HostProcessDto.FromProcessHostMetricsInfo(model.Process),
            HostUptimeSeconds = model.HostUptimeSeconds,
            ThermalZones = model.ThermalZones.Select(ThermalZoneDto.FromThermalZoneInfo).ToArray(),
            Sessions = HostSessionsDto.FromHostSessionMetricsInfo(model.Sessions)
        };
    }
}
