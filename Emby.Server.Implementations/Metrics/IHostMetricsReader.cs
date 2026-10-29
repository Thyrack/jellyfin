using System.Collections.Generic;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Abstraction de lecture du système de fichiers procfs/sysfs (injectable pour les tests).
/// </summary>
public interface IHostMetricsReader
{
    /// <summary>
    /// Lit le contenu intégral d'un fichier, ou null si absent ou illisible.
    /// </summary>
    /// <param name="path">Chemin absolu du fichier.</param>
    /// <returns>Contenu brut, ou null.</returns>
    string? ReadAllText(string path);

    /// <summary>
    /// Liste les zones thermiques disponibles sous /sys/class/thermal.
    /// </summary>
    /// <returns>Chemins des répertoires thermal_zone* (liste vide si aucune).</returns>
    IReadOnlyList<string> ListThermalZones();
}
