namespace MediaBrowser.Model.System;

/// <summary>
/// Métrique mémoire du processus serveur dotnet.
/// </summary>
public record ProcessHostMetricsInfo
{
    /// <summary>
    /// Gets le working set du processus en octets.
    /// </summary>
    public required long? WorkingSetBytes { get; init; }

    /// <summary>
    /// Gets la taille du tas du garbage collector en octets.
    /// </summary>
    public required long? HeapBytes { get; init; }

    /// <summary>
    /// Gets la durée de vie du processus serveur en secondes.
    /// </summary>
    public required double? UptimeSeconds { get; init; }
}
