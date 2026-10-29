using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.HostMetrics;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Common.Tests.Plugins;

public class HostMetricsPluginTests
{
    private static HostMetricsPlugin CreerPlugin()
    {
        var appPaths = new Mock<IApplicationPaths>();
        appPaths.Setup(p => p.PluginsPath).Returns(Path.GetTempPath());
        return new(appPaths.Object);
    }

    [Fact]
    public void GetPages_UneSeulePage_ChampsFideles()
    {
        var pages = CreerPlugin().GetPages().ToList();
        var page = Assert.Single(pages);
        Assert.Equal("HostMetrics", page.Name);
        Assert.Equal("Métriques host", page.DisplayName);
        Assert.Equal("Jellyfin.Plugin.HostMetrics.embedded.hostmetrics.html", page.EmbeddedResourcePath);
        Assert.False(page.EnableInMainMenu);
        Assert.Equal("server", page.MenuSection);
        Assert.Equal("cloud", page.MenuIcon);
    }

    [Fact]
    public void Resource_Embarquee_Lisible()
    {
        var stream = typeof(HostMetricsPlugin).Assembly.GetManifestResourceStream(
            "Jellyfin.Plugin.HostMetrics.embedded.hostmetrics.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();
        Assert.Contains("System/HostMetrics", html, StringComparison.Ordinal);
        Assert.Contains("MESSAGES", html, StringComparison.Ordinal);
        Assert.Contains("jellyfin_credentials", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Nom_Et_Description_Presents()
    {
        var plugin = CreerPlugin();
        Assert.False(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.False(string.IsNullOrWhiteSpace(plugin.Description));
    }

    [Fact]
    public void Bootstrap_Version_Et_Id_Attaches()
    {
        var plugin = CreerPlugin();
        Assert.Equal(new Version(13, 0, 0, 0), plugin.Version);
        Assert.Equal(new Guid(HostMetricsPlugin.GuidConstante), plugin.Id);
        Assert.False(string.IsNullOrWhiteSpace(plugin.AssemblyFilePath));
    }
}
