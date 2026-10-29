using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HostMetrics;

/// <summary>
/// Plugin d'affichage des métriques host : une seule page de configuration embarquée.
/// </summary>
public class HostMetricsPlugin : BasePlugin, IHasWebPages
{
    /// <summary>
    /// Guid fixe du plugin (référencé aussi par le manifeste meta.json du packaging).
    /// </summary>
    public const string GuidConstante = "0b2e4b3e-31ea-4f6b-9d5a-e6f66b7c0b81";

    /// <summary>
    /// Initializes a new instance of the <see cref="HostMetricsPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of <see cref="IApplicationPaths"/> interface.</param>
    public HostMetricsPlugin(IApplicationPaths applicationPaths)
    {
        // Bootstrap à la manière de BasePlugin<T> : le socle BasePlugin ne s'auto-attache pas,
        // Version/Id/AssemblyFilePath sont requis par le PluginManager (sinon null → plugin désactivé).
        var assembly = GetType().Assembly;
        var dataFolderPath = Path.Combine(applicationPaths.PluginsPath, "HostMetrics");
        SetAttributes(assembly.Location, dataFolderPath, assembly.GetName().Version);
        SetId(new Guid(GuidConstante));
    }

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
