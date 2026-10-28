using System.Globalization;
using Xunit;

namespace Jellyfin.Extensions.Tests
{
    /// <summary>
    /// Valide l'initialisation d'ICU4N (via <see cref="StringExtensions.Transliterated"/>) sous
    /// culture invariante. Deux constats serveur : (00:49) un thread portant une culture
    /// spécifique faisait échouer le static-ctor d'ICU4N ; (01:58) même avec une hygiène
    /// culture parfaite, le static-ctor tente <c>new CultureInfo("en")</c> (fallback du
    /// bundle), interdit en globalization-invariant — d'où le repli « translitération
    /// désactivée » implémenté côté <see cref="StringExtensions"/>.
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

        [Fact]
        public static void Transliterated_ne_leve_jamais_sur_textes_exotiques()
        {
            var ancienne = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                foreach (var texte in new[] { "日本語のタイトル", "Le 🎬 Film", "Терра (Россия)", string.Empty })
                {
                    var resultat = texte.Transliterated();   // ne doit jamais lancer (ICU4N indisponible ou non)
                    Assert.NotNull(resultat);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = ancienne;
            }
        }
    }
}
