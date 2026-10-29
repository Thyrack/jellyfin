using System;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Calcul du pourcentage CPU entre deux échantillons /proc/stat.
/// </summary>
public static class CpuMetricsCalculator
{
    /// <summary>
    /// Calcule le pourcentage d'occupation entre deux échantillons (deltas des compteurs cumulés).
    /// </summary>
    /// <param name="previous">Échantillon précédent (null tant qu'aucun échantillon précédent n'est accumulé).</param>
    /// <param name="current">Échantillon courant.</param>
    /// <returns>Pourcentage dans [0, 100] ; 0.0 si delta total ≤ 0 ; null si précédent absent.</returns>
    public static double? ComputePercent(CpuSample? previous, CpuSample current)
    {
        if (previous is null)
        {
            return null;
        }

        var deltaTotal = current.Total - previous.Total;
        if (deltaTotal <= 0)
        {
            return 0.0;
        }

        var deltaActive = Occupation(current) - Occupation(previous);
        return Math.Clamp(deltaActive * 100.0 / deltaTotal, 0.0, 100.0);
    }

    private static long Occupation(CpuSample sample)
        => sample.Total - sample.Idle - sample.Iowait;
}
