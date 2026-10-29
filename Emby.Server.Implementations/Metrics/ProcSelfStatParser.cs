using System;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /proc/self/stat : utime + stime (jiffies) du processus de l'application,
/// seule source CPU lisible depuis un app Android (SELinux bloque /proc/stat).
/// </summary>
public static class ProcSelfStatParser
{
    /// <summary>
    /// Champ noyau de /proc/stat : utime (14) et stime (15). Ils sont les 12ᵉ et 13ᵉ
    /// jetons après 'comm' (le comm contient des espaces : découper après la dernière parenthèse).
    /// </summary>
    private const int DecalageUtime = 11;

    /// <summary>
    /// Nombre de jetons requis après 'comm' (champs noyau 3 à 16) : atteindre
    /// utime (index 11) et stime (index 12) exige au moins 13 jetons.
    /// </summary>
    private const int MinChampsApresComm = 13;

    /// <summary>
    /// Extrait la somme utime + stime en jiffies du contenu de /proc/self/stat.
    /// </summary>
    /// <param name="content">Contenu intégral du fichier.</param>
    /// <returns>Jiffies cumulés, ou null si illisible.</returns>
    public static long? ParseCpuJiffies(string content)
    {
        var fermeture = content.LastIndexOf(')');
        if (fermeture < 0)
        {
            return null;
        }

        var apresComm = content.Substring(fermeture + 1)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (apresComm.Length < MinChampsApresComm)
        {
            return null;
        }

        var utime = apresComm[DecalageUtime];
        var stime = apresComm[DecalageUtime + 1];
        if (!long.TryParse(utime, out var utimeJiffies)
            || !long.TryParse(stime, out var stimeJiffies))
        {
            return null;
        }

        return utimeJiffies + stimeJiffies;
    }
}
