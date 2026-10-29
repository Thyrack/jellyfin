using System.Collections.Generic;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HostMetrics;

/// <summary>
/// Plugin d'affichage des métriques host : une seule page de configuration embarquée.
/// </summary>
public class HostMetricsPlugin : BasePlugin, IHasWebPages
{
    /// <summary>
    /// Gets le nom du plugin affiché dans le dashboard.
    /// </summary>
    public override string Name => "Métriques host";

    /// <summary>
    /// Gets la description courte du plugin.
    /// </summary>
    public override string Description => "Métriques host du serveur : CPU, RAM, process, thermique, sessions.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "HostMetrics",
            DisplayName = "Métriques host",
            EmbeddedResourcePath = "Jellyfin.Plugin.HostMetrics.embedded.hostmetrics.html",
            EnableInMainMenu = false,
            MenuSection = "server",
            MenuIcon = "cloud"
        };
    }
}
