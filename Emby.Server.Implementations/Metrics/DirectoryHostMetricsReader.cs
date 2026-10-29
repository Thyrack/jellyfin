using System;
using System.Collections.Generic;
using System.IO;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Lecteur procfs réel : toute erreur (fichier absent, SELinux, IO) renvoie null/liste vide, jamais d'exception.
/// </summary>
public sealed class DirectoryHostMetricsReader : IHostMetricsReader
{
    /// <summary>
    /// Racine noyau des zones thermiques.
    /// </summary>
    private const string RacineThermale = "/sys/class/thermal";

    /// <inheritdoc />
    public string? ReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListThermalZones()
    {
        try
        {
            return Directory.Exists(RacineThermale)
                ? Directory.GetDirectories(RacineThermale, "thermal_zone*")
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
