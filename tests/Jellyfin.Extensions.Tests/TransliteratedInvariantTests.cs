using System.Globalization;
using Xunit;

namespace Jellyfin.Extensions.Tests
{
    /// <summary>
    /// Valide l'initialisation d'ICU4N (via <see cref="StringExtensions.Transliterated"/>) sous
    /// culture invariante — le static-ctor d'ICU4N.Globalization.UCultureInfo tentait
    /// <c>new CultureInfo("fr-FR")</c> quand un thread portait une culture spécifique
    /// (globalization-invariant mode → CultureNotFoundException, constat serveur Shield 00:49).
    /// </summary>
    public static class TransliteratedInvariantTests
    {
        [Fact]
        public static void Transliterated_sous_culture_invariante_ne_leve_pas()
        {
            var ancienne = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var resultat = "École".Transliterated();
                Assert.Equal("ecole", resultat);
            }
            finally
            {
                CultureInfo.CurrentCulture = ancienne;
            }
        }

        [Fact]
        public static void Transliterated_texte_deja_ascii_renvoie_texte()
        {
            var ancienne = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                Assert.Equal("abc 123", "abc 123".Transliterated());
            }
            finally
            {
                CultureInfo.CurrentCulture = ancienne;
            }
        }
    }
}
