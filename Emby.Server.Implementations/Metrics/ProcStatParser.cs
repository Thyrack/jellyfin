using System;
using System.Linq;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse la ligne CPU agrégée de /proc/stat.
/// </summary>
public static class ProcStatParser
{
    /// <summary>
    /// Nombre minimal de colonnes attendues : user, nice, system, idle, iowait.
    /// </summary>
    private const int MinColonnes = 5;

    /// <summary>
    /// Extrait la ligne « cpu » globale d'un contenu de /proc/stat.
    /// </summary>
    /// <param name="content">Contenu intégral du fichier.</param>
    /// <returns>Échantillon agrégé, ou null si absent ou illisible.</returns>
    public static CpuSample? ParseSingleCpuLine(string content)
    {
        var ligne = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (ligne is null)
        {
            return null;
        }

        var mesures = ligne.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(t => long.TryParse(t, out var valeur) ? valeur : (long?)null)
            .TakeWhile(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();
        if (mesures.Length < MinColonnes)
        {
            return null;
        }

        // user..steal = 8 compteurs fixes ; guest/guest_nice (2 dernières colonnes,
        // présentes seulement si Length > 8) sont exclus car déjà inclus dans user par le noyau.
        long total = 0;
        var limite = Math.Min(mesures.Length, 8);
        for (var i = 0; i < limite; i++)
        {
            total += mesures[i];
        }

        return new CpuSample(total, mesures[3], mesures[4]);
    }
}
