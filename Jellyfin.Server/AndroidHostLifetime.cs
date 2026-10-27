using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Server
{
    /// <summary>
    /// Host lifetime sans enregistrement de signaux Posix : le PAL CoreCLR Android ne les
    /// supporte pas (PlatformNotSupportedException au PosixSignalRegistration — constat smoke T5).
    /// L'arrêt se fait par destruction du process par le wrapper Android.
    /// </summary>
    public sealed class AndroidHostLifetime : IHostLifetime
    {
        /// <summary>
        /// Aucune initialisation : pas d'enregistrement de sons de signaux SIGTERM/SIGINT.
        /// </summary>
        /// <param name="cancellationToken">Jeton d'annulation de l'hôte.</param>
        /// <returns>Tâche déjà achevée.</returns>
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Aucun arrêt coordonné : le process est détruit par le wrapper.
        /// </summary>
        /// <param name="cancellationToken">Jeton d'annulation de l'hôte.</param>
        /// <returns>Tâche déjà achevée.</returns>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
