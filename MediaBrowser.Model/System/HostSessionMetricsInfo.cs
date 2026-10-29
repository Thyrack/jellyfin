namespace MediaBrowser.Model.System;

/// <summary>
/// Compteurs de sessions sur le serveur.
/// </summary>
public record HostSessionMetricsInfo
{
    /// <summary>
    /// Gets le nombre de sessions actives (activité de moins de 300 s).
    /// </summary>
    public required int Active { get; init; }

    /// <summary>
    /// Gets le nombre de sessions en transcodage.
    /// </summary>
    public required int Transcoding { get; init; }
}
