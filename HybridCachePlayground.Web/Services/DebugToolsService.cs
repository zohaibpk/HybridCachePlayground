using System.Text.Json;
using HybridCachePlayground.Web.Models;

namespace HybridCachePlayground.Web.Services;

public sealed class DebugToolsService : IDebugToolsService
{
    private readonly ICachePlaygroundService _cache;
    private readonly INotificationService   _notifications;
    private readonly ILogger<DebugToolsService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { WriteIndented = true };

    public DebugToolsService(
        ICachePlaygroundService cache,
        INotificationService notifications,
        ILogger<DebugToolsService> logger)
    {
        _cache         = cache;
        _notifications = notifications;
        _logger        = logger;
    }

    // ── Stubs ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ── USER IMPLEMENTATION REQUIRED ─────────────────────────────────────────
    /// Inject IMemoryCache / MemoryCache in the constructor and clear it:
    ///
    ///   if (_memoryCache is MemoryCache mc) mc.Clear();
    ///
    /// Note: MemoryCache.Clear() was added in .NET 7. For earlier versions use
    ///   mc.Compact(1.0) to evict all entries.
    /// ─────────────────────────────────────────────────────────────────────────
    public Task ClearL1CacheAsync()
    {
        _logger.LogWarning("ClearL1Cache called — stub not yet implemented");
        // Add your L1 clear code here.
        return Task.CompletedTask;
    }

    /// <summary>
    /// ── USER IMPLEMENTATION REQUIRED ─────────────────────────────────────────
    /// IDistributedCache has no ClearAll API. Use the provider-specific SDK:
    ///
    ///   Redis (StackExchange.Redis):
    ///     var server = _redis.GetServer(endpoint);
    ///     await server.FlushDatabaseAsync();
    ///
    ///   NCache:
    ///     NCache.InitializeCache("cacheName").Clear();
    ///
    ///   SQL Server distributed cache:
    ///     await _db.ExecuteSqlRawAsync("TRUNCATE TABLE dbo.HybridCache");
    /// ─────────────────────────────────────────────────────────────────────────
    public Task ClearL2CacheAsync()
    {
        _logger.LogWarning("ClearL2Cache called — stub not yet implemented");
        // Add your L2 clear code here.
        return Task.CompletedTask;
    }

    // ── Functional ───────────────────────────────────────────────────────────

    public async Task ResetStatisticsAsync()
    {
        _cache.ResetStatistics();
        _logger.LogInformation("Statistics reset via debug tools");
        await _notifications.NotifyAsync(
            "Statistics Reset", "Hit/miss/factory counters cleared.", NotificationLevel.Info);
    }

    public void PruneExpiredEntries()
    {
        _logger.LogInformation("Force prune triggered via debug tools");
        _cache.PruneExpired();
    }

    public async Task<int> SimulatePressureAsync(
        string keyPrefix, int entryCount, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Simulate pressure | Prefix: {Prefix} | Count: {Count}", keyPrefix, entryCount);

        await _cache.BulkSetAsync(keyPrefix, entryCount, ["pressure-test"], 5, ct: ct);

        await _notifications.NotifyAsync(
            "Pressure Simulation",
            $"{entryCount} entries added with prefix '{keyPrefix}'.",
            NotificationLevel.Warning);

        return entryCount;
    }

    public async Task<string> ExportSnapshotAsync()
    {
        var entries = _cache.GetAllEntries();
        var stats   = _cache.GetStats();

        var snapshot = new
        {
            ExportedAt   = DateTimeOffset.UtcNow,
            Stats        = stats,
            EntryCount   = entries.Count,
            Entries      = entries
        };

        var json = JsonSerializer.Serialize(snapshot, _jsonOpts);
        _logger.LogInformation("Cache snapshot exported | Entries: {Count}", entries.Count);

        await _notifications.NotifyAsync(
            "Snapshot Exported",
            $"{entries.Count} entries exported.",
            NotificationLevel.Success);

        return json;
    }

    public void LogCacheState()
    {
        var stats = _cache.GetStats();
        _logger.LogInformation(
            "Cache State Dump | Active: {Active} | Hits: {Hits} | Misses: {Misses} | " +
            "HitRatio: {Ratio}% | UniqueKeys: {Keys} | UniqueTags: {Tags} | FactoryRuns: {Factory}",
            stats.ActiveEntries, stats.Hits, stats.Misses, stats.HitRatio,
            stats.TotalUniqueKeys, stats.TotalUniqueTags, stats.FactoryInvocations);
    }
}
