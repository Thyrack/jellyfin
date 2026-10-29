using Emby.Server.Implementations.Metrics;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Metrics;

public class ProcfsParsersTests
{
    private const string ContenuStatStandard =
        "cpu  100 200 300 3000 100 0 0 0 0 0\n" +
        "cpu0 1 2 3 4 0 0 0 0 0 0\n" +
        "ctxt 12345\n";

    [Fact]
    public void ParseSingleCpuLine_FichierStandard_SommeSansCpu0()
    {
        // user(100)+nice(200)+system(300)+idle(3000)+iowait(100)+irq(0)+softirq(0)+steal(0) = 3700
        // guest et guest_nice (2 dernières colonnes) exclus.
        var sample = ProcStatParser.ParseSingleCpuLine(ContenuStatStandard);
        Assert.Equal(3700L, sample?.Total);
        Assert.Equal(3000L, sample?.Idle);
        Assert.Equal(100L, sample?.Iowait);
    }

    [Fact]
    public void ParseSingleCpuLine_SansLigneCpu_RetourNull()
        => Assert.Null(ProcStatParser.ParseSingleCpuLine("ctxt 12345\nintr 987\n"));

    [Fact]
    public void ParseSingleCpuLine_ColonnesInsuffisantes_RetourNull()
        => Assert.Null(ProcStatParser.ParseSingleCpuLine("cpu 1 2 3\n"));

    [Fact]
    public void ParseSingleCpuLine_TokensNonNumeriques_RetourNull()
        => Assert.Null(ProcStatParser.ParseSingleCpuLine("cpu A B C D E\n"));

    [Fact]
    public void ParseSingleCpuLine_NoyauMinimal5Colonnes_Accepte()
        => Assert.Equal(11L, ProcStatParser.ParseSingleCpuLine("cpu 1 2 3 4 1\n")?.Total);

    [Fact]
    public void ParseSingleCpuLine_GuestNonAdditionne()
    {
        // guest (col 9=50) est déjà inclus dans user côté noyau : total = user..steal = 100.
        var sample = ProcStatParser.ParseSingleCpuLine("cpu 100 0 0 0 0 0 0 0 50 0\n");
        Assert.Equal(100L, sample?.Total);
    }

    [Fact]
    public void ComputePercent_SansPrecedent_RetourNull()
        => Assert.Null(CpuMetricsCalculator.ComputePercent(null, new CpuSample(100, 50, 0)));

    [Fact]
    public void ComputePercent_DeltaNormal_PourcentageCorrect()
    {
        var avant = new CpuSample(1000, 800, 20);    // busy = 180
        var apres = new CpuSample(2000, 1600, 40);   // busy = 360
        Assert.Equal(18.0, CpuMetricsCalculator.ComputePercent(avant, apres)!.Value, 6);
    }

    [Fact]
    public void ComputePercent_DeltaTotalNul_RetourZero()
        => Assert.Equal(0.0, CpuMetricsCalculator.ComputePercent(
            new CpuSample(100, 90, 2), new CpuSample(100, 90, 2)));

    [Fact]
    public void ComputePercent_CompteursRegressent_RetourZero()
    {
        // après redémarrage de noyau/conteneur, les compteurs reculent : toujours 0.0 (clamp bas)
        Assert.Equal(0.0, CpuMetricsCalculator.ComputePercent(
            new CpuSample(500, 100, 5), new CpuSample(400, 90, 4)));
    }

    [Fact]
    public void ParseMemInfo_Standard_ChampsExtraits()
    {
        var (total, available, swapTotal, swapFree) = ProcMemInfoParser.Parse(
            "MemTotal:       2480008 kB\n" +
            "MemFree:          95000 kB\n" +
            "MemAvailable:    950000 kB\n" +
            "Buffers:            100 kB\n" +
            "Cached:            50000 kB\n" +
            "SwapTotal:       524200 kB\n" +
            "SwapFree:        410000 kB\n");
        Assert.Equal(2480008L, total);
        Assert.Equal(950000L, available);
        Assert.Equal(524200L, swapTotal);
        Assert.Equal(410000L, swapFree);
    }

    [Fact]
    public void ParseMemInfo_SwapAbsent_ChampsNull()
    {
        var (total, available, swapTotal, swapFree) =
            ProcMemInfoParser.Parse("MemTotal: 1000 kB\nMemAvailable: 200 kB\n");
        Assert.Equal(1000L, total);
        Assert.Equal(200L, available);
        Assert.Null(swapTotal);
        Assert.Null(swapFree);
    }

    [Fact]
    public void ParseMemInfo_ContenuVide_ToutNull()
    {
        var (total, available, swapTotal, swapFree) = ProcMemInfoParser.Parse(string.Empty);
        Assert.Null(total);
        Assert.Null(available);
        Assert.Null(swapTotal);
        Assert.Null(swapFree);
    }

    [Theory]
    [InlineData("3856.21 7345.60\n", 3856.21)]
    [InlineData("0 0\n", 0.0)]
    public void ParseUptimeSeconds_ValeursStandard(string contenu, double attendu)
        => Assert.Equal(attendu, ProcUptimeParser.ParseUptimeSeconds(contenu)!.Value, 3);

    [Fact]
    public void ParseUptimeSeconds_ContenuVide_RetourNull()
        => Assert.Null(ProcUptimeParser.ParseUptimeSeconds(string.Empty));

    [Theory]
    [InlineData("1.24 0.92 3/426 2214\n", 1.24, 0.92)]
    [InlineData("0.00 0.00 0/100 42\n", 0.0, 0.0)]
    public void ParseLoadAvg_ValeursStandard(string contenu, double l1, double l5)
    {
        var (load1, load5) = ProcLoadAvgParser.Parse(contenu);
        Assert.Equal(l1, load1!.Value, 3);
        Assert.Equal(l5, load5!.Value, 3);
    }

    [Fact]
    public void ParseLoadAvg_ColonnesInsuffisantes_ToutNull()
    {
        var (load1, load5) = ProcLoadAvgParser.Parse("1.24\n");
        Assert.Equal(1.24, load1!.Value, 3);
        Assert.Null(load5);
    }

    [Theory]
    [InlineData("57453\n", 57.5)]
    [InlineData("0\n", 0.0)]
    [InlineData("-40000\n", -40.0)]
    public void ParseTempCelsius_Millidegre(string contenu, double attendu)
        => Assert.Equal(attendu, ThermalZoneParser.ParseTempCelsius(contenu)!.Value, 1);

    [Fact]
    public void ParseTempCelsius_ContenuNonNumerique_RetourNull()
        => Assert.Null(ThermalZoneParser.ParseTempCelsius("not-a-number\n"));
}
