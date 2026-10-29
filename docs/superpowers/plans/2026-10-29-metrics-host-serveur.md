# Métriques host serveur — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exposer `GET /System/HostMetrics` (CPU %, RAM host, mémoire process, uptime, thermique, sessions) + page dashboard embarquée via plugin local, déployable sur l'APK Shield.

**Architecture:** Service noyau `HostMetricsService` (BackgroundService, échantillonnage 2 s via procfs/sysfs) exposé par `SystemController` ; plugin local `Jellyfin.Plugin.HostMetrics` affichant la page de configuration (poll 3 s) ; distribution sur Shield via asset `plugin/hostmetrics-plugin.tar.xz` + `syncPlugin` d'app-server.

**Tech Stack:** .NET 10 (C#), xunit.v3 + Moq, StyleCop obligatoire, Kotlin/Gradle pour app-server, bash pour le packaging.

**Spec de référence:** `docs/superpowers/specs/2026-10-29-metrics-host-serveur-design.md`

## Global Constraints

- Endpoint : `GET /System/HostMetrics`, policy `Policies.RequiresElevation` (identique à `Info/Storage`, `Jellyfin.Api/Controllers/SystemController.cs:81`).
- Échantillonnage service : 2 s (`SamplingIntervalMilliseconds = 2000`) ; poll UI : 3 s ; seuil thermique UI : 85 °C ; fenêtre sessions actives : `ActiveSessionWindowSeconds = 300`.
- Champs nullables quand /proc|/sys/process indisponible ou illisible ; l'endpoint renvoie TOUJOURS 200 ; log WARN au plus une fois par source illisible.
- Pas de configuration persistante, pas d'historique (snapshot volatile unique).
- StyleCop : doc XML obligatoire sur tout membre public (fr) ; `TreatWarningsAsErrors` + `AnalysisMode` AllEnabledByDefault en Debug → zéro warning toléré.
- Cible : net10.0 (`global.json` SDK 10.0.0, rollForward latestMinor) ; version serveur 13.0.0 (`SharedVersion.cs`) ; `targetAbi` plugin = `13.0` ; GUID plugin fixe `0b2e4b3e-31ea-4f6b-9d5a-e6f66b7c0b81`.
- Documentation XML **en français** sur toute nouvelle API publique (exemption : fichiers de tests).
- Limites qualité : méthode ≤ 30 lignes, imbrication ≤ 3 niveaux, zéro code mort ; commits en français au format Conventional Commits ; un commit par intention.
- NE PAS modifier jellyfin-web (tarball upstream) ni l'endpoint `GET /System/Info/Storage`.
- Windows : hors périmètre (`IHostMetricsReader` renvoie null au lieu de lever).

---

### Task 1: Modèle + DTO + endpoint `GET /System/HostMetrics`

**Files:**
- Create: `MediaBrowser.Model/System/HostMetricsInfo.cs`
- Create: `MediaBrowser.Model/System/CpuHostMetricsInfo.cs`
- Create: `MediaBrowser.Model/System/ProcessHostMetricsInfo.cs`
- Create: `MediaBrowser.Model/System/HostSessionMetricsInfo.cs`
- Create: `MediaBrowser.Model/System/ThermalZoneInfo.cs`
- Create: `MediaBrowser.Controller/Metrics/IHostMetricsService.cs`
- Modify: `MediaBrowser.Controller/ISystemManager.cs`
- Modify: `Emby.Server.Implementations/SystemManager.cs`
- Modify: `Jellyfin.Api/Controllers/SystemController.cs`
- Create: `Jellyfin.Api/Models/SystemInfoDtos/HostMetricsDto.cs`
- Create: `Jellyfin.Api/Models/SystemInfoDtos/HostCpuDto.cs`
- Create: `Jellyfin.Api/Models/SystemInfoDtos/HostProcessDto.cs`
- Create: `Jellyfin.Api/Models/SystemInfoDtos/HostSessionsDto.cs`
- Create: `Jellyfin.Api/Models/SystemInfoDtos/ThermalZoneDto.cs`
- Modify: `tests/Jellyfin.Api.Tests/Controllers/SystemControllerTests.cs`

**Interfaces:**
- Consumes: rien (première brique).
- Produces:
  - `MediaBrowser.Controller.Metrics.IHostMetricsService` avec `HostMetricsInfo GetSnapshot()`.
  - `HostMetricsInfo` (ns `MediaBrowser.Model.System`) : `Timestamp: DateTimeOffset`, `Cpu: CpuHostMetricsInfo` (`Percent: double?`, `CoreCount: int`, `Load1/Load5: double?`), `MemoryTotalKiB/MemoryAvailableKiB/SwapTotalKiB/SwapFreeKiB: long?`, `Process: ProcessHostMetricsInfo` (`WorkingSetBytes/HeapBytes: long?`, `UptimeSeconds: double?`), `HostUptimeSeconds: double?`, `ThermalZones: IReadOnlyList<ThermalZoneInfo>` (`Zone: string`, `Type: string?`, `Celsius: double`), `Sessions: HostSessionMetricsInfo` (`Active: int`, `Transcoding: int`). Records `required` + `{ get; init; }`.
  - `ISystemManager.GetHostMetrics(): HostMetricsInfo`.
  - `HostMetricsDto.FromHostMetricsInfo(HostMetricsInfo): HostMetricsDto` + mappers `FromCpuHostMetricsInfo`, `FromProcessHostMetricsInfo`, `FromHostSessionMetricsInfo`, `FromThermalZoneInfo` (internes, tiennent les mêmes noms de champs).
  - `SystemController` : nouveau param `IHostMetricsService hostMetricsService` en DERNIER paramètre du ctor (+ doc XML).

- [ ] **Step 1: Écrire le test qui échoue**

Remplacer le contenu de `tests/Jellyfin.Api.Tests/Controllers/SystemControllerTests.cs` par :

```csharp
using System;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Models.SystemInfoDtos;
using Jellyfin.Server.Implementations.SystemBackupService;
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
    private static SystemController BuildController(IHostMetricsService? hostMetrics = null)
    {
        return new SystemController(
            Mock.Of<ILogger<SystemController>>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IServerApplicationPaths>(),
            new Mock<IFileSystem>().Object,
            Mock.Of<INetworkManager>(),
            Mock.Of<ISystemManager>(),
            hostMetrics ?? Mock.Of<IHostMetricsService>());
    }

    [Fact]
    public void GetLogFile_FileDoesNotExist_ReturnsNotFound()
    {
        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem
            .Setup(fs => fs.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns([new() { Name = "file1.txt" }, new() { Name = "file2.txt" }]);

        var controller = BuildController();

        var result = controller.GetLogFile("DOES_NOT_EXIST.txt");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetHostMetrics_ServiceRetourneUnSnapshot_MappingDtoFidele()
    {
        var snapshot = new HostMetricsInfo
        {
            Timestamp = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero),
            Cpu = new CpuHostMetricsInfo { Percent = 34.5, CoreCount = 8, Load1 = 1.2, Load5 = 0.9 },
            MemoryTotalKiB = 2480008,
            MemoryAvailableKiB = 950000,
            SwapTotalKiB = 524200,
            SwapFreeKiB = 410000,
            Process = new ProcessHostMetricsInfo { WorkingSetBytes = 512000000L, HeapBytes = 210000000L, UptimeSeconds = 885.5 },
            HostUptimeSeconds = 1958.2,
            ThermalZones = [new ThermalZoneInfo { Zone = "thermal_zone0", Type = "CPU-therm", Celsius = 57.4 }],
            Sessions = new HostSessionMetricsInfo { Active = 2, Transcoding = 1 }
        };
        var service = new Mock<IHostMetricsService>();
        service.Setup(s => s.GetSnapshot()).Returns(snapshot);

        var result = BuildController(service.Object).GetHostMetrics();

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<HostMetricsDto>(ok.Value);
        Assert.Equal(34.5, dto.Cpu.Percent);
        Assert.Equal(8, dto.Cpu.CoreCount);
        Assert.Equal(2480008L, dto.MemoryTotalKiB);
        Assert.Equal(210000000L, dto.Process.HeapBytes);
        Assert.Equal(1958.2, dto.HostUptimeSeconds);
        Assert.Equal("CPU-therm", dto.ThermalZones[0].Type);
        Assert.Equal(1, dto.Sessions.Transcoding);
    }
}
```

- [ ] **Step 2: Vérifier que le test échoue**

Run: `dotnet build tests/Jellyfin.Api.Tests --nologo`
Expected: FAIL — types `HostMetricsInfo`, `IHostMetricsService` et `HostMetricsDto` inexistants.

- [ ] **Step 3: Modèle noyau — 5 fichiers MediaBrowser.Model/System**

`MediaBrowser.Model/System/HostMetricsInfo.cs` :

```csharp
using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.System;

/// <summary>
/// Instantané des métriques du host exposé par le serveur.
/// </summary>
public record HostMetricsInfo
{
    /// <summary>
    /// Obtient la date et l'heure UTC du snapshot.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Obtient la métrique CPU globale du host.
    /// </summary>
    public required CpuHostMetricsInfo Cpu { get; init; }

    /// <summary>
    /// Obtient la mémoire totale du host en KiB.
    /// </summary>
    public required long? MemoryTotalKiB { get; init; }

    /// <summary>
    /// Obtient la mémoire disponible du host en KiB.
    /// </summary>
    public required long? MemoryAvailableKiB { get; init; }

    /// <summary>
    /// Obtient la swap totale du host en KiB.
    /// </summary>
    public required long? SwapTotalKiB { get; init; }

    /// <summary>
    /// Obtient la swap libre du host en KiB.
    /// </summary>
    public required long? SwapFreeKiB { get; init; }

    /// <summary>
    /// Obtient la métrique mémoire du processus serveur.
    /// </summary>
    public required ProcessHostMetricsInfo Process { get; init; }

    /// <summary>
    /// Obtient la durée de vie du host en secondes.
    /// </summary>
    public required double? HostUptimeSeconds { get; init; }

    /// <summary>
    /// Obtient les zones thermiques du host (liste vide si aucune source).
    /// </summary>
    public required IReadOnlyList<ThermalZoneInfo> ThermalZones { get; init; }

    /// <summary>
    /// Obtient les compteurs de sessions.
    /// </summary>
    public required HostSessionMetricsInfo Sessions { get; init; }
}
```

`MediaBrowser.Model/System/CpuHostMetricsInfo.cs` :

```csharp
namespace MediaBrowser.Model.System;

/// <summary>
/// Métrique CPU globale du host.
/// </summary>
public record CpuHostMetricsInfo
{
    /// <summary>
    /// Obtient le pourcentage d'occupation CPU global (null tant que 2 échantillons ne sont pas accumulés).
    /// </summary>
    public required double? Percent { get; init; }

    /// <summary>
    /// Obtient le nombre de cœurs logiques du host.
    /// </summary>
    public required int CoreCount { get; init; }

    /// <summary>
    /// Obtient la charge moyenne sur 1 minute.
    /// </summary>
    public required double? Load1 { get; init; }

    /// <summary>
    /// Obtient la charge moyenne sur 5 minutes.
    /// </summary>
    public required double? Load5 { get; init; }
}
```

`MediaBrowser.Model/System/ProcessHostMetricsInfo.cs` :

```csharp
namespace MediaBrowser.Model.System;

/// <summary>
/// Métrique mémoire du processus serveur dotnet.
/// </summary>
public record ProcessHostMetricsInfo
{
    /// <summary>
    /// Obtient le working set du processus en octets.
    /// </summary>
    public required long? WorkingSetBytes { get; init; }

    /// <summary>
    /// Obtient la taille du tas du garbage collector en octets.
    /// </summary>
    public required long? HeapBytes { get; init; }

    /// <summary>
    /// Obtient la durée de vie du processus serveur en secondes.
    /// </summary>
    public required double? UptimeSeconds { get; init; }
}
```

`MediaBrowser.Model/System/HostSessionMetricsInfo.cs` :

```csharp
namespace MediaBrowser.Model.System;

/// <summary>
/// Compteurs de sessions sur le serveur.
/// </summary>
public record HostSessionMetricsInfo
{
    /// <summary>
    /// Obtient le nombre de sessions actives (activité de moins de 300 s).
    /// </summary>
    public required int Active { get; init; }

    /// <summary>
    /// Obtient le nombre de sessions en transcodage.
    /// </summary>
    public required int Transcoding { get; init; }
}
```

`MediaBrowser.Model/System/ThermalZoneInfo.cs` :

```csharp
namespace MediaBrowser.Model.System;

/// <summary>
/// Zone thermique du host (soc, cpu, gpu…).
/// </summary>
public record ThermalZoneInfo
{
    /// <summary>
    /// Obtient l'identifiant noyau de la zone (ex. thermal_zone0).
    /// </summary>
    public required string Zone { get; init; }

    /// <summary>
    /// Obtient le type déclaré par la zone (ex. CPU-therm), null si illisible.
    /// </summary>
    public required string? Type { get; init; }

    /// <summary>
    /// Obtient la température en degrés Celsius.
    /// </summary>
    public required double Celsius { get; init; }
}
```

- [ ] **Step 4: Interface métriques + délégation SystemManager**

`MediaBrowser.Controller/Metrics/IHostMetricsService.cs` :

```csharp
using MediaBrowser.Model.System;

namespace MediaBrowser.Controller.Metrics;

/// <summary>
/// Fournit l'instantané courant des métriques du host (échantillonné en tâche de fond).
/// </summary>
public interface IHostMetricsService
{
    /// <summary>
    /// Retourne le snapshot le plus récent (immuable), à coût d'une simple lecture.
    /// </summary>
    /// <returns>Instantané des métriques du host.</returns>
    HostMetricsInfo GetSnapshot();
}
```

`MediaBrowser.Controller/ISystemManager.cs` — ajouter après `GetSystemStorageInfo()` (l.40), dans les usings existants `MediaBrowser.Model.System` déjà présente :

```csharp
    /// <summary>
    /// Gets the current host metrics snapshot.
    /// </summary>
    /// <returns><see cref="HostMetricsInfo"/> du host.</returns>
    HostMetricsInfo GetHostMetrics();
```

`Emby.Server.Implementations/SystemManager.cs` :
- ctor : ajouter en dernier paramètre `IHostMetricsService hostMetricsService` + champ `_hostMetricsService` (doc param : `<param name="hostMetricsService">Instance of <see cref="IHostMetricsService"/> interface.</param>` ; usings : ajouter `using MediaBrowser.Controller.Metrics;`).
- après `GetSystemStorageInfo()` (l.109) :

```csharp
    /// <inheritdoc />
    public HostMetricsInfo GetHostMetrics()
        => _hostMetricsService.GetSnapshot();
```

- [ ] **Step 5: DTOs API — 5 fichiers Jellyfin.Api/Models/SystemInfoDtos**

`Jellyfin.Api/Models/SystemInfoDtos/HostMetricsDto.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO des métriques du host (CPU, RAM, process, thermique, sessions).
/// </summary>
public record HostMetricsDto
{
    /// <summary>
    /// Obtient l'horodatage UTC du snapshot.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Obtient le CPU global du host.
    /// </summary>
    public required HostCpuDto Cpu { get; init; }

    /// <summary>
    /// Obtient la mémoire totale du host en KiB.
    /// </summary>
    public required long? MemoryTotalKiB { get; init; }

    /// <summary>
    /// Obtient la mémoire disponible du host en KiB.
    /// </summary>
    public required long? MemoryAvailableKiB { get; init; }

    /// <summary>
    /// Obtient la swap totale du host en KiB.
    /// </summary>
    public required long? SwapTotalKiB { get; init; }

    /// <summary>
    /// Obtient la swap libre du host en KiB.
    /// </summary>
    public required long? SwapFreeKiB { get; init; }

    /// <summary>
    /// Obtient la mémoire du processus serveur.
    /// </summary>
    public required HostProcessDto Process { get; init; }

    /// <summary>
    /// Obtient la durée de vie du host en secondes.
    /// </summary>
    public required double? HostUptimeSeconds { get; init; }

    /// <summary>
    /// Obtient les zones thermiques du host.
    /// </summary>
    public required IReadOnlyList<ThermalZoneDto> ThermalZones { get; init; }

    /// <summary>
    /// Obtient les compteurs de sessions.
    /// </summary>
    public required HostSessionsDto Sessions { get; init; }

    internal static HostMetricsDto FromHostMetricsInfo(HostMetricsInfo model)
    {
        return new()
        {
            Timestamp = model.Timestamp,
            Cpu = HostCpuDto.FromCpuHostMetricsInfo(model.Cpu),
            MemoryTotalKiB = model.MemoryTotalKiB,
            MemoryAvailableKiB = model.MemoryAvailableKiB,
            SwapTotalKiB = model.SwapTotalKiB,
            SwapFreeKiB = model.SwapFreeKiB,
            Process = HostProcessDto.FromProcessHostMetricsInfo(model.Process),
            HostUptimeSeconds = model.HostUptimeSeconds,
            ThermalZones = model.ThermalZones.Select(ThermalZoneDto.FromThermalZoneInfo).ToArray(),
            Sessions = HostSessionsDto.FromHostSessionMetricsInfo(model.Sessions)
        };
    }
}
```

`Jellyfin.Api/Models/SystemInfoDtos/HostCpuDto.cs` :

```csharp
using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO de la métrique CPU globale du host.
/// </summary>
public record HostCpuDto
{
    /// <summary>
    /// Obtient le pourcentage d'occupation CPU global, null si non mesurable.
    /// </summary>
    public required double? Percent { get; init; }

    /// <summary>
    /// Obtient le nombre de cœurs logiques.
    /// </summary>
    public required int CoreCount { get; init; }

    /// <summary>
    /// Obtient la charge moyenne sur 1 minute.
    /// </summary>
    public required double? Load1 { get; init; }

    /// <summary>
    /// Obtient la charge moyenne sur 5 minutes.
    /// </summary>
    public required double? Load5 { get; init; }

    internal static HostCpuDto FromCpuHostMetricsInfo(CpuHostMetricsInfo model)
    {
        return new()
        {
            Percent = model.Percent,
            CoreCount = model.CoreCount,
            Load1 = model.Load1,
            Load5 = model.Load5
        };
    }
}
```

`Jellyfin.Api/Models/SystemInfoDtos/HostProcessDto.cs` :

```csharp
using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO de la mémoire du processus serveur.
/// </summary>
public record HostProcessDto
{
    /// <summary>
    /// Obtient le working set du processus en octets.
    /// </summary>
    public required long? WorkingSetBytes { get; init; }

    /// <summary>
    /// Obtient le tas GC en octets.
    /// </summary>
    public required long? HeapBytes { get; init; }

    /// <summary>
    /// Obtient l'uptime du processus en secondes.
    /// </summary>
    public required double? UptimeSeconds { get; init; }

    internal static HostProcessDto FromProcessHostMetricsInfo(ProcessHostMetricsInfo model)
    {
        return new()
        {
            WorkingSetBytes = model.WorkingSetBytes,
            HeapBytes = model.HeapBytes,
            UptimeSeconds = model.UptimeSeconds
        };
    }
}
```

`Jellyfin.Api/Models/SystemInfoDtos/HostSessionsDto.cs` :

```csharp
using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO des compteurs de sessions.
/// </summary>
public record HostSessionsDto
{
    /// <summary>
    /// Obtient le nombre de sessions actives (moins de 300 s).
    /// </summary>
    public required int Active { get; init; }

    /// <summary>
    /// Obtient le nombre de sessions en transcodage.
    /// </summary>
    public required int Transcoding { get; init; }

    internal static HostSessionsDto FromHostSessionMetricsInfo(HostSessionMetricsInfo model)
    {
        return new()
        {
            Active = model.Active,
            Transcoding = model.Transcoding
        };
    }
}
```

`Jellyfin.Api/Models/SystemInfoDtos/ThermalZoneDto.cs` :

```csharp
using MediaBrowser.Model.System;

namespace Jellyfin.Api.Models.SystemInfoDtos;

/// <summary>
/// DTO d'une zone thermique du host.
/// </summary>
public record ThermalZoneDto
{
    /// <summary>
    /// Obtient l'identifiant noyau de la zone.
    /// </summary>
    public required string Zone { get; init; }

    /// <summary>
    /// Obtient le type déclaré par la zone, null si illisible.
    /// </summary>
    public required string? Type { get; init; }

    /// <summary>
    /// Obtient la température en degrés Celsius.
    /// </summary>
    public required double Celsius { get; init; }

    internal static ThermalZoneDto FromThermalZoneInfo(ThermalZoneInfo model)
    {
        return new()
        {
            Zone = model.Zone,
            Type = model.Type,
            Celsius = model.Celsius
        };
    }
}
```

- [ ] **Step 6: Endpoint contrôleur**

`Jellyfin.Api/Controllers/SystemController.cs` :
- usings : ajouter `using Jellyfin.Api.Models.SystemInfoDtos;` et `using MediaBrowser.Controller.Metrics;`.
- ctor : ajouter en dernier paramètre `IHostMetricsService hostMetricsService` + champ `_hostMetricsService` (doc : `<param name="hostMetricsService">Instance of <see cref="IHostMetricsService"/> interface.</param>`).
- après l'endpoint `GetSystemStorage` (après l.86) :

```csharp
    /// <summary>
    /// Gets the host metrics (CPU, memory, process, thermal, sessions).
    /// </summary>
    /// <response code="200">Metrics snapshot retrieved.</response>
    /// <response code="403">User does not have permission to retrieve information.</response>
    /// <returns>A <see cref="HostMetricsDto"/> with the current host metrics snapshot.</returns>
    [HttpGet("HostMetrics")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<HostMetricsDto> GetHostMetrics()
        => Ok(HostMetricsDto.FromHostMetricsInfo(_hostMetricsService.GetSnapshot()));
```

(Router class-level `[Route("[controller]")]` : l'URL résultante est bien `/System/HostMetrics`.)

- [ ] **Step 7: NE PAS toucher `Startup.cs` dans cette tâche**

L'enregistrement DI (avec `HostMetricsService`, qui n'existe qu'à partir de la Task 3) est fait en Task 3. La compilation du contrôleur n'en dépend pas.

- [ ] **Step 8: Vérifier que le test passe**

Run: `dotnet test tests/Jellyfin.Api.Tests --filter "FullyQualifiedName~SystemControllerTests" --nologo`
Expected: PASS (2 tests).

Run: `dotnet build Jellyfin.sln --nologo`
Expected: PASS sans warning.

- [ ] **Step 9: Commit**

```bash
git add MediaBrowser.Model/System/HostMetricsInfo.cs MediaBrowser.Model/System/CpuHostMetricsInfo.cs MediaBrowser.Model/System/ProcessHostMetricsInfo.cs MediaBrowser.Model/System/HostSessionMetricsInfo.cs MediaBrowser.Model/System/ThermalZoneInfo.cs MediaBrowser.Controller/Metrics/IHostMetricsService.cs MediaBrowser.Controller/ISystemManager.cs Emby.Server.Implementations/SystemManager.cs Jellyfin.Api/Controllers/SystemController.cs Jellyfin.Api/Models/SystemInfoDtos tests/Jellyfin.Api.Tests/Controllers/SystemControllerTests.cs
git commit -m "feat(metrics): expose l'endpoint System/HostMetrics avec modèle et DTO"
```

---

### Task 2: Parsers procfs purs (CPU, RAM, uptime, loadavg, thermique)

**Files:**
- Create: `Emby.Server.Implementations/Metrics/CpuSample.cs`
- Create: `Emby.Server.Implementations/Metrics/ProcStatParser.cs`
- Create: `Emby.Server.Implementations/Metrics/CpuMetricsCalculator.cs`
- Create: `Emby.Server.Implementations/Metrics/ProcMemInfoParser.cs`
- Create: `Emby.Server.Implementations/Metrics/ProcUptimeParser.cs`
- Create: `Emby.Server.Implementations/Metrics/ProcLoadAvgParser.cs`
- Create: `Emby.Server.Implementations/Metrics/ThermalZoneParser.cs`
- Test: `tests/Jellyfin.Server.Implementations.Tests/Metrics/ProcfsParsersTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces (ns `Emby.Server.Implementations.Metrics`, tous publics, fonctions statiques pures) :
  - `sealed record CpuSample(long Total, long Idle, long Iowait)`.
  - `ProcStatParser.ParseSingleCpuLine(string content) : CpuSample?` — ligne `cpu ` agrégée de `/proc/stat` (les `cpu0..N` sont ignorés) ; total = somme des 8 premiers compteurs (user..steal, guest EXCLU côté addition) ; null si ligne absente ou < 5 colonnes.
  - `CpuMetricsCalculator.ComputePercent(CpuSample? previous, CpuSample current) : double?` — null si `previous` null ; 0.0 si delta total ≤ 0 (inclut les compteurs qui reculent) ; clamp [0, 100].
  - `ProcMemInfoParser.Parse(string content) : (long? Total, long? Available, long? SwapTotal, long? SwapFree)` — MemTotal/MemAvailable/SwapTotal/SwapFree en KiB.
  - `ProcUptimeParser.ParseUptimeSeconds(string content) : double?` — premier champ de `/proc/uptime`.
  - `ProcLoadAvgParser.Parse(string content) : (double? Load1, double? Load5)` — 2 premiers champs, parse InvariantCulture (« . »).
  - `ThermalZoneParser.ParseTempCelsius(string content) : double?` — millidegrés → °C arrondi 1 décimale ; null si non numérique.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/Jellyfin.Server.Implementations.Tests/Metrics/ProcfsParsersTests.cs` :

```csharp
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
        Assert.Equal(18.0, CpuMetricsCalculator.ComputePercent(avant, apres), 6);
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
        => Assert.Equal(attendu, ProcUptimeParser.ParseUptimeSeconds(contenu), 3);

    [Fact]
    public void ParseUptimeSeconds_ContenuVide_RetourNull()
        => Assert.Null(ProcUptimeParser.ParseUptimeSeconds(string.Empty));

    [Theory]
    [InlineData("1.24 0.92 3/426 2214\n", 1.24, 0.92)]
    [InlineData("0.00 0.00 0/100 42\n", 0.0, 0.0)]
    public void ParseLoadAvg_ValeursStandard(string contenu, double l1, double l5)
    {
        var (load1, load5) = ProcLoadAvgParser.Parse(contenu);
        Assert.Equal(l1, load1, 3);
        Assert.Equal(l5, load5, 3);
    }

    [Fact]
    public void ParseLoadAvg_ColonnesInsuffisantes_ToutNull()
    {
        var (load1, load5) = ProcLoadAvgParser.Parse("1.24\n");
        Assert.Equal(1.24, load1, 3);
        Assert.Null(load5);
    }

    [Theory]
    [InlineData("57453\n", 57.5)]
    [InlineData("0\n", 0.0)]
    [InlineData("-40000\n", -40.0)]
    public void ParseTempCelsius_Millidegre(string contenu, double attendu)
        => Assert.Equal(attendu, ThermalZoneParser.ParseTempCelsius(contenu), 1);

    [Fact]
    public void ParseTempCelsius_ContenuNonNumerique_RetourNull()
        => Assert.Null(ThermalZoneParser.ParseTempCelsius("not-a-number\n"));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet build tests/Jellyfin.Server.Implementations.Tests --nologo`
Expected: FAIL — types du ns `Emby.Server.Implementations.Metrics` inexistants.

- [ ] **Step 3: Implémentation des parsers**

`Emby.Server.Implementations/Metrics/CpuSample.cs` :

```csharp
namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Compteurs CPU agrégés extraits de /proc/stat (total cumulé, idle, iowait).
/// </summary>
/// <param name="Total">Somme des compteurs user..steal (guest exclu : déjà compris dans user côté noyau).</param>
/// <param name="Idle">Compteur idle.</param>
/// <param name="Iowait">Compteur iowait.</param>
public sealed record CpuSample(long Total, long Idle, long Iowait);
```

`Emby.Server.Implementations/Metrics/ProcStatParser.cs` :

```csharp
using System;
using System.Linq;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse la ligne CPU agrégée de /proc/stat.
/// </summary>
public static class ProcStatParser
{
    /// <summary>
    /// Nombre minimal de colonnes attendues : user, nice, system, idle, iowait.
    /// </summary>
    private const int MinColonnes = 5;

    /// <summary>
    /// Extrait la ligne « cpu » globale d'un contenu de /proc/stat.
    /// </summary>
    /// <param name="content">Contenu intégral du fichier.</param>
    /// <returns>Échantillon agrégé, ou null si absent ou illisible.</returns>
    public static CpuSample? ParseSingleCpuLine(string content)
    {
        var ligne = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (ligne is null)
        {
            return null;
        }

        var mesures = ligne.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(t => long.TryParse(t, out var valeur) ? valeur : (long?)null)
            .TakeWhile(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();
        if (mesures.Length < MinColonnes)
        {
            return null;
        }

        // user..steal = 8 compteurs fixes ; guest/guest_nice (2 dernières colonnes,
        // présentes seulement si Length > 8) sont exclus car déjà inclus dans user par le noyau.
        long total = 0;
        var limite = Math.Min(mesures.Length, 8);
        for (var i = 0; i < limite; i++)
        {
            total += mesures[i];
        }

        return new CpuSample(total, mesures[3], mesures[4]);
    }
}
```

`Emby.Server.Implementations/Metrics/CpuMetricsCalculator.cs` :

```csharp
using System;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Calcul du pourcentage CPU entre deux échantillons /proc/stat.
/// </summary>
public static class CpuMetricsCalculator
{
    /// <summary>
    /// Calcule le pourcentage d'occupation entre deux échantillons (deltas des compteurs cumulés).
    /// </summary>
    /// <param name="previous">Échantillon précédent (null tant qu'aucun échantillon précédent n'est accumulé).</param>
    /// <param name="current">Échantillon courant.</param>
    /// <returns>Pourcentage dans [0, 100] ; 0.0 si delta total ≤ 0 ; null si précédent absent.</returns>
    public static double? ComputePercent(CpuSample? previous, CpuSample current)
    {
        if (previous is null)
        {
            return null;
        }

        var deltaTotal = current.Total - previous.Total;
        if (deltaTotal <= 0)
        {
            return 0.0;
        }

        var deltaActive = Occupation(current) - Occupation(previous);
        return Math.Clamp(deltaActive * 100.0 / deltaTotal, 0.0, 100.0);
    }

    private static long Occupation(CpuSample sample)
        => sample.Total - sample.Idle - sample.Iowait;
}
```

`Emby.Server.Implementations/Metrics/ProcMemInfoParser.cs` :

```csharp
using System;
using System.Linq;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /proc/meminfo (valeurs en KiB).
/// </summary>
public static class ProcMemInfoParser
{
    /// <summary>
    /// Extrait MemTotal, MemAvailable, SwapTotal, SwapFree du contenu.
    /// </summary>
    /// <param name="content">Contenu du fichier.</param>
    /// <returns>Tuple de valeurs en KiB (null si champ absent).</returns>
    public static (long? Total, long? Available, long? SwapTotal, long? SwapFree) Parse(string content)
    {
        return (Extraire(content, "MemTotal:"), Extraire(content, "MemAvailable:"),
            Extraire(content, "SwapTotal:"), Extraire(content, "SwapFree:"));
    }

    private static long? Extraire(string content, string cle)
    {
        var ligne = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith(cle, StringComparison.Ordinal));
        if (ligne is null)
        {
            return null;
        }

        var champ = ligne.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        return long.TryParse(champ, out var valeur) ? valeur : null;
    }
}
```

`Emby.Server.Implementations/Metrics/ProcUptimeParser.cs` :

```csharp
using System;
using System.Globalization;
using System.Linq;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /proc/uptime (première colonne, secondes).
/// </summary>
public static class ProcUptimeParser
{
    /// <summary>
    /// Extrait l'uptime du host en secondes.
    /// </summary>
    /// <param name="content">Contenu du fichier.</param>
    /// <returns>Secondes, ou null si illisible.</returns>
    public static double? ParseUptimeSeconds(string content)
    {
        var champ = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(0);
        return double.TryParse(champ, NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : null;
    }
}
```

`Emby.Server.Implementations/Metrics/ProcLoadAvgParser.cs` :

```csharp
using System;
using System.Globalization;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /proc/loadavg (séparateur décimal « . », lecture en InvariantCulture).
/// </summary>
public static class ProcLoadAvgParser
{
    /// <summary>
    /// Extrait la charge moyenne 1 min et 5 min.
    /// </summary>
    /// <param name="content">Contenu du fichier.</param>
    /// <returns>Tuple (Load1, Load5) ; nulls si colonnes absentes.</returns>
    public static (double? Load1, double? Load5) Parse(string content)
    {
        var champs = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (Champ(champs, 0), Champ(champs, 1));
    }

    private static double? Champ(string[] champs, int index)
    {
        return champs.Length > index
            && double.TryParse(champs[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : null;
    }
}
```

`Emby.Server.Implementations/Metrics/ThermalZoneParser.cs` :

```csharp
using System;
using System.Globalization;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Parse /sys/class/thermal/thermal_zoneN/temp (millidegrés Celsius).
/// </summary>
public static class ThermalZoneParser
{
    /// <summary>
    /// Convertit le contenu « temp » en degrés Celsius (1 décimale).
    /// </summary>
    /// <param name="content">Contenu du fichier temp.</param>
    /// <returns>Température en °C, ou null si illisible.</returns>
    public static double? ParseTempCelsius(string content)
    {
        if (!long.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var millidegre))
        {
            return null;
        }

        return Math.Round(millidegre / 1000.0, 1);
    }
}
```

- [ ] **Step 4: Vérifier que les tests passent**

Run: `dotnet test tests/Jellyfin.Server.Implementations.Tests --filter "FullyQualifiedName~ProcfsParsersTests" --nologo`
Expected: PASS (19 tests).

- [ ] **Step 5: Commit**

```bash
git add Emby.Server.Implementations/Metrics/CpuSample.cs Emby.Server.Implementations/Metrics/ProcStatParser.cs Emby.Server.Implementations/Metrics/CpuMetricsCalculator.cs Emby.Server.Implementations/Metrics/ProcMemInfoParser.cs Emby.Server.Implementations/Metrics/ProcUptimeParser.cs Emby.Server.Implementations/Metrics/ProcLoadAvgParser.cs Emby.Server.Implementations/Metrics/ThermalZoneParser.cs tests/Jellyfin.Server.Implementations.Tests/Metrics/ProcfsParsersTests.cs
git commit -m "feat(metrics): parsers procfs purs avec fixtures de tests"
```

---

### Task 3: Service d'échantillonnage `HostMetricsService` + DI

**Files:**
- Create: `Emby.Server.Implementations/Metrics/IHostMetricsReader.cs`
- Create: `Emby.Server.Implementations/Metrics/DirectoryHostMetricsReader.cs`
- Create: `Emby.Server.Implementations/Metrics/HostMetricsService.cs`
- Modify: `Jellyfin.Server/Startup.cs` (fin du bloc `AddHostedService`, l.149-158)
- Test: `tests/Jellyfin.Server.Implementations.Tests/Metrics/HostMetricsServiceTests.cs`

**Interfaces:**
- Consumes: parsers et calcul (Task 2) ; `IHostMetricsService.GetSnapshot()` + `HostMetricsInfo` et consorts (Task 1) ; `ISessionManager.Sessions`, `SessionInfo.LastActivityDate`, `SessionInfo.TranscodingInfo` (existant).
- Produces:
  - `Emby.Server.Implementations.Metrics.IHostMetricsReader` :
    - `string? ReadAllText(string path)` — contenu intégral ou null (absent/illisible).
    - `IReadOnlyList<string> ListThermalZones()` — chemins des répertoires `/sys/class/thermal/thermal_zone*` (liste vide sinon).
  - `DirectoryHostMetricsReader : IHostMetricsReader` (implémentation procfs réelle, jamais d'exception).
  - `HostMetricsService : BackgroundService, IHostMetricsService` :
    - ctor `(ILogger<HostMetricsService> logger, ISessionManager sessionManager, IHostMetricsReader reader)`.
    - const `SamplingIntervalMilliseconds = 2000`, `ActiveSessionWindowSeconds = 300`.
    - `public void SampleOnce()` — échantillonne toutes les sources une fois (utilisé par `ExecuteAsync` et les tests).
    - `GetSnapshot()` : renvoie le snapshot courant ; avant tout échantillonnage, snapshot « vide » (champs null, ThermalZones vide, sessions 0, `Timestamp` fixé au démarrage).
    - `ExecuteAsync` : boucle `SampleOnce(); await Task.Delay(SamplingIntervalMilliseconds, stoppingToken);` (exception annulation avalée).
    - Counting `Sessions` avec le même seuil `ActiveSessionWindowSeconds` et `TranscodingInfo != null`.
  - DI dans `Jellyfin.Server/Startup.cs` (après le bloc `AddHostedService<DeviceAccessHost>` l.158) :
    - `services.AddSingleton<IHostMetricsReader, DirectoryHostMetricsReader>();`
    - `services.AddHostedService<HostMetricsService>();`
    - `services.AddSingleton<IHostMetricsService>(sp => sp.GetRequiredService<HostMetricsService>());`

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/Jellyfin.Server.Implementations.Tests/Metrics/HostMetricsServiceTests.cs` :

```csharp
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

    private sealed class FakeReader : IHostMetricsReader
    {
        public readonly Dictionary<string, string> Fichiers = new();
        public readonly List<string> Zones = new();

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
        Assert.Equal(1958.4, snap.HostUptimeSeconds, 1);
        Assert.Equal(1.24, snap.Cpu.Load1, 3);
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
        Assert.Equal(80.0, service.GetSnapshot().Cpu.Percent, 1);
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
    public void FichierIllisible_ChampsNullSansException()
    {
        var service = CreerService(FakeReader.Vide());
        service.SampleOnce();
        var snap = service.GetSnapshot();
        Assert.Null(snap.MemoryTotalKiB);
        Assert.Null(snap.HostUptimeSeconds);
        Assert.Null(snap.Cpu.Load1);
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
    public void WarnUnique_ApresTroisEchantillonnages_ToutesSourcesPerdues_QuatreWarnings()
    {
        // 4 sources fixe : /proc/stat, /proc/meminfo, /proc/uptime, /proc/loadavg (thermal sans zone)
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
            Times.Exactly(4));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet build tests/Jellyfin.Server.Implementations.Tests --nologo`
Expected: FAIL — `HostMetricsService` et `IHostMetricsReader` inexistants.

- [ ] **Step 3: Implémentation de l'abstraction du système de fichiers**

`Emby.Server.Implementations/Metrics/IHostMetricsReader.cs` :

```csharp
using System.Collections.Generic;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Abstraction de lecture du système de fichiers procfs/sysfs (injectable pour les tests).
/// </summary>
public interface IHostMetricsReader
{
    /// <summary>
    /// Lit le contenu intégral d'un fichier, ou null si absent ou illisible.
    /// </summary>
    /// <param name="path">Chemin absolu du fichier.</param>
    /// <returns>Contenu brut, ou null.</returns>
    string? ReadAllText(string path);

    /// <summary>
    /// Liste les zones thermiques disponibles sous /sys/class/thermal.
    /// </summary>
    /// <returns>Chemins des répertoires thermal_zone* (liste vide si aucune).</returns>
    IReadOnlyList<string> ListThermalZones();
}
```

`Emby.Server.Implementations/Metrics/DirectoryHostMetricsReader.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Lecteur procfs réel : toute erreur (fichier absent, SELinux, IO) renvoie null/liste vide, jamais d'exception.
/// </summary>
public sealed class DirectoryHostMetricsReader : IHostMetricsReader
{
    /// <summary>
    /// Racine noyau des zones thermiques.
    /// </summary>
    private const string RacineThermale = "/sys/class/thermal";

    /// <inheritdoc />
    public string? ReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListThermalZones()
    {
        try
        {
            return Directory.Exists(RacineThermale)
                ? Directory.GetDirectories(RacineThermale, "thermal_zone*")
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
```

- [ ] **Step 4: Implémentation du service**

`Emby.Server.Implementations/Metrics/HostMetricsService.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Metrics;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Service d'échantillonnage en tâche de fond des métriques du host (procfs/sysfs + process + sessions).
/// </summary>
public sealed class HostMetricsService : BackgroundService, IHostMetricsService
{
    /// <summary>
    /// Période d'échantillonnage en millisecondes.
    /// </summary>
    public const int SamplingIntervalMilliseconds = 2000;

    /// <summary>
    /// Fenêtre en secondes au-delà de laquelle une session n'est plus comptée active.
    /// </summary>
    public const int ActiveSessionWindowSeconds = 300;

    /// <summary>
    /// Chemin du compteur CPU agrégé du noyau.
    /// </summary>
    private const string CheminStat = "/proc/stat";

    /// <summary>
    /// Chemin du fichier mémoire du noyau.
    /// </summary>
    private const string CheminMemInfo = "/proc/meminfo";

    /// <summary>
    /// Chemin du fichier uptime du noyau.
    /// </summary>
    private const string CheminUptime = "/proc/uptime";

    /// <summary>
    /// Chemin du fichier load average du noyau.
    /// </summary>
    private const string CheminLoadAvg = "/proc/loadavg";

    private readonly ILogger<HostMetricsService> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly IHostMetricsReader _reader;
    private readonly object _lock = new();
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    private HostMetricsInfo _snapshot;
    private CpuSample? _cpuPrecedent;
    private double? _cpuPercent;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostMetricsService"/> class.
    /// </summary>
    /// <param name="logger">Instance of <see cref="ILogger{HostMetricsService}"/> interface.</param>
    /// <param name="sessionManager">Instance of <see cref="ISessionManager"/> interface.</param>
    /// <param name="reader">Instance of <see cref="IHostMetricsReader"/> interface.</param>
    public HostMetricsService(
        ILogger<HostMetricsService> logger,
        ISessionManager sessionManager,
        IHostMetricsReader reader)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _reader = reader;
        _snapshot = SnapshotVide(DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public HostMetricsInfo GetSnapshot()
    {
        lock (_lock)
        {
            return _snapshot;
        }
    }

    /// <summary>
    /// Échantillonne toutes les sources une fois (public : test unitaire + boucle ExecuteAsync).
    /// </summary>
    public void SampleOnce()
    {
        MesurerCpu();
        var nouveau = ConstruireSnapshot();
        lock (_lock)
        {
            _snapshot = nouveau;
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            SampleOnce();
            await Task.Delay(SamplingIntervalMilliseconds, stoppingToken).ConfigureAwait(false);
        }
    }

    private void MesurerCpu()
    {
        var contenu = _reader.ReadAllText(CheminStat);
        if (contenu is null)
        {
            InstruireUneFois(CheminStat);
            _cpuPercent = null;
            return;
        }

        var sample = ProcStatParser.ParseSingleCpuLine(contenu);
        if (sample is null)
        {
            InstruireUneFois(CheminStat + ":parse");
            _cpuPercent = null;
            return;
        }

        _cpuPercent = CpuMetricsCalculator.ComputePercent(_cpuPrecedent, sample);
        _logger.LogDebug("CPU host échantillonné : {Percent} %", _cpuPercent);
        _cpuPrecedent = sample;
    }

    private HostMetricsInfo ConstruireSnapshot()
    {
        var memoire = Memoire();
        var (charge1, charge5) = Charge();
        var process = ReadProcessMetrics();
        return new HostMetricsInfo
        {
            Timestamp = DateTimeOffset.UtcNow,
            Cpu = new CpuHostMetricsInfo
            {
                Percent = _cpuPercent,
                CoreCount = Environment.ProcessorCount,
                Load1 = charge1,
                Load5 = charge5
            },
            MemoryTotalKiB = memoire.Total,
            MemoryAvailableKiB = memoire.Available,
            SwapTotalKiB = memoire.SwapTotal,
            SwapFreeKiB = memoire.SwapFree,
            Process = process,
            HostUptimeSeconds = Uptime(),
            ThermalZones = ZonesThermiques(),
            Sessions = CompteSessions()
        };
    }

    private (long? Total, long? Available, long? SwapTotal, long? SwapFree) Memoire()
    {
        var contenu = _reader.ReadAllText(CheminMemInfo);
        if (contenu is null)
        {
            InstruireUneFois(CheminMemInfo);
            return (null, null, null, null);
        }

        return ProcMemInfoParser.Parse(contenu);
    }

    private (double? Load1, double? Load5) Charge()
    {
        var contenu = _reader.ReadAllText(CheminLoadAvg);
        if (contenu is null)
        {
            InstruireUneFois(CheminLoadAvg);
            return (null, null);
        }

        return ProcLoadAvgParser.Parse(contenu);
    }

    private double? Uptime()
    {
        var contenu = _reader.ReadAllText(CheminUptime);
        if (contenu is null)
        {
            InstruireUneFois(CheminUptime);
            return null;
        }

        return ProcUptimeParser.ParseUptimeSeconds(contenu);
    }

    private ProcessHostMetricsInfo ReadProcessMetrics()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var uptime = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds;
            return new ProcessHostMetricsInfo
            {
                WorkingSetBytes = process.WorkingSet64,
                HeapBytes = GC.GetGCMemoryInfo().HeapSizeBytes,
                UptimeSeconds = uptime
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Métriques du processus serveur indisponibles");
            return new ProcessHostMetricsInfo { WorkingSetBytes = null, HeapBytes = null, UptimeSeconds = null };
        }
    }

    private IReadOnlyList<ThermalZoneInfo> ZonesThermiques()
    {
        var zones = new List<ThermalZoneInfo>();
        foreach (var chemin in _reader.ListThermalZones())
        {
            var tempTxt = _reader.ReadAllText(Path.Combine(chemin, "temp"));
            var celsius = tempTxt is null ? null : ThermalZoneParser.ParseTempCelsius(tempTxt);
            if (celsius is null)
            {
                InstruireUneFois(chemin);
                continue;
            }

            zones.Add(new ThermalZoneInfo
            {
                Zone = Path.GetFileName(chemin),
                Type = _reader.ReadAllText(Path.Combine(chemin, "type")),
                Celsius = celsius.Value
            });
        }

        return zones;
    }

    private HostSessionMetricsInfo CompteSessions()
    {
        var dateLimite = DateTime.UtcNow.AddSeconds(-ActiveSessionWindowSeconds);
        var actives = 0;
        var enTranscodage = 0;
        foreach (var session in _sessionManager.Sessions)
        {
            if (session.LastActivityDate < dateLimite)
            {
                continue;
            }

            actives++;
            if (session.TranscodingInfo is not null)
            {
                enTranscodage++;
            }
        }

        return new HostSessionMetricsInfo { Active = actives, Transcoding = enTranscodage };
    }

    private void InstruireUneFois(string source)
    {
        lock (_lock)
        {
            if (_warned.Add(source))
            {
                _logger.LogWarning("Source métriques {Source} illisible : champs null", source);
            }
        }
    }

    private static HostMetricsInfo SnapshotVide(DateTimeOffset now)
    {
        return new HostMetricsInfo
        {
            Timestamp = now,
            Cpu = new CpuHostMetricsInfo { Percent = null, CoreCount = Environment.ProcessorCount, Load1 = null, Load5 = null },
            MemoryTotalKiB = null,
            MemoryAvailableKiB = null,
            SwapTotalKiB = null,
            SwapFreeKiB = null,
            Process = new ProcessHostMetricsInfo { WorkingSetBytes = null, HeapBytes = null, UptimeSeconds = null },
            HostUptimeSeconds = null,
            ThermalZones = [],
            Sessions = new HostSessionMetricsInfo { Active = 0, Transcoding = 0 }
        };
    }
}
```

**Notes d'implémentation :**
1. Le type `ProcessHostMetricsInfo` et la méthode de lecture portent des noms distincts (méthode : `ReadProcessMetrics()`) — évite tout conflit de résolution.
2. Documentation XML sur tous les membres publics du fichier (fr) ; les constantes Chemin* portent un doc.
3. Les classes de test (`FakeReader` etc.) sont exemptes de doc (code de test, cf. AGENTS.md).

- [ ] **Step 5: Enregistrement DI dans `Jellyfin.Server/Startup.cs`**

À la fin du bloc (l.149-158), après `DeviceAccessHost` :

```csharp
            services.AddHostedService<DeviceAccessHost>();
            services.AddHostedService<HostMetricsService>();
            services.AddSingleton<IHostMetricsReader, DirectoryHostMetricsReader>();
            services.AddSingleton<IHostMetricsService>(sp => sp.GetRequiredService<HostMetricsService>());
```

Usings à ajouter en tête de fichier si absents :

```csharp
using Emby.Server.Implementations.Metrics;
using MediaBrowser.Controller.Metrics;
```

- [ ] **Step 6: Vérifier que les tests passent**

Run: `dotnet test tests/Jellyfin.Server.Implementations.Tests --filter "FullyQualifiedName~HostMetricsServiceTests" --nologo`
Expected: PASS (10 tests).

Run: `dotnet build Jellyfin.sln --nologo`
Expected: PASS sans warning.

- [ ] **Step 7: Commit**

```bash
git add Emby.Server.Implementations/Metrics/IHostMetricsReader.cs Emby.Server.Implementations/Metrics/DirectoryHostMetricsReader.cs Emby.Server.Implementations/Metrics/HostMetricsService.cs Jellyfin.Server/Startup.cs tests/Jellyfin.Server.Implementations.Tests/Metrics/HostMetricsServiceTests.cs
git commit -m "feat(metrics): service d'échantillonnage HostMetricsService avec DI"
```

---

### Task 4: Plugin page embarquée `Jellyfin.Plugin.HostMetrics`

**Files:**
- Create: `Jellyfin.Plugin.HostMetrics/Jellyfin.Plugin.HostMetrics.csproj`
- Create: `Jellyfin.Plugin.HostMetrics/HostMetricsPlugin.cs`
- Create: `Jellyfin.Plugin.HostMetrics/embedded/hostmetrics.html`
- Modify: `Jellyfin.sln` (Project + configuration)
- Test: `tests/Jellyfin.Common.Tests/Plugins/HostMetricsPluginTests.cs`

**Interfaces:**
- Consumes: `BasePlugin` (ns `MediaBrowser.Common.Plugins`), `IHasWebPages` / `PluginPageInfo` (ns `MediaBrowser.Model.Plugins`).
- Produces:
  - Plugin `Jellyfin.Plugin.HostMetrics.HostMetricsPlugin : BasePlugin, IHasWebPages` :
    - `Name => "Métriques host"`, `Description` fr courte.
    - `GetPages()` → 1 `PluginPageInfo` : `Name="HostMetrics"`, `DisplayName="Métriques host"`, `EmbeddedResourcePath="Jellyfin.Plugin.HostMetrics.embedded.hostmetrics.html"`, `EnableInMainMenu=false`, `MenuSection="server"`, `MenuIcon="cloud"`.
  - Page accessible à `configurationpage?name=HostMetrics` (route supportée par le bundle web v12.3 + `DashboardController`).
  - Endpoint appelé par la page : `GET /System/HostMetrics` (Task 1).

- [ ] **Step 1: Écrire le test qui échoue (smoke plugin)**

`tests/Jellyfin.Common.Tests/Plugins/HostMetricsPluginTests.cs` :

```csharp
using System.IO;
using System.Linq;
using Jellyfin.Plugin.HostMetrics;
using Xunit;

namespace Jellyfin.Common.Tests.Plugins;

public class HostMetricsPluginTests
{
    [Fact]
    public void GetPages_UneSeulePage_ChampsFideles()
    {
        var pages = new HostMetricsPlugin().GetPages().ToList();
        var page = Assert.Single(pages);
        Assert.Equal("HostMetrics", page.Name);
        Assert.Equal("Métriques host", page.DisplayName);
        Assert.Equal("Jellyfin.Plugin.HostMetrics.embedded.hostmetrics.html", page.EmbeddedResourcePath);
        Assert.False(page.EnableInMainMenu);
        Assert.Equal("server", page.MenuSection);
        Assert.Equal("cloud", page.MenuIcon);
    }

    [Fact]
    public void Resource_Embarquee_Lisible()
    {
        var stream = typeof(HostMetricsPlugin).Assembly.GetManifestResourceStream(
            "Jellyfin.Plugin.HostMetrics.embedded.hostmetrics.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();
        Assert.Contains("System/HostMetrics", html);
        Assert.Contains("MESSAGES", html);
    }

    [Fact]
    public void Nom_Et_Description_Presents()
    {
        var plugin = new HostMetricsPlugin();
        Assert.False(string.IsNullOrWhiteSpace(plugin.Name));
        Assert.False(string.IsNullOrWhiteSpace(plugin.Description));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet build tests/Jellyfin.Common.Tests --nologo`
Expected: FAIL — projet `Jellyfin.Plugin.HostMetrics` inexistant.

- [ ] **Step 3: Projet + classe plugin**

`Jellyfin.Plugin.HostMetrics/Jellyfin.Plugin.HostMetrics.csproj` :

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- ProjectGuid is only included as a requirement for SonarQube analysis -->
  <PropertyGroup>
    <ProjectGuid>{0B2E4B3E-31EA-4F6B-9D5A-E6F66B7C0B81}</ProjectGuid>
  </PropertyGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>Jellyfin.Plugin.HostMetrics</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="..\SharedVersion.cs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MediaBrowser.Common\MediaBrowser.Common.csproj" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="embedded\hostmetrics.html" />
  </ItemGroup>

  <!-- Code Analyzers -->
  <ItemGroup Condition=" '$(Configuration)' == 'Debug' ">
    <PackageReference Include="IDisposableAnalyzers">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="SerilogAnalyzer" PrivateAssets="All" />
    <PackageReference Include="StyleCop.Analyzers" PrivateAssets="All" />
    <PackageReference Include="SmartAnalyzers.MultithreadingAnalyzer" PrivateAssets="All" />
  </ItemGroup>

</Project>
```

`Jellyfin.Plugin.HostMetrics/HostMetricsPlugin.cs` :

```csharp
using System.Collections.Generic;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HostMetrics;

/// <summary>
/// Plugin d'affichage des métriques host : une seule page de configuration embarquée.
/// </summary>
public class HostMetricsPlugin : BasePlugin, IHasWebPages
{
    /// <summary>
    /// Obtient le nom du plugin affiché dans le dashboard.
    /// </summary>
    public override string Name => "Métriques host";

    /// <summary>
    /// Obtient la description courte du plugin.
    /// </summary>
    public override string Description => "Métriques host du serveur : CPU, RAM, process, thermique, sessions.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "HostMetrics",
            DisplayName = "Métriques host",
            EmbeddedResourcePath = "Jellyfin.Plugin.HostMetrics.embedded.hostmetrics.html",
            EnableInMainMenu = false,
            MenuSection = "server",
            MenuIcon = "cloud"
        };
    }
}
```

- [ ] **Step 4: Page HTML embarquée**

`Jellyfin.Plugin.HostMetrics/embedded/hostmetrics.html` :

```html
<!DOCTYPE html>
<html lang="fr">
<head>
<meta charset="utf-8"/>
<title>Métriques host</title>
<style>
  body { font-family: sans-serif; color: inherit; max-width: 720px; margin: 0 auto; }
  .carte { border: 1px solid rgba(127,127,127,.4); border-radius: 8px; padding: 10px 14px; margin: 8px 0; }
  .ligne { display: flex; justify-content: space-between; gap: 16px; }
  .valeur { font-weight: bold; }
  .chaleur { color: #ff5f5f; }
  .barre { background: rgba(127,127,127,.25); border-radius: 4px; height: 10px; margin-top: 6px; }
  .barre > div { background: #00a4dc; height: 10px; border-radius: 4px; width: 0; }
  .nd { opacity: .5; }
  h1 { font-size: 1.15em; } h2 { font-size: 1em; margin: 4px 0; }
</style>
</head>
<body>
<div data-role="page" class="page standalonePage" data-theme="b">
  <h1></h1>
  <div class="carte">
    <div class="ligne"><span data-label="cpu"></span><span class="valeur" id="cpu"></span></div>
    <div class="barre"><div id="cpu-bar"></div></div>
    <div class="ligne"><span data-label="ram"></span><span class="valeur" id="ram"></span></div>
    <div class="barre"><div id="ram-bar"></div></div>
    <div class="ligne"><span data-label="swap"></span><span class="valeur" id="swap"></span></div>
    <div class="barre"><div id="swap-bar"></div></div>
  </div>
  <div class="carte"><h2 data-label="process"></h2>
    <div class="ligne"><span data-label="workingset"></span><span class="valeur" id="ws"></span></div>
    <div class="ligne"><span data-label="heap"></span><span class="valeur" id="heap"></span></div>
    <div class="ligne"><span data-label="uptimeProcess"></span><span class="valeur" id="upt"></span></div>
    <div class="ligne"><span data-label="uptimeHost"></span><span class="valeur" id="hostUp"></span></div>
  </div>
  <div class="carte"><h2 data-label="thermal"></h2><div id="zones"></div></div>
  <div class="carte"><h2 data-label="sessions"></h2>
    <div class="ligne"><span data-label="active"></span><span class="valeur" id="act"></span></div>
    <div class="ligne"><span data-label="transcodage"></span><span class="valeur" id="tco"></span></div>
  </div>
</div>
<script>
var MESSAGES = {
  fr: {
    titre: "Métriques host",
    cpu: "CPU global",
    ram: "RAM host",
    swap: "Swap",
    process: "Processus serveur",
    workingset: "Mémoire (working set)",
    heap: "Tas GC",
    uptimeProcess: "Uptime serveur",
    uptimeHost: "Uptime host",
    thermal: "Zones thermiques",
    sessions: "Sessions (300 s)",
    active: "Active",
    transcodage: "En transcodage",
    nd: "n/d"
  }
};
var L = MESSAGES.fr;
var SEUIL_ARRET_THERMIQUE = 85;
var INTERVALLE_MS = 3000;

function indisp(v) { return v === null || v === undefined ? L.nd : v; }
function kib(v) { return v == null ? L.nd : (v / 1024 / 1024).toFixed(1) + " GiB"; }
function octets(v) { return v == null ? L.nd : (v / 1024 / 1024).toFixed(0) + " Mo"; }
function pct(p) { return p == null ? L.nd : p.toFixed(1) + " %"; }
function duree(v) { return v == null ? L.nd : Math.floor(v / 3600) + " h " + Math.round((v % 3600) / 60) + " min"; }

function appliqueLabels() {
  document.title = L.titre;
  document.querySelector("h1").textContent = L.titre;
  document.querySelectorAll("[data-label]").forEach(function (n) {
    n.textContent = L[n.getAttribute("data-label")] || n.textContent;
  });
}

function barre(id, ratio) {
  var el = document.getElementById(id);
  if (el) { el.style.width = Math.min(100, Math.max(0, ratio * 100)) + "%"; }
}

function rendDonnees(d) {
  document.getElementById("cpu").textContent = pct(d.cpu.percent);
  barre("cpu-bar", (d.cpu.percent || 0) / 100);
  var memoireUtilisee = d.memoryTotalKiB && d.memoryAvailableKiB ? d.memoryTotalKiB - d.memoryAvailableKiB : null;
  document.getElementById("ram").textContent = kib(memoireUtilisee) + " / " + kib(d.memoryTotalKiB);
  barre("ram-bar", memoireUtilisee && d.memoryTotalKiB ? memoireUtilisee / d.memoryTotalKiB : 0);
  var swapUtilisee = d.swapTotalKiB && d.swapFreeKiB ? d.swapTotalKiB - d.swapFreeKiB : null;
  document.getElementById("swap").textContent = kib(swapUtilisee) + " / " + kib(d.swapTotalKiB);
  barre("swap-bar", swapUtilisee && d.swapTotalKiB ? swapUtilisee / d.swapTotalKiB : 0);
  document.getElementById("ws").textContent = octets(d.process.workingSetBytes);
  document.getElementById("heap").textContent = octets(d.process.heapBytes);
  document.getElementById("upt").textContent = duree(d.process.uptimeSeconds);
  document.getElementById("hostUp").textContent = duree(d.hostUptimeSeconds);
  rendZones(d.thermalZones || []);
  document.getElementById("act").textContent = indisp(d.sessions.active);
  document.getElementById("tco").textContent = indisp(d.sessions.transcoding);
}

function rendZones(zones) {
  var html = "";
  zones.forEach(function (z) {
    var classe = z.celsius >= SEUIL_ARRET_THERMIQUE ? "valeur chaleur" : "valeur";
    html += '<div class="ligne"><span>' + indisp(z.type || z.zone) +
      '</span><span class="' + classe + '">' + z.celsius.toFixed(1) + " °C</span></div>";
  });
  document.getElementById("zones").innerHTML = html || '<span class="nd">' + L.nd + "</span>";
}

function requete() {
  var entete = {};
  var url = "/System/HostMetrics";
  if (window.ApiClient) {
    entete.Authorization = "MediaBrowser Token=\"" + ApiClient.accessToken() + "\"";
    url = ApiClient.serverAddress() + url;
  }
  fetch(url, { headers: entete })
    .then(function (r) { return r.json(); })
    .then(function (d) { rendDonnees(d); setTimeout(requete, INTERVALLE_MS); })
    .catch(function () { setTimeout(requete, INTERVALLE_MS); });
}

appliqueLabels();
requete();
</script>
</body>
</html>
```

- [ ] **Step 5: Référencer dans la solution**

`Jellyfin.sln` :
- Section projets racine — après l'entrée `Emby.Server.Implementations` (l.24-25) :

```
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Jellyfin.Plugin.HostMetrics", "Jellyfin.Plugin.HostMetrics\Jellyfin.Plugin.HostMetrics.csproj", "{E5F0C2A1-77D4-4C0A-9B1B-3AA5F1B2C7D9}"
EndProject
```

- GlobalSection(ProjectConfigurationPlatforms) — reproduire le pattern d'une entrée C# existante (ex. `Jellyfin.Api`) en substituant le GUID `DFBEFB4C-DA19-4143-98B7-27320C7F7163` par `E5F0C2A1-77D4-4C0A-9B1B-3AA5F1B2C7D9` (toutes les configurations présentes dans la solution : Debug|AnyCPU, Release|AnyCPU, Debug|ARM64 etc. — lister exactement comme les autres).

- [ ] **Step 6: Vérifier que les tests passent**

Run: `dotnet test tests/Jellyfin.Common.Tests --filter "FullyQualifiedName~HostMetricsPluginTests" --nologo`
Expected: PASS (3 tests).

Run: `dotnet publish Jellyfin.Plugin.HostMetrics/Jellyfin.Plugin.HostMetrics.csproj -c Release -o /tmp/opencode/plugin-publish --nologo`
Expected: `Jellyfin.Plugin.HostMetrics.dll` + resource embarquée présents dans `/tmp/opencode/plugin-publish/`.

- [ ] **Step 7: Commit**

```bash
git add Jellyfin.Plugin.HostMetrics Jellyfin.sln tests/Jellyfin.Common.Tests/Plugins/HostMetricsPluginTests.cs
git commit -m "feat(metrics): page de configuration embarquée via plugin local"
```

---

### Task 5: Distribution APK Shield (assets + syncPlugin)

**Files:**
- Modify: `deployment/apk-android/collect-assets.sh` (étape 5)
- Modify: `app-server/app/src/main/kotlin/org/jellyfin/shieldserver/ServerAssets.kt`
- Modify: `app-server/app/src/main/kotlin/org/jellyfin/shieldserver/ServerService.kt`
- Modify: `app-server/app/src/test/kotlin/org/jellyfin/shieldserver/ServerAssetsTest.kt`

**Interfaces:**
- Consumes: `dotnet publish` du plugin (Task 4), motif `syncTls` (`ServerService.kt:166-182`), extraction tar dans `ServerService.extractTar` (l.184-198) réutilisée en base de `syncPlugin`.
- Produces:
  - Asset `plugin/hostmetrics-plugin.tar.xz` (entrées à la racine : `HostMetrics.dll`, `meta.json`).
  - `ServerAssets.assetFilenames()` : + `"plugin/hostmetrics-plugin.tar.xz"` (9 assets).
  - `ServerService.syncPlugin(datadir: File)` : décompresse l'asset et copie byte-à-byte (byte-compare) vers `<datadir>/plugins/HostMetrics/` au démarrage (après `syncTls`), fail-open (jamais bloquant).
  - `meta.json` manifeste : `id 0b2e4b3e-31ea-4f6b-9d5a-e6f66b7c0b81`, `version` = version serveur (13.0.0), `targetAbi` = `13.0`, `status: Active`, `assemblies: ["HostMetrics.dll"]`.

- [ ] **Step 1: `collect-assets.sh` — ajout de l'étape (5) en fin de fichier**

Insérer à la fin du fichier (après la dernière ligne d'empaquetage `printf '[serveur] stamp=%s...` de l'étape (4)) :

```bash
# (5) plugin page de configuration « Métriques host » : publish IL pur net10.0,
#     renommé HostMetrics.dll + manifeste meta.json aligné sur la version serveur
SERVER_VERSION="$(grep -oPm1 'Version\("\K[0-9.]+' SharedVersion.cs)"
TARGET_ABI="${SERVER_VERSION%.*}"
PLUGIN_NAME="Jellyfin.Plugin.HostMetrics"
mkdir -p "$WORK/plugin"
dotnet publish "$DEPLOY_DIR/../../$PLUGIN_NAME/$PLUGIN_NAME.csproj" -c Release -o "$WORK/plugin-publish"
cp "$WORK/plugin-publish/$PLUGIN_NAME.dll" "$WORK/plugin/HostMetrics.dll"
{
  printf '{\n'
  printf '  "id": "0b2e4b3e-31ea-4f6b-9d5a-e6f66b7c0b81",\n'
  printf '  "name": "Métriques host",\n'
  printf '  "version": "%s",\n' "$SERVER_VERSION"
  printf '  "owner": "Thyrack",\n'
  printf '  "category": "General",\n'
  printf '  "targetAbi": "%s",\n' "$TARGET_ABI"
  printf '  "status": "Active",\n'
  printf '  "assemblies": ["HostMetrics.dll"],\n'
  printf '  "autoUpdate": false\n'
  printf '}\n'
} > "$WORK/plugin/meta.json"
mkdir -p "$ASSETS_DIR/plugin"
tar -C "$WORK/plugin" -cJf "$ASSETS_DIR/plugin/hostmetrics-plugin.tar.xz" HostMetrics.dll meta.json
printf '[plugin] version=%s, targetAbi=%s\n' "$SERVER_VERSION" "$TARGET_ABI"
```

(Remarque : entrées tar à la racine « HostMetrics.dll », « meta.json » — volontairement SANS prefix « ./ », cf. `syncPlugin`.)

Et mettre à jour l'entête (section DRY-RUN, l.16-19) en ajoutant une ligne :

```bash
  printf '%s\n' '-> publish du plugin page host-metrics (IL net10.0, tar.xz assets/plugin)'
```

Vérification statique du script :

Run: `bash -n deployment/apk-android/collect-assets.sh && shellcheck deployment/apk-android/collect-assets.sh`
Expected: 0 nouvelle erreur shellcheck.

- [ ] **Step 2: `ServerAssets.kt` — catalogue**

Dans `assetFilenames()` (l.7-19), ajouter après le bloc tlsprovider :

```kotlin
        // Plugin page de configuration « Métriques host » (déployé dans datadir/plugins/HostMetrics)
        "plugin/hostmetrics-plugin.tar.xz",
```

- [ ] **Step 3: `ServerService.kt` — `syncPlugin` + appel**

Après la fonction `syncTls` (l.166-182) :

```kotlin
    /**
     * Diffusion plugin (métriques host) : décompresse l'asset XZ embarqué et compare
     * byte-à-byte chaque entrée avec la cible <datadir>/plugins/HostMetrics/ ; réécrit
     * si le contenu diffère. Fail-open (motif TlsSyncer) : un asset indisponible n'empêche
     * jamais le démarrage du serveur.
     */
    private fun syncPlugin(datadir: File) {
        val pluginDir = File(datadir, "plugins/HostMetrics").apply { mkdirs() }
        try {
            assets.open("plugin/hostmetrics-plugin.tar.xz").use { flux ->
                TarArchiveInputStream(XZCompressorInputStream(flux)).use { tar ->
                    var ecrits = 0
                    while (true) {
                        val entry = tar.nextTarEntry ?: break
                        if (entry.isDirectory) continue
                        val cible = File(pluginDir, entry.name)
                        cible.parentFile?.mkdirs()
                        val contenu = tar.readBytes()
                        if (!cible.isFile || !cible.readBytes().contentEquals(contenu)) {
                            cible.outputStream().use { it.write(contenu) }
                            ecrits++
                        }
                    }
                    Log.i(TAG, "syncPlugin : $ecrits fichier(s) synchronisés dans ${pluginDir.path}")
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "syncPlugin : plugin métriques indisponible : ${e.message}")
        }
    }
```

Appel dans `onStartCommand` (l.60-61) :

```kotlin
                syncNatives(dir)
                syncTls(dir)
                syncPlugin(DataDirSelector.choisir(filesDir).datadir)
```

- [ ] **Step 4: `ServerAssetsTest.kt` — catalogue à 9**

Remplacer le test `les 8 noms d'assets attendus` par :

```kotlin
    @Test fun `les 9 noms d'assets attendus`() {
        assertEquals(
            listOf(
                "server/jellyfin-server.tar.xz", "web/jellyfin-web.tar.xz",
                "bin/ffmpeg.xz", "bin/ffprobe.xz",
                // Provider TLS G4-bis : IL linux substituées + natives BoringSSL
                "tlsprovider/System.Security.Cryptography.dll",
                "tlsprovider/System.Net.Security.dll",
                "tlsprovider/libSystem.Security.Cryptography.Native.OpenSsl.so",
                "tlsprovider/libSystem.Net.Security.Native.so",
                // Plugin page de configuration « Métriques host »
                "plugin/hostmetrics-plugin.tar.xz"
            ),
            ServerAssets.assetFilenames())
    }
```

- [ ] **Step 5: Vérifier les tests Kotlin**

Run: `cd app-server && ./gradlew :app:testDebugUnitTest --console=plain`
Expected: PASS (ServerAssets + ServerService sans régression).

- [ ] **Step 6: Commit**

```bash
git add deployment/apk-android/collect-assets.sh app-server/app/src/main/kotlin/org/jellyfin/shieldserver/ServerAssets.kt app-server/app/src/main/kotlin/org/jellyfin/shieldserver/ServerService.kt app-server/app/src/test/kotlin/org/jellyfin/shieldserver/ServerAssetsTest.kt
git commit -m "feat(apk): distribution du plugin métriques host via assets et syncPlugin"
```

---

### Task 6: Vérification globale (aucun commit)

**Files:** aucune modification.

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: verdict qualité finale (à joindre au rapport).

- [ ] **Step 1: Build complet**

Run: `dotnet build Jellyfin.sln --nologo`
Expected: 0 erreur, 0 warning (`TreatWarningsAsErrors`).

- [ ] **Step 2: Suites C#**

Run: `dotnet test tests/Jellyfin.Api.Tests tests/Jellyfin.Server.Implementations.Tests tests/Jellyfin.Common.Tests --nologo`
Expected: PASS complet, aucune régression sur les suites préexistantes.

- [ ] **Step 3: Tests Kotlin**

Run: `cd app-server && ./gradlew :app:testDebugUnitTest --console=plain`
Expected: PASS.

- [ ] **Step 4: Contrôle qualité**

Run: `km cogcom --format terse Emby.Server.Implementations/Metrics Jellyfin.Api/Controllers/SystemController.cs Jellyfin.Plugin.HostMetrics/HostMetricsPlugin.cs` (le cas échéant : suivre les commandes précises listées par la skill `expert-kimun`, exécutées avec `--format terse`)
Expected: cogcom ≤ 15, indent ≤ 3, aucune dégradation de score.

- [ ] **Step 5: Rapport final**

Rédiger : fichiers créés/modifiés, tests exécutés + résultats, suites régressées éventuelles, suivi du plan (checkboxes).

---

## Auto-review (réalisée par le rédacteur du plan)

- [x] Spec §3.1 : Task 1 (modèle/DTO/endpoint/SystemManager) + Task 3 (service + DI Startup.cs).
- [x] Spec §3.2 : Task 2 (parsers purs) + Task 3 (échantillonnage 2 s, sessions 300 s, WARN unique, coreCount via `Environment.ProcessorCount`).
- [x] Spec §3.3 : payload Task 1 (`hostUptimeSeconds` top-level ≠ `process.uptimeSeconds`).
- [x] Spec §3.4 : Task 4 (`MESSAGES` fr, MenuSection/Menu, URL garantie, pas de `IPluginServiceRegistrator`).
- [x] Spec §3.5 : Task 5 (étape 5 collect-assets, `targetAbi: 13.0`, `syncPlugin` fail-open sur motif syncTls, ServerAssetsTest 9 assets).
- [x] Spec §5 : nulls gracieux + WARN unique (Task 2/3), endpoint jamais d'exception (Task 1).
- [x] Spec §6 : fixtures parsers, delta CPU, sessions 299/301/`TranscodingInfo != null` (suggestion conservée), WARN unique, smoke plugin, ServerAssetsTest.
- [x] Pistes écartées respectées : pas de web modifié, pas d'historique, pas de Windows, pas d'échantillon à l'appel HTTP.
- [x] Types cohérents entre tasks (`CpuSample`, `IHostMetricsReader`, `HostMetricsInfo` ↔ `HostMetricsDto`, signatures consignées dans chaque Interfaces).
