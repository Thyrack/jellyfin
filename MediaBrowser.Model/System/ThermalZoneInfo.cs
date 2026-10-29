namespace MediaBrowser.Model.System;

/// <summary>
/// Zone thermique du host (soc, cpu, gpu…).
/// </summary>
public record ThermalZoneInfo
{
    /// <summary>
    /// Gets l'identifiant noyau de la zone (ex. thermal_zone0).
    /// </summary>
    public required string Zone { get; init; }

    /// <summary>
    /// Gets le type déclaré par la zone (ex. CPU-therm), null si illisible.
    /// </summary>
    public required string? Type { get; init; }

    /// <summary>
    /// Gets la température en degrés Celsius.
    /// </summary>
    public required double Celsius { get; init; }
}
