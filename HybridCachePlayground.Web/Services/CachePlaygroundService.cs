using System.Collections.Concurrent;
using HybridCachePlayground.Web.Models;
using Microsoft.Extensions.Caching.Hybrid;

namespace HybridCachePlayground.Web.Services;

public sealed class CachePlaygroundService : ICachePlaygroundService
{
    private readonly HybridCache _cache;
    private readonly ConcurrentDictionary<string, CacheEntryMetadata> _metadata = new();
    private long _hits;
    private long _misses;

    public CachePlaygroundService(HybridCache cache)
    {
        _cache = cache;
    }

    public async Task SetAsync(string key, string value, IEnumerable<string> tags, int expirationMinutes, CancellationToken ct = default)
    {
        var tagList = tags.ToList();
        var options = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(expirationMinutes),
            LocalCacheExpiration = TimeSpan.FromMinutes(Math.Min(expirationMinutes, 2))
        };

        await _cache.SetAsync(key, value, options, tagList, ct);

        _metadata[key] = new CacheEntryMetadata
        {
            Key = key,
            Value = value,
            Tags = tagList,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes)
        };
    }

    public async Task<CacheGetResult> GetOrCreateAsync(string key, CancellationToken ct = default)
    {
        var wasHit = _metadata.ContainsKey(key) && !_metadata[key].IsExpired;

        // Use a flag to detect whether the factory ran (cache miss)
        var factoryRan = false;

        var value = await _cache.GetOrCreateAsync(key, async innerCt =>
        {
            factoryRan = true;
            await Task.CompletedTask;
            return (string?)null;
        }, cancellationToken: ct);

        var isHit = !factoryRan && value is not null;

        if (isHit)
        {
            Interlocked.Increment(ref _hits);
            if (_metadata.TryGetValue(key, out var meta))
                meta.LastAccessedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            Interlocked.Increment(ref _misses);
            // Remove stale metadata if the factory ran (entry wasn't in cache)
            if (factoryRan)
                _metadata.TryRemove(key, out _);
        }

        return new CacheGetResult
        {
            Key = key,
            Value = value,
            IsHit = isHit,
            HasResult = value is not null
        };
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(key, ct);
        _metadata.TryRemove(key, out _);
    }

    public async Task<int> RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        await _cache.RemoveByTagAsync(tag, ct);

        // Prune metadata entries that had this tag
        var removed = 0;
        foreach (var kvp in _metadata)
        {
            if (kvp.Value.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                if (_metadata.TryRemove(kvp.Key, out _))
                    removed++;
            }
        }
        return removed;
    }

    public IReadOnlyList<CacheEntryMetadata> GetAllEntries()
    {
        PruneExpired();
        return _metadata.Values.OrderBy(e => e.CreatedAt).ToList();
    }

    public CacheStats GetStats()
    {
        var all = _metadata.Values.ToList();
        return new CacheStats
        {
            Hits = Interlocked.Read(ref _hits),
            Misses = Interlocked.Read(ref _misses),
            TotalTrackedEntries = all.Count,
            ActiveEntries = all.Count(e => !e.IsExpired)
        };
    }

    public void PruneExpired()
    {
        foreach (var kvp in _metadata.Where(k => k.Value.IsExpired).ToList())
            _metadata.TryRemove(kvp.Key, out _);
    }
}
