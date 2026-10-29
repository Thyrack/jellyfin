using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.System;

/// <summary>
/// Instantané des métriques du host exposé par le serveur.
/// </summary>
public record HostMetricsInfo
{
    /// <summary>
    /// Gets la date et l'heure UTC du snapshot.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets la métrique CPU globale du host.
    /// </summary>
    public required CpuHostMetricsInfo Cpu { get; init; }

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
    /// Gets la métrique mémoire du processus serveur.
    /// </summary>
    public required ProcessHostMetricsInfo Process { get; init; }

    /// <summary>
    /// Gets la durée de vie du host en secondes.
    /// </summary>
    public required double? HostUptimeSeconds { get; init; }

    /// <summary>
    /// Gets les zones thermiques du host (liste vide si aucune source).
    /// </summary>
    public required IReadOnlyList<ThermalZoneInfo> ThermalZones { get; init; }

    /// <summary>
    /// Gets les compteurs de sessions.
    /// </summary>
    public required HostSessionMetricsInfo Sessions { get; init; }
}
