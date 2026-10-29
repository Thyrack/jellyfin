using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO de la mémoire du processus serveur.
/// </summary>
public record HostProcessDto
{
    /// <summary>
    /// Gets le working set du processus en octets.
    /// </summary>
    public required long? WorkingSetBytes { get; init; }

    /// <summary>
    /// Gets le tas GC en octets.
    /// </summary>
    public required long? HeapBytes { get; init; }

    /// <summary>
    /// Gets l'uptime du processus en secondes.
    /// </summary>
    public required double? UptimeSeconds { get; init; }

    internal static HostProcessDto FromProcessHostMetricsInfo(ProcessHostMetricsInfo model)
    {
        return new()
        {
            WorkingSetBytes = model.WorkingSetBytes,
            HeapBytes = model.HeapBytes,
            UptimeSeconds = model.UptimeSeconds
        };
    }
}
