using MediaBrowser.Model.System;

namespace MediaBrowser.Controller.Metrics;

/// <summary>
/// Fournit l'instantané courant des métriques du host (échantillonné en tâche de fond).
/// </summary>
public interface IHostMetricsService
{
    /// <summary>
    /// Retourne le snapshot le plus récent (immuable), à coût d'une simple lecture.
    /// </summary>
    /// <returns>Instantané des métriques du host.</returns>
    HostMetricsInfo GetSnapshot();
}
