using System.Collections.Concurrent;
using HybridCachePlayground.Web.Models;
using Microsoft.Extensions.Caching.Hybrid;

namespace HybridCachePlayground.Web.Services;

public sealed class CachePlaygroundService : ICachePlaygroundService
{
    private readonly HybridCache _cache;

    // Active entries (pruned on expiry)
    private readonly ConcurrentDictionary<string, CacheEntryMetadata> _metadata = new();

    // All-time registries (never cleared, survive eviction)
    private readonly ConcurrentDictionary<string, KeyRegistryEntry> _keyRegistry = new();
    private readonly ConcurrentDictionary<string, TagRegistryEntry> _tagRegistry = new();

    private long _hits;
    private long _misses;
    private long _factoryInvocations;

    public CachePlaygroundService(HybridCache cache)
    {
        _cache = cache;
    }

    // ─── Set ─────────────────────────────────────────────────────────────────

    public async Task SetAsync(string key, string value, IEnumerable<string> tags, int expirationMinutes, CancellationToken ct = default)
    {
        var tagList = tags.ToList();
        var options = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(expirationMinutes),
            LocalCacheExpiration = TimeSpan.FromMinutes(Math.Min(expirationMinutes, 2))
        };

        await _cache.SetAsync(key, value, options, tagList, ct);

        var now = DateTimeOffset.UtcNow;

        _metadata[key] = new CacheEntryMetadata
        {
            Key = key,
            Value = value,
            Tags = tagList,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(expirationMinutes)
        };

        // Update key registry
        _keyRegistry.AddOrUpdate(key,
            _ => new KeyRegistryEntry
            {
                Key = key,
                FirstSeen = now,
                LastSeen = now,
                TimesSet = 1,
                LastKnownTags = tagList,
                IsCurrentlyActive = true
            },
            (_, existing) =>
            {
                existing.LastSeen = now;
                existing.TimesSet++;
                existing.LastKnownTags = tagList;
                existing.IsCurrentlyActive = true;
                return existing;
            });

        // Update tag registry
        foreach (var tag in tagList)
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

    // ─── Get / GetOrCreate ────────────────────────────────────────────────────

    public async Task<CacheGetResult> GetOrCreateAsync(string key, CancellationToken ct = default)
    {
        var factoryRan = false;
        string? factoryLabel = null;

        var value = await _cache.GetOrCreateAsync(key, async innerCt =>
        {
            factoryRan = true;
            Interlocked.Increment(ref _factoryInvocations);

            var (label, json) = RandomDataFactory.Generate();
            factoryLabel = label;

            await Task.CompletedTask;
            return json;
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

            // Factory ran — store the generated value in our metadata
            if (factoryRan && value is not null)
            {
                var defaultTtl = 5;
                _metadata[key] = new CacheEntryMetadata
                {
                    Key = key,
                    Value = value,
                    Tags = ["factory-generated", factoryLabel?.ToLower() ?? "unknown"],
                    CreatedAt = now,
                    ExpiresAt = now.AddMinutes(defaultTtl),
                    FactoryGenerated = true,
                    FactoryLabel = factoryLabel
                };

                // Register key and auto-tags
                var autoTags = new List<string> { "factory-generated", factoryLabel?.ToLower() ?? "unknown" };

                _keyRegistry.AddOrUpdate(key,
                    _ => new KeyRegistryEntry
                    {
                        Key = key,
                        FirstSeen = now,
                        LastSeen = now,
                        TimesSet = 1,
                        LastKnownTags = autoTags,
                        IsCurrentlyActive = true,
                        Misses = 1
                    },
                    (_, existing) =>
                    {
                        existing.LastSeen = now;
                        existing.TimesSet++;
                        existing.LastKnownTags = autoTags;
                        existing.IsCurrentlyActive = true;
                        return existing;
                    });

                foreach (var tag in autoTags)
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
            else if (factoryRan)
            {
                // Factory ran but returned null — key was never set
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

        // Local counter — counts how many times the factory actually executes in this test run
        var localFactoryHits = 0;
        string? capturedValue = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Fire all requests simultaneously
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            _cache.GetOrCreateAsync(key, async innerCt =>
            {
                Interlocked.Increment(ref localFactoryHits);
                Interlocked.Increment(ref _factoryInvocations);

                // Simulate a brief async factory (makes the coalescing visible)
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

        // Record in metadata if the factory ran and produced a value
        if (localFactoryHits > 0 && capturedValue is not null)
        {
            var tags = new List<string> { "factory-generated", "stampede-test" };
            _metadata[key] = new CacheEntryMetadata
            {
                Key = key,
                Value = capturedValue,
                Tags = tags,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(5),
                FactoryGenerated = true,
                FactoryLabel = "Stampede"
            };

            _keyRegistry.AddOrUpdate(key,
                _ => new KeyRegistryEntry { Key = key, FirstSeen = now, LastSeen = now, TimesSet = 1, LastKnownTags = tags, IsCurrentlyActive = true },
                (_, e) => { e.LastSeen = now; e.TimesSet++; e.LastKnownTags = tags; e.IsCurrentlyActive = true; return e; });

            foreach (var tag in tags)
                _tagRegistry.AddOrUpdate(tag,
                    _ => new TagRegistryEntry { Tag = tag, TimesUsed = 1, FirstSeen = now, LastSeen = now, KnownKeys = [key] },
                    (_, e) => { e.TimesUsed++; e.LastSeen = now; if (!e.KnownKeys.Contains(key)) e.KnownKeys.Add(key); return e; });
        }

        // Update hit/miss counters: 1 miss (the factory call) + (concurrency-1) hits
        if (localFactoryHits > 0)
        {
            Interlocked.Increment(ref _misses);
            var hitCount = successCount - 1;
            if (hitCount > 0) Interlocked.Add(ref _hits, hitCount);
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
        await _cache.RemoveByTagAsync(tag, ct);

        var removed = 0;
        foreach (var kvp in _metadata)
        {
            if (kvp.Value.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
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
        foreach (var entry in _tagRegistry.Values)
            entry.ActiveEntries = _metadata.Values.Count(m =>
                !m.IsExpired && m.Tags.Contains(entry.Tag, StringComparer.OrdinalIgnoreCase));

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
}
