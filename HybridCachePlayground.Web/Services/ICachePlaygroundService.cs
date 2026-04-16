using HybridCachePlayground.Web.Models;

namespace HybridCachePlayground.Web.Services;

public interface ICachePlaygroundService
{
    Task SetAsync(string key, string value, IEnumerable<string> tags, int expirationMinutes, CancellationToken ct = default);

    Task<CacheGetResult> GetOrCreateAsync(string key, CancellationToken ct = default);

    Task<StampedeResult> RunStampedeTestAsync(string key, int concurrency, bool forceEvict, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    Task<int> RemoveByTagAsync(string tag, CancellationToken ct = default);

    /// <summary>
    /// Removes all entries whose tags match a glob pattern (* = any chars, ? = single char).
    /// Returns the number of entries removed and the list of matched tags.
    /// </summary>
    Task<(int RemovedEntries, IReadOnlyList<string> MatchedTags)> RemoveByTagWildcardAsync(string pattern, CancellationToken ct = default);

    /// <summary>Returns all known tags that match the given glob pattern (preview, no removal).</summary>
    IReadOnlyList<string> GetMatchingTags(string pattern);

    IReadOnlyList<CacheEntryMetadata> GetAllEntries();

    CacheStats GetStats();

    IReadOnlyList<KeyRegistryEntry> GetKeyRegistry();

    IReadOnlyList<TagRegistryEntry> GetTagRegistry();

    void PruneExpired();
}
