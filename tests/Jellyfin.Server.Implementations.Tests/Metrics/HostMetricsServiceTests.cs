using System;
using System.Collections.Generic;
using Emby.Server.Implementations.Metrics;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Metrics;

public class HostMetricsServiceTests
{
    private const string StatZero =
        "cpu  1000 200 3000 3000 100 0 0 0 0 0\n"; // Total=7300, Idle=3000, Iowait=100

    private const string StatUn =
        "cpu  2000 200 3600 3400 100 0 0 0 0 0\n"; // Total=9300, Idle=3400, Iowait=100

    [Fact]
    public void GetSnapshot_AvantTouteMesure_TousChampsNulls()
    {
        var service = CreerService(FakeReader.Vide());
        var snap = service.GetSnapshot();
        Assert.Null(snap.Cpu.Percent);
        Assert.Null(snap.MemoryTotalKiB);
        Assert.Null(snap.Process.WorkingSetBytes);
        Assert.Null(snap.HostUptimeSeconds);
        Assert.Empty(snap.ThermalZones);
        Assert.Equal(0, snap.Sessions.Active);
        Assert.Equal(0, snap.Sessions.Transcoding);
    }

    [Fact]
    public void ApresPremierEchantillon_CpuNull_AutresChampsPeuples()
    {
        var service = CreerService(FakeReader.Complet(StatZero));
        service.SampleOnce();
        var snap = service.GetSnapshot();
        Assert.Null(snap.Cpu.Percent);
        Assert.Equal(950000L, snap.MemoryAvailableKiB);
        Assert.Equal(2480008L, snap.MemoryTotalKiB);
        Assert.Equal(1958.4, snap.HostUptimeSeconds!.Value, 1);
        Assert.Equal(1.24, snap.Cpu.Load1!.Value, 3);
        Assert.Equal("CPU-therm", snap.ThermalZones[0].Type);
        Assert.Equal(57.5, snap.ThermalZones[0].Celsius, 1);
        Assert.NotNull(snap.Process);
    }

    [Fact]
    public void ApresDeuxEchantillons_CpuPourcentDeltaExact()
    {
        var reader = FakeReader.Complet(StatZero);
        var service = CreerService(reader);
        service.SampleOnce();
        reader.Fichiers["/proc/stat"] = StatUn;
        service.SampleOnce();

        // busy avant = 7300-3000-100 = 4200 ; après = 9300-3400-100 = 5800 ;
        // deltaBusy = 1600 / deltaTotal = 2000 => 80.0 %
        Assert.Equal(80.0, service.GetSnapshot().Cpu.Percent!.Value, 1);
    }

    [Fact]
    public void Sessions_299Secondes_Active()
    {
        var maintenant = DateTime.UtcNow;
        var sessions = new[] { Session(maintenant.AddSeconds(-299), false) };
        var service = CreerService(FakeReader.Vide(), sessions);
        service.SampleOnce();
        Assert.Equal(1, service.GetSnapshot().Sessions.Active);
        Assert.Equal(0, service.GetSnapshot().Sessions.Transcoding);
    }

    [Fact]
    public void Sessions_301Secondes_Inactive()
    {
        var maintenant = DateTime.UtcNow;
        var sessions = new[] { Session(maintenant.AddSeconds(-301), false) };
        var service = CreerService(FakeReader.Vide(), sessions);
        service.SampleOnce();
        Assert.Equal(0, service.GetSnapshot().Sessions.Active);
    }

    [Fact]
    public void Sessions_TranscodingInfoNonNull_CompteTranscodage()
    {
        var maintenant = DateTime.UtcNow;
        var sessions = new[]
        {
            Session(maintenant.AddSeconds(-10), true),
            Session(maintenant.AddSeconds(-10), false),
            Session(maintenant.AddSeconds(-400), true) // inactive : non comptée
        };
        var service = CreerService(FakeReader.Vide(), sessions);
        service.SampleOnce();
        Assert.Equal(2, service.GetSnapshot().Sessions.Active);
        Assert.Equal(1, service.GetSnapshot().Sessions.Transcoding);
    }

    [Fact]
    public void FichierIllisible_ChampsNullUptimeRepliSansException()
    {
        var service = CreerService(FakeReader.Vide());
        service.SampleOnce();
        var snap = service.GetSnapshot();
        Assert.Null(snap.MemoryTotalKiB);
        Assert.Null(snap.Cpu.Load1);
        // Repli uptime : TickCount64 remplace /proc/uptime interdit.
        Assert.NotNull(snap.HostUptimeSeconds);
        var attendu = Environment.TickCount64 / 1000.0;
        Assert.InRange(snap.HostUptimeSeconds!.Value, attendu - 2, attendu + 2);
    }

    [Fact]
    public void CpuProcessPremierEchantillon_Null()
    {
        var reader = FakeReader.Vide();
        reader.Fichiers["/proc/self/stat"] = "1369 (dotnet) S 1 2 3 4 5 6 7 8 9 0 40 0 0 0 20 0 1 0 666 1 2\n";
        var service = CreerService(reader);
        service.SampleOnce();
        var snap = service.GetSnapshot();
        Assert.Null(snap.Cpu.Percent);
        Assert.Null(snap.Process.CpuPercent);
    }

    [Fact]
    public void CpuProcessRepli_JiffiesStables_PourcentZero()
    {
        var contenu = "1369 (dotnet) S 1 2 3 4 5 6 7 8 9 0 40 0 0 0 20 0 1 0 666 1 2\n";
        var reader = FakeReader.Vide();
        reader.Fichiers["/proc/self/stat"] = contenu;
        var service = CreerService(reader);
        service.SampleOnce();
        service.SampleOnce();
        var snap = service.GetSnapshot();
        Assert.Null(snap.Cpu.Percent);
        Assert.Equal(0.0, snap.Process.CpuPercent!.Value, 6);
    }

    [Fact]
    public void RepliCpuProcess_JiffiesRegressent_ClampZero()
    {
        var reader = FakeReader.Vide();
        reader.Fichiers["/proc/self/stat"] = "1369 (dotnet) S 1 2 3 4 5 6 7 8 9 0 40 0 0 0 20 0 1 0 666 1 2\n";
        var service = CreerService(reader);
        service.SampleOnce();
        reader.Fichiers["/proc/self/stat"] = "1369 (dotnet) S 1 2 3 4 5 6 7 8 9 0 4 0 0 0 20 0 1 0 666 1 2\n";
        service.SampleOnce();
        Assert.Equal(0.0, service.GetSnapshot().Process.CpuPercent!.Value, 6);
    }

    [Fact]
    public void StatSelfMalforme_CpuProcessNullSansException()
    {
        var reader = FakeReader.Vide();
        reader.Fichiers["/proc/self/stat"] = "1369 (dotnet) S 1\n";
        var service = CreerService(reader);
        service.SampleOnce();
        service.SampleOnce();
        Assert.Null(service.GetSnapshot().Process.CpuPercent);
    }

    [Fact]
    public void ZoneThermaleSansFichiers_IgnoreeSansException()
    {
        var reader = FakeReader.Vide();
        reader.Zones.Add("/sys/class/thermal/thermal_zone7");
        var service = CreerService(reader);
        service.SampleOnce();
        Assert.Empty(service.GetSnapshot().ThermalZones);
    }

    [Fact]
    public void FenetreSessions_ConstanteEst300()
        => Assert.Equal(300, HostMetricsService.ActiveSessionWindowSeconds);

    [Fact]
    public void WarnUnique_ApresTroisEchantillonnages_ToutesSourcesPerdues_CinqWarnings()
    {
        // 5 sources fixe : /proc/stat, /proc/meminfo, /proc/uptime, /proc/loadavg, /proc/self/stat (thermal sans zone)
        var logger = new Mock<ILogger<HostMetricsService>>();
        var service = new HostMetricsService(logger.Object, new Mock<ISessionManager>().Object, FakeReader.Vide());
        service.SampleOnce();
        service.SampleOnce();
        service.SampleOnce();
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(5));
    }

    private static HostMetricsService CreerService(
        FakeReader reader, IReadOnlyList<SessionInfo>? sessions = null)
    {
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.Sessions).Returns(sessions ?? []);
        return new HostMetricsService(
            Mock.Of<ILogger<HostMetricsService>>(),
            sessionManager.Object,
            reader);
    }

    private static SessionInfo Session(DateTime lastActivity, bool transcoding)
        => new(Mock.Of<ISessionManager>(), Mock.Of<ILogger<SessionInfo>>())
        {
            LastActivityDate = lastActivity,
            TranscodingInfo = transcoding ? new TranscodingInfo() : null
        };

    private sealed class FakeReader : IHostMetricsReader
    {
        public Dictionary<string, string> Fichiers { get; } = new();

        public List<string> Zones { get; } = new();

        public string? ReadAllText(string path) => Fichiers.TryGetValue(path, out var contenu) ? contenu : null;

        public IReadOnlyList<string> ListThermalZones() => Zones;

        public static FakeReader Vide() => new();

        public static FakeReader Complet(string stat)
        {
            var reader = new FakeReader
            {
                Fichiers =
                {
                    ["/proc/stat"] = stat,
                    ["/proc/meminfo"] = "MemTotal:       2480008 kB\nMemAvailable:    950000 kB\nSwapTotal:       524200 kB\nSwapFree:        410000 kB\n",
                    ["/proc/uptime"] = "1958.40 1.00\n",
                    ["/proc/loadavg"] = "1.24 0.92 3/426 2214\n",
                    ["/sys/class/thermal/thermal_zone0/type"] = "CPU-therm",
                    ["/sys/class/thermal/thermal_zone0/temp"] = "57453\n"
                }
            };
            reader.Zones.Add("/sys/class/thermal/thermal_zone0");
            return reader;
        }
    }
}
