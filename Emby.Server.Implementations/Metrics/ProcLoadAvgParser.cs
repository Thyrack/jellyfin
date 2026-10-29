using System;
using System.Globalization;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /proc/loadavg (séparateur décimal « . », lecture en InvariantCulture).
/// </summary>
public static class ProcLoadAvgParser
{
    /// <summary>
    /// Extrait la charge moyenne 1 min et 5 min.
    /// </summary>
    /// <param name="content">Contenu du fichier.</param>
    /// <returns>Tuple (Load1, Load5) ; nulls si colonnes absentes.</returns>
    public static (double? Load1, double? Load5) Parse(string content)
    {
        var champs = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (Champ(champs, 0), Champ(champs, 1));
    }

    private static double? Champ(string[] champs, int index)
    {
        return champs.Length > index
            && double.TryParse(champs[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : null;
    }
}
