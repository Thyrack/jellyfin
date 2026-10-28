using System.Globalization;
using Emby.Server.Implementations.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

namespace Jellyfin.Server;

/// <summary>
/// Configuration des cultures de requête, adaptée au mode globalization-invariant
/// (runtime Android sans données ICU : seul <see cref="CultureInfo.InvariantCulture"/>
/// est instanciable — smoke T5).
/// </summary>
public static class RequestLocalizationConfigurator
{
    /// <summary>
    /// Configure les options de localisation de requête.
    /// En mode invariant, une seule culture supportée (l'invariante) : le middleware
    /// RequestLocalization ne peut plus appliquer la culture du client (ex. fr-FR du
    /// header Accept-Language) sur un thread de requête — c'est ce qui déclenchait
    /// <c>CultureNotFoundException</c> dans le static-ctor d'ICU4N (constat 00:49).
    /// Hors mode invariant : liste complète des cultures UI supportées (comportement upstream).
    /// </summary>
    /// <param name="options">Les options de localisation à configurer.</param>
    /// <param name="serverUICulture">La culture UI configurée pour le serveur.</param>
    /// <param name="invariantMode">Vrai si DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1.</param>
    public static void Configure(RequestLocalizationOptions options, string? serverUICulture, bool invariantMode)
    {
        if (invariantMode)
        {
            // garde sur les threads : culture de formatting ET d'UI invariantes
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            options.SupportedCultures = [CultureInfo.InvariantCulture];
            options.SupportedUICultures = [CultureInfo.InvariantCulture];
            options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture);
        }
        else
        {
            serverUICulture = string.IsNullOrEmpty(serverUICulture) ? "en-US" : serverUICulture;
            var culture = new CultureInfo(serverUICulture);
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            var supportedUICultures = LocalizationManager.GetSupportedUICultures();
            options.SupportedCultures = supportedUICultures;
            options.SupportedUICultures = supportedUICultures;
            options.DefaultRequestCulture = new RequestCulture(serverUICulture);
        }

        options.ApplyCurrentCultureToResponseHeaders = true;
        options.FallBackToParentCultures = true;
        options.FallBackToParentUICultures = true;
    }
}
