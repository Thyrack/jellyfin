using System;
using System.Globalization;
using System.Linq;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /proc/uptime (première colonne, secondes).
/// </summary>
public static class ProcUptimeParser
{
    /// <summary>
    /// Extrait l'uptime du host en secondes.
    /// </summary>
    /// <param name="content">Contenu du fichier.</param>
    /// <returns>Secondes, ou null si illisible.</returns>
    public static double? ParseUptimeSeconds(string content)
    {
        var champ = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(0);
        return double.TryParse(champ, NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : null;
    }
}
