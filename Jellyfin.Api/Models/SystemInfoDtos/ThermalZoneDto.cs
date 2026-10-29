using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO d'une zone thermique du host.
/// </summary>
public record ThermalZoneDto
{
    /// <summary>
    /// Gets l'identifiant noyau de la zone.
    /// </summary>
    public required string Zone { get; init; }

    /// <summary>
    /// Gets le type déclaré par la zone, null si illisible.
    /// </summary>
    public required string? Type { get; init; }

    /// <summary>
    /// Gets la température en degrés Celsius.
    /// </summary>
    public required double Celsius { get; init; }

    internal static ThermalZoneDto FromThermalZoneInfo(ThermalZoneInfo model)
    {
        return new()
        {
            Zone = model.Zone,
            Type = model.Type,
            Celsius = model.Celsius
        };
    }
}
