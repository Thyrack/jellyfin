namespace MediaBrowser.Model.System;

/// <summary>
/// Métrique CPU globale du host.
/// </summary>
public record CpuHostMetricsInfo
{
    /// <summary>
    /// Gets le pourcentage d'occupation CPU global (null tant que 2 échantillons ne sont pas accumulés).
    /// </summary>
    public required double? Percent { get; init; }

    /// <summary>
    /// Gets le nombre de cœurs logiques du host.
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
}
