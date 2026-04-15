using System.Collections.Concurrent;
using HybridCachePlayground.Web.Models;
using Microsoft.Extensions.Caching.Hybrid;

namespace HybridCachePlayground.Web.Services;

public sealed class CachePlaygroundService : ICachePlaygroundService
{
    private readonly HybridCache _cache;
    private readonly int _defaultTtlMinutes;
    private readonly int _defaultLocalTtlMinutes;

    // Active entries (pruned on expiry)
    private readonly ConcurrentDictionary<string, CacheEntryMetadata> _metadata = new();

    // All-time registries — survive eviction, reset on app restart
    private readonly ConcurrentDictionary<string, KeyRegistryEntry> _keyRegistry = new();
    private readonly ConcurrentDictionary<string, TagRegistryEntry> _tagRegistry = new();

    private long _hits;
    private long _misses;
    private long _factoryInvocations;

    public CachePlaygroundService(HybridCache cache, IConfiguration configuration)
    {
        _cache = cache;
        _defaultTtlMinutes = configuration.GetValue("HybridCache:DefaultExpirationMinutes", 5);
        _defaultLocalTtlMinutes = configuration.GetValue("HybridCache:LocalCacheExpirationMinutes", 2);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tags are normalized to lowercase-trimmed on every write so all
    /// comparisons can use ordinal exact-match — no repeated OrdinalIgnoreCase.
    /// </summary>
    private static string NormalizeTag(string tag) => tag.Trim().ToLowerInvariant();

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(NormalizeTag).Distinct().ToList();

    private HybridCacheEntryOptions MakeOptions(int expirationMinutes) => new()
    {
        Expiration = TimeSpan.FromMinutes(expirationMinutes),
        LocalCacheExpiration = TimeSpan.FromMinutes(
            Math.Min(expirationMinutes, _defaultLocalTtlMinutes))
    };

    // ─── Set ─────────────────────────────────────────────────────────────────

    public async Task SetAsync(string key, string value, IEnumerable<string> tags, int expirationMinutes, CancellationToken ct = default)
    {
        var tagList = NormalizeTags(tags);

        await _cache.SetAsync(key, value, MakeOptions(expirationMinutes), tagList, ct);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(expirationMinutes);

        _metadata[key] = new CacheEntryMetadata
        {
            Key = key,
            Value = value,
            Tags = tagList,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        RegisterKey(key, tagList, now);
        RegisterTags(tagList, key, now);
    }

    // ─── Get / GetOrCreate ────────────────────────────────────────────────────

    public async Task<CacheGetResult> GetOrCreateAsync(string key, CancellationToken ct = default)
    {
        var factoryRan = false;
        string? factoryLabel = null;

        var value = await _cache.GetOrCreateAsync(key, innerCt =>
        {
            factoryRan = true;
            Interlocked.Increment(ref _factoryInvocations);

            var (label, json) = RandomDataFactory.Generate();
            factoryLabel = label;

            return ValueTask.FromResult(json);
        }, cancellationToken: ct);

        var isHit = !factoryRan && value is not null;
        var now = DateTimeOffset.UtcNow;

        if (isHit)
        {
            Interlocked.Increment(ref _hits);

            if (_metadata.TryGetValue(key, out var meta))
                meta.LastAccessedAt = now;

            if (_keyRegistry.TryGetValue(key, out var keyEntry))
                Interlocked.Increment(ref keyEntry.Hits);
        }
        else
        {
            Interlocked.Increment(ref _misses);

            if (_keyRegistry.TryGetValue(key, out var keyEntry))
                Interlocked.Increment(ref keyEntry.Misses);

            if (factoryRan && value is not null)
            {
                // Cache the factory-generated entry in our metadata tracker
                var autoTags = new List<string> { "factory-generated", NormalizeTag(factoryLabel ?? "unknown") };

                _metadata[key] = new CacheEntryMetadata
                {
                    Key = key,
                    Value = value,
                    Tags = autoTags,
                    CreatedAt = now,
                    ExpiresAt = now.AddMinutes(_defaultTtlMinutes),
                    FactoryGenerated = true,
                    FactoryLabel = factoryLabel
                };

                RegisterKey(key, autoTags, now);
                RegisterTags(autoTags, key, now);
            }
            else if (factoryRan)
            {
                _metadata.TryRemove(key, out _);
            }
        }

        return new CacheGetResult
        {
            Key = key,
            Value = value,
            IsHit = isHit,
            HasResult = value is not null,
            FactoryLabel = factoryLabel
        };
    }

    // ─── Stampede test ───────────────────────────────────────────────────────

    public async Task<StampedeResult> RunStampedeTestAsync(string key, int concurrency, bool forceEvict, CancellationToken ct = default)
    {
        if (forceEvict)
        {
            await _cache.RemoveAsync(key, ct);
            _metadata.TryRemove(key, out _);
            if (_keyRegistry.TryGetValue(key, out var k)) k.IsCurrentlyActive = false;
        }

        var localFactoryHits = 0;
        string? capturedValue = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            _cache.GetOrCreateAsync(key, async innerCt =>
            {
                Interlocked.Increment(ref localFactoryHits);
                Interlocked.Increment(ref _factoryInvocations);

                // 50ms delay widens the coalescing window so the test is meaningful
                await Task.Delay(50, innerCt);

                var (_, json) = RandomDataFactory.Generate();
                capturedValue = json;
                return json;
            }, cancellationToken: ct).AsTask()
        ).ToList();

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        var successCount = results.Count(v => v is not null);
        var now = DateTimeOffset.UtcNow;

        if (localFactoryHits > 0 && capturedValue is not null)
        {
            var tags = new List<string> { "factory-generated", "stampede-test" };

            _metadata[key] = new CacheEntryMetadata
            {
                Key = key,
                Value = capturedValue,
                Tags = tags,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(_defaultTtlMinutes),
                FactoryGenerated = true,
                FactoryLabel = "Stampede"
            };

            RegisterKey(key, tags, now);
            RegisterTags(tags, key, now);
        }

        // 1 miss (factory ran) + (successCount - 1) hits coalesced by HybridCache
        if (localFactoryHits > 0)
        {
            Interlocked.Increment(ref _misses);
            var coalesced = successCount - 1;
            if (coalesced > 0) Interlocked.Add(ref _hits, coalesced);
        }
        else
        {
            Interlocked.Add(ref _hits, successCount);
        }

        return new StampedeResult
        {
            Key = key,
            RequestCount = concurrency,
            FactoryInvocations = localFactoryHits,
            SuccessfulResponses = successCount,
            ElapsedMs = sw.ElapsedMilliseconds,
            SampleValue = capturedValue ?? results.FirstOrDefault(v => v is not null)
        };
    }

    // ─── Remove ───────────────────────────────────────────────────────────────

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(key, ct);
        _metadata.TryRemove(key, out _);

        if (_keyRegistry.TryGetValue(key, out var entry))
            entry.IsCurrentlyActive = false;
    }

    public async Task<int> RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        var normalizedTag = NormalizeTag(tag);
        await _cache.RemoveByTagAsync(normalizedTag, ct);

        var removed = 0;
        foreach (var kvp in _metadata)
        {
            // Exact match — safe because all tags are normalized on write
            if (kvp.Value.Tags.Contains(normalizedTag))
            {
                if (_metadata.TryRemove(kvp.Key, out _))
                {
                    removed++;
                    if (_keyRegistry.TryGetValue(kvp.Key, out var keyEntry))
                        keyEntry.IsCurrentlyActive = false;
                }
            }
        }
        return removed;
    }

    // ─── Queries ──────────────────────────────────────────────────────────────

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
            ActiveEntries = all.Count(e => !e.IsExpired),
            TotalUniqueKeys = _keyRegistry.Count,
            TotalUniqueTags = _tagRegistry.Count,
            FactoryInvocations = Interlocked.Read(ref _factoryInvocations)
        };
    }

    public IReadOnlyList<KeyRegistryEntry> GetKeyRegistry()
    {
        var activeKeys = _metadata.Keys.ToHashSet();

        foreach (var entry in _keyRegistry.Values)
            entry.IsCurrentlyActive = activeKeys.Contains(entry.Key);

        return _keyRegistry.Values.OrderByDescending(e => e.LastSeen).ToList();
    }

    public IReadOnlyList<TagRegistryEntry> GetTagRegistry()
    {
        // Single pass over active metadata to build tag → count map.
        // O(entries × avg-tags-per-entry) instead of O(tags × entries).
        var activeTagCounts = new Dictionary<string, int>();
        foreach (var meta in _metadata.Values)
        {
            if (meta.IsExpired) continue;
            foreach (var tag in meta.Tags)
                activeTagCounts[tag] = activeTagCounts.GetValueOrDefault(tag) + 1;
        }

        foreach (var entry in _tagRegistry.Values)
            entry.ActiveEntries = activeTagCounts.GetValueOrDefault(entry.Tag, 0);

        return _tagRegistry.Values.OrderByDescending(e => e.TimesUsed).ToList();
    }

    public void PruneExpired()
    {
        foreach (var kvp in _metadata.Where(k => k.Value.IsExpired).ToList())
        {
            _metadata.TryRemove(kvp.Key, out _);
            if (_keyRegistry.TryGetValue(kvp.Key, out var entry))
                entry.IsCurrentlyActive = false;
        }
    }

    // ─── Private registry helpers ─────────────────────────────────────────────

    private void RegisterKey(string key, List<string> tags, DateTimeOffset now)
    {
        _keyRegistry.AddOrUpdate(key,
            _ => new KeyRegistryEntry
            {
                Key = key,
                FirstSeen = now,
                LastSeen = now,
                TimesSet = 1,
                LastKnownTags = tags,
                IsCurrentlyActive = true
            },
            (_, existing) =>
            {
                existing.LastSeen = now;
                existing.TimesSet++;
                existing.LastKnownTags = tags;
                existing.IsCurrentlyActive = true;
                return existing;
            });
    }

    private void RegisterTags(List<string> tags, string key, DateTimeOffset now)
    {
        foreach (var tag in tags)
        {
            _tagRegistry.AddOrUpdate(tag,
                _ => new TagRegistryEntry
                {
                    Tag = tag,
                    TimesUsed = 1,
                    FirstSeen = now,
                    LastSeen = now,
                    KnownKeys = [key]
                },
                (_, existing) =>
                {
                    existing.TimesUsed++;
                    existing.LastSeen = now;
                    if (!existing.KnownKeys.Contains(key))
                        existing.KnownKeys.Add(key);
                    return existing;
                });
        }
    }
}
