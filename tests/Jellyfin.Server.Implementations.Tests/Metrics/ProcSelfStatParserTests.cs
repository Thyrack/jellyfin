using Emby.Server.Implementations.Metrics;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Metrics;

public class ProcSelfStatParserTests
{
    private const string StatStandard =
        "1369 (dotnet) S 1 1369 1369 0 -1 4194560 12232 0 0 0 120 30 0 0 20 0 12 0 666 1234567 4321 18446744073709551615 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9\n";

    private const string StatCommAvecEspaces =
        "4721 (dotnet merged host) S 1 4721 4721 0 -1 4194560 0 0 0 0 5 2 0 0 20 0 9 0 666 1234567 4321 1 1 2 3\n";

    [Fact]
    public void ParseCpuJiffies_Standard_SommeUtimeStime()
    {
        // utime (f14) = 120, stime (f15) = 30
        Assert.Equal(150L, ProcSelfStatParser.ParseCpuJiffies(StatStandard));
    }

    [Fact]
    public void ParseCpuJiffies_CommAvecEspaces_SommeUtimeStime()
    {
        Assert.Equal(7L, ProcSelfStatParser.ParseCpuJiffies(StatCommAvecEspaces));
    }

    [Fact]
    public void ParseCpuJiffies_TropCourt_RetourNull()
        => Assert.Null(ProcSelfStatParser.ParseCpuJiffies("1369 (dotnet) S 1\n"));

    [Fact]
    public void ParseCpuJiffies_TokensNonNumeriques_RetourNull()
        => Assert.Null(ProcSelfStatParser.ParseCpuJiffies(
            "1369 (x) S 1 2 3 4 5 6 7 8 9 0 A B 0 0 20 0 1 0 666 1 2\n"));

    [Fact]
    public void ParseCpuJiffies_ContenuVide_RetourNull()
        => Assert.Null(ProcSelfStatParser.ParseCpuJiffies(string.Empty));
}
