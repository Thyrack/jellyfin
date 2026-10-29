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
