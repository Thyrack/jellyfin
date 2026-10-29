using System;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Models.SystemInfoDtos;
using Jellyfin.Api.Results;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Metrics;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class SystemControllerTests
{
    private static SystemController BuildController(
        IHostMetricsService? hostMetrics = null,
        IFileSystem? fileSystem = null)
    {
        return new SystemController(
            Mock.Of<ILogger<SystemController>>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IServerApplicationPaths>(),
            fileSystem ?? new Mock<IFileSystem>().Object,
            Mock.Of<INetworkManager>(),
            Mock.Of<ISystemManager>(),
            hostMetrics ?? Mock.Of<IHostMetricsService>());
    }

    private static HostMetricsInfo SnapshotComplet()
    {
        return new HostMetricsInfo
        {
            Timestamp = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero),
            Cpu = new CpuHostMetricsInfo { Percent = 34.5, CoreCount = 8, Load1 = 1.2, Load5 = 0.9 },
            MemoryTotalKiB = 2480008,
            MemoryAvailableKiB = 950000,
            SwapTotalKiB = 524200,
            SwapFreeKiB = 410000,
            Process = new ProcessHostMetricsInfo { WorkingSetBytes = 512000000L, HeapBytes = 210000000L, CpuPercent = 12.3, UptimeSeconds = 885.5 },
            HostUptimeSeconds = 1958.2,
            ThermalZones = [new ThermalZoneInfo { Zone = "thermal_zone0", Type = "CPU-therm", Celsius = 57.4 }],
            Sessions = new HostSessionMetricsInfo { Active = 2, Transcoding = 1 }
        };
    }

    private static HostMetricsInfo SnapshotDegenerate()
    {
        return new HostMetricsInfo
        {
            Timestamp = new DateTimeOffset(2026, 10, 30, 6, 0, 0, TimeSpan.Zero),
            Cpu = new CpuHostMetricsInfo { Percent = null, CoreCount = 4, Load1 = null, Load5 = null },
            MemoryTotalKiB = null,
            MemoryAvailableKiB = null,
            SwapTotalKiB = null,
            SwapFreeKiB = null,
            Process = new ProcessHostMetricsInfo { WorkingSetBytes = null, HeapBytes = null, CpuPercent = null, UptimeSeconds = null },
            HostUptimeSeconds = null,
            ThermalZones = [],
            Sessions = new HostSessionMetricsInfo { Active = 0, Transcoding = 0 }
        };
    }

    private static HostMetricsDto ExtraireDtoDuResultat(ActionResult<HostMetricsDto> result)
    {
        var ok = Assert.IsType<OkResult<HostMetricsDto>>(result.Result);
        return Assert.IsType<HostMetricsDto>(ok.Value);
    }

    [Fact]
    public void GetLogFile_FileDoesNotExist_ReturnsNotFound()
    {
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem
            .Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns([new() { Name = "file1.txt" }, new() { Name = "file2.txt" }]);

        var controller = BuildController(fileSystem: mockFileSystem.Object);

        var result = controller.GetLogFile("DOES_NOT_EXIST.txt");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetHostMetrics_ServiceRetourneUnSnapshot_MappingDtoFidele()
    {
        var snapshot = SnapshotComplet();
        var service = new Mock<IHostMetricsService>();
        service.Setup(s => s.GetSnapshot()).Returns(snapshot);

        var result = BuildController(service.Object).GetHostMetrics();
        var dto = ExtraireDtoDuResultat(result);

        Assert.Equal(snapshot.Timestamp, dto.Timestamp);
        Assert.Equal(34.5, dto.Cpu.Percent);
        Assert.Equal(8, dto.Cpu.CoreCount);
        Assert.Equal(1.2, dto.Cpu.Load1);
        Assert.Equal(0.9, dto.Cpu.Load5);
        Assert.Equal(2480008L, dto.MemoryTotalKiB);
        Assert.Equal(950000L, dto.MemoryAvailableKiB);
        Assert.Equal(524200L, dto.SwapTotalKiB);
        Assert.Equal(410000L, dto.SwapFreeKiB);
        Assert.Equal(512000000L, dto.Process.WorkingSetBytes);
        Assert.Equal(210000000L, dto.Process.HeapBytes);
        Assert.Equal(12.3, dto.Process.CpuPercent);
        Assert.Equal(885.5, dto.Process.UptimeSeconds);
        Assert.Equal(1958.2, dto.HostUptimeSeconds);
        var zone = Assert.Single(dto.ThermalZones);
        Assert.Equal("thermal_zone0", zone.Zone);
        Assert.Equal("CPU-therm", zone.Type);
        Assert.Equal(57.4, zone.Celsius);
        Assert.Equal(2, dto.Sessions.Active);
        Assert.Equal(1, dto.Sessions.Transcoding);
    }

    [Fact]
    public void GetHostMetrics_SnapshotDegenerate_MappingNullFidele()
    {
        var snapshot = SnapshotDegenerate();
        var service = new Mock<IHostMetricsService>();
        service.Setup(s => s.GetSnapshot()).Returns(snapshot);

        var result = BuildController(service.Object).GetHostMetrics();
        var dto = ExtraireDtoDuResultat(result);

        Assert.Equal(snapshot.Timestamp, dto.Timestamp);
        Assert.Null(dto.Cpu.Percent);
        Assert.Equal(4, dto.Cpu.CoreCount);
        Assert.Null(dto.Cpu.Load1);
        Assert.Null(dto.Cpu.Load5);
        Assert.Null(dto.MemoryTotalKiB);
        Assert.Null(dto.MemoryAvailableKiB);
        Assert.Null(dto.SwapTotalKiB);
        Assert.Null(dto.SwapFreeKiB);
        Assert.Null(dto.Process.WorkingSetBytes);
        Assert.Null(dto.Process.HeapBytes);
        Assert.Null(dto.Process.CpuPercent);
        Assert.Null(dto.Process.UptimeSeconds);
        Assert.Null(dto.HostUptimeSeconds);
        Assert.Empty(dto.ThermalZones);
        Assert.Equal(0, dto.Sessions.Active);
        Assert.Equal(0, dto.Sessions.Transcoding);
    }
}
