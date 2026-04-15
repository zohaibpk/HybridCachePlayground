using HybridCachePlayground.Web.Models;

namespace HybridCachePlayground.Web.Services;

public interface ICachePlaygroundService
{
    Task SetAsync(string key, string value, IEnumerable<string> tags, int expirationMinutes, CancellationToken ct = default);

    Task<CacheGetResult> GetOrCreateAsync(string key, CancellationToken ct = default);

    Task<StampedeResult> RunStampedeTestAsync(string key, int concurrency, bool forceEvict, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    Task<int> RemoveByTagAsync(string tag, CancellationToken ct = default);

    IReadOnlyList<CacheEntryMetadata> GetAllEntries();

    CacheStats GetStats();

    IReadOnlyList<KeyRegistryEntry> GetKeyRegistry();

    IReadOnlyList<TagRegistryEntry> GetTagRegistry();

    void PruneExpired();
}
