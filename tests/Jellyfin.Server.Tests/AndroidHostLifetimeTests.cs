using System.Threading.Tasks;
using Jellyfin.Server;
using Xunit;

namespace Jellyfin.Server.Tests;

public class AndroidHostLifetimeTests
{
    [Fact]
    public async Task WaitForStartAsync_CompleteSansEnregistrementDeSignaux()
    {
        // Le PAL CoreCLR Android ne supporte pas PosixSignalRegistration : le lifetime
        // de substitution ne doit rien enregistrer et se terminer immédiatement.
        var cancellation = TestContext.Current.CancellationToken;
        await new AndroidHostLifetime().WaitForStartAsync(cancellation);
    }

    [Fact]
    public async Task StopAsync_CompletSansCoordonnateur()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await new AndroidHostLifetime().StopAsync(cancellation);
    }
}
