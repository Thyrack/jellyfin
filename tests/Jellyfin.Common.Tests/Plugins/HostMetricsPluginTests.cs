using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.HostMetrics;
using Xunit;

namespace Jellyfin.Common.Tests.Plugins;

public class HostMetricsPluginTests
{
    [Fact]
    public void GetPages_UneSeulePage_ChampsFideles()
    {
        var pages = new HostMetricsPlugin().GetPages().ToList();
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
    }

    [Fact]
    public void Nom_Et_Description_Presents()
    {
        var plugin = new HostMetricsPlugin();
        Assert.False(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.False(string.IsNullOrWhiteSpace(plugin.Description));
    }
}
