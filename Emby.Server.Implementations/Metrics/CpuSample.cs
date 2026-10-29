namespace Emby.Server.Implementations.Metrics;

/// <summary>
/// Compteurs CPU agrégés extraits de /proc/stat (total cumulé, idle, iowait).
/// </summary>
/// <param name="Total">Somme des compteurs user..steal (guest exclu : déjà compris dans user côté noyau).</param>
/// <param name="Idle">Compteur idle.</param>
/// <param name="Iowait">Compteur iowait.</param>
public sealed record CpuSample(long Total, long Idle, long Iowait);
