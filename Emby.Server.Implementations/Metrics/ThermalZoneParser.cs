using System;
using System.Globalization;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /sys/class/thermal/thermal_zoneN/temp (millidegrés Celsius).
/// </summary>
public static class ThermalZoneParser
{
    /// <summary>
    /// Convertit le contenu « temp » en degrés Celsius (1 décimale).
    /// </summary>
    /// <param name="content">Contenu du fichier temp.</param>
    /// <returns>Température en °C, ou null si illisible.</returns>
    public static double? ParseTempCelsius(string content)
    {
        if (!long.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var millidegre))
        {
            return null;
        }

        return Math.Round(millidegre / 1000.0, 1);
    }
}
