using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Xunit;

namespace Jellyfin.Server.Tests
{
    /// <summary>
    /// Tests du configurateur de localisation de requête : en mode globalization-invariant,
    /// une seule culture supportée (l'invariante) — le middleware RequestLocalization ne doit
    /// plus pouvoir appliquer la culture client (fr-FR du header Accept-Language) sur un
    /// thread de requête (constat CultureNotFoundException/ICU4N 00:49).
    /// </summary>
    public static class RequestLocalizationConfiguratorTests
    {
        [Fact]
        public static void Mode_invariant_une_seule_culture_supportee()
        {
            var options = new RequestLocalizationOptions();
            var ancienneCulture = CultureInfo.DefaultThreadCurrentCulture;
            var ancienneUICulture = CultureInfo.DefaultThreadCurrentUICulture;
            try
            {
                RequestLocalizationConfigurator.Configure(options, "fr-FR", invariantMode: true);

                Assert.Single(options.SupportedCultures!);
                Assert.Equal(CultureInfo.InvariantCulture, options.SupportedCultures![0]);
                Assert.Single(options.SupportedUICultures!);
                Assert.Equal(CultureInfo.InvariantCulture, options.SupportedUICultures![0]);
                Assert.Equal(CultureInfo.InvariantCulture, options.DefaultRequestCulture.Culture);
                Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentCulture);
                Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentUICulture);
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentCulture = ancienneCulture;
                CultureInfo.DefaultThreadCurrentUICulture = ancienneUICulture;
            }
        }

        [Fact]
        public static void Mode_normal_conserve_la_liste_complete()
        {
            var options = new RequestLocalizationOptions();
            var ancienne = CultureInfo.DefaultThreadCurrentUICulture;
            try
            {
                RequestLocalizationConfigurator.Configure(options, "fr-FR", invariantMode: false);

                Assert.True(options.SupportedCultures!.Count > 1);
                Assert.Equal("fr-FR", options.DefaultRequestCulture.Culture!.Name);
                Assert.Equal("fr-FR", CultureInfo.DefaultThreadCurrentUICulture!.Name);
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentUICulture = ancienne;
            }
        }

        [Fact]
        public static void Mode_normal_sans_culture_configuree_defaut_en_us()
        {
            var options = new RequestLocalizationOptions();
            var ancienne = CultureInfo.DefaultThreadCurrentUICulture;
            try
            {
                RequestLocalizationConfigurator.Configure(options, null, invariantMode: false);

                Assert.Equal("en-US", options.DefaultRequestCulture.Culture!.Name);
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentUICulture = ancienne;
            }
        }
    }
}
