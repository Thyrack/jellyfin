using System;
using System.Linq;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /proc/meminfo (valeurs en KiB).
/// </summary>
public static class ProcMemInfoParser
{
    /// <summary>
    /// Extrait MemTotal, MemAvailable, SwapTotal, SwapFree du contenu.
    /// </summary>
    /// <param name="content">Contenu du fichier.</param>
    /// <returns>Tuple de valeurs en KiB (null si champ absent).</returns>
    public static (long? Total, long? Available, long? SwapTotal, long? SwapFree) Parse(string content)
    {
        return (Extraire(content, "MemTotal:"), Extraire(content, "MemAvailable:"),
            Extraire(content, "SwapTotal:"), Extraire(content, "SwapFree:"));
    }

    private static long? Extraire(string content, string cle)
    {
        var ligne = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith(cle, StringComparison.Ordinal));
        if (ligne is null)
        {
            return null;
        }

        var champ = ligne.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        return long.TryParse(champ, out var valeur) ? valeur : null;
    }
}
