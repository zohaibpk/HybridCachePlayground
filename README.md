# HybridCache Playground

An interactive ASP.NET 8 MVC web application for exploring and testing **Microsoft HybridCache** (`Microsoft.Extensions.Caching.Hybrid`). Set, retrieve, and invalidate cache entries through a live browser UI — no Postman or test code required.

The project defaults to **IMemoryCache (L1) + DistributedMemoryCache (L2)** so it runs out of the box with zero infrastructure. The L2 backend is designed to be swapped for NCache, SQL Server, or any other `IDistributedCache` provider with a single config change.

---

## Table of Contents

- [What is HybridCache?](#what-is-hybridcache)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Project Structure](#project-structure)
- [Pages & Usage](#pages--usage)
  - [Dashboard](#dashboard)
  - [Set Entry](#set-entry)
  - [Get Entry](#get-entry)
  - [Remove by Key](#remove-by-key)
  - [Remove by Tag](#remove-by-tag)
  - [Stampede Test](#stampede-test)
- [Random Data Factory](#random-data-factory)
- [Key Registry](#key-registry)
- [Tag Registry](#tag-registry)
- [Configuration](#configuration)
- [Switching the L2 Cache Backend](#switching-the-l2-cache-backend)
  - [NCache](#ncache)
  - [SQL Server](#sql-server)
- [Architecture](#architecture)
  - [Cache Layers](#cache-layers)
  - [Service Layer](#service-layer)
  - [Metadata Tracking](#metadata-tracking)
- [HybridCache API Reference](#hybridcache-api-reference)
- [Multi-Server Testing](#multi-server-testing)
- [Roadmap](#roadmap)

---

## What is HybridCache?

`HybridCache` (introduced in .NET 9, available on .NET 8 via NuGet) is Microsoft's unified caching abstraction that combines two cache tiers:

| Tier | Type | Purpose |
|---|---|---|
| **L1** | `IMemoryCache` (in-process) | Fastest — sub-microsecond reads, no serialisation |
| **L2** | `IDistributedCache` (e.g. NCache, SQL Server) | Shared across servers, survives process restarts |

Key advantages over using `IMemoryCache` or `IDistributedCache` directly:

- **Stampede protection** — concurrent requests for the same key coalesce; the factory runs exactly once
- **Tag-based invalidation** — group entries under tags and evict them all with a single call
- **Unified API** — one interface for both cache tiers, one place to configure TTLs
- **Transparent fallback** — if L2 is unavailable, L1 still serves stale data

---

## Features

- **Interactive UI** — set, get, and remove cache entries directly in the browser
- **Hit / Miss detection** — every Get operation shows whether the value came from cache or the factory ran
- **Random data factory** — cache misses on the Get page auto-generate realistic JSON payloads (User, Product, Order, Session, Analytics, Employee) so you always have data to inspect
- **Tag management** — assign multiple tags per entry, then bulk-evict by tag
- **Live dashboard** — active entries with expandable values, tag count badges, source labels (manual vs factory), TTL countdown, and hit/miss stats
- **Key Registry** — all-time record of every key ever used: times set, hits, misses, per-key hit ratio, active status
- **Tag Registry** — all-time record of every tag ever used: usage count, active entry count, associated keys, quick evict button
- **Stampede protection test** — fire N concurrent requests for the same uncached key; proves the factory runs exactly once
- **Configurable TTLs** — per-entry expiration via the Set form; defaults configurable in `appsettings.json`
- **Swappable L2 backend** — replace one line in `Program.cs` to switch to NCache or SQL Server
- **Zero infrastructure** — runs entirely in-process by default, no external services needed

---

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 8.0 or later |
| Any modern browser | — |

No database, cache server, or Docker required for the default setup.

---

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/zohaibpk/HybridCachePlayground.git
cd HybridCachePlayground
```

### 2. Restore & build

```bash
dotnet build
```

### 3. Run

```bash
cd HybridCachePlayground.Web
dotnet run
```

### 4. Open in browser

Navigate to the URL shown in the terminal (typically `http://localhost:5280`).

---

## Project Structure

```
HybridCachePlayground/
├── HybridCachePlayground.sln
├── .gitignore
├── README.md
└── HybridCachePlayground.Web/
    ├── Controllers/
    │   ├── HomeController.cs              # Dashboard + quick-remove action
    │   └── CacheController.cs             # Set, Get, Remove, RemoveByTag, Stampede
    ├── Models/
    │   ├── CacheEntryMetadata.cs          # Active entry (key, value, tags, expiry, source)
    │   ├── CacheStats.cs                  # Hits, misses, hit ratio, unique keys/tags, factory count
    │   ├── CacheSetRequest.cs             # Form model for Set
    │   ├── CacheGetRequest.cs             # Form model for Get
    │   ├── CacheGetResult.cs              # Get result (hit/miss flag, value, factory label)
    │   ├── CacheRemoveRequest.cs          # Form model for Remove by key / by tag
    │   ├── DashboardViewModel.cs          # Entries + stats + key registry + tag registry
    │   ├── KeyRegistryEntry.cs            # All-time key record
    │   ├── TagRegistryEntry.cs            # All-time tag record
    │   ├── StampedeRequest.cs             # Stampede test form model
    │   └── StampedeResult.cs              # Stampede test result
    ├── Services/
    │   ├── ICachePlaygroundService.cs     # Service interface
    │   ├── CachePlaygroundService.cs      # HybridCache wrapper + metadata + registries
    │   └── RandomDataFactory.cs           # Random JSON generator (6 templates)
    ├── Views/
    │   ├── Home/
    │   │   └── Index.cshtml               # Dashboard
    │   ├── Cache/
    │   │   ├── Set.cshtml
    │   │   ├── Get.cshtml
    │   │   ├── Remove.cshtml
    │   │   ├── RemoveByTag.cshtml
    │   │   └── Stampede.cshtml            # Stampede protection test
    │   └── Shared/
    │       └── _Layout.cshtml             # Bootstrap 5 layout + nav
    ├── Program.cs                         # DI registration + middleware pipeline
    └── appsettings.json                   # TTL defaults
```

---

## Pages & Usage

### Dashboard

**Route:** `/`

The home page shows a full live snapshot of cache state.

**Stats row (7 cards):**

| Card | Description |
|---|---|
| Active Entries | Entries currently in cache and not expired |
| Cache Hits | Total successful cache reads across all Get operations |
| Cache Misses | Total factory invocations from Get operations |
| Hit Ratio | `Hits / (Hits + Misses) × 100` |
| Unique Keys (all-time) | Total distinct keys ever written in this session |
| Unique Tags (all-time) | Total distinct tags ever used in this session |
| Factory Invocations | Total times the random data factory has run |

**Active entries table:**

| Column | Description |
|---|---|
| Key | Cache key |
| Value | Collapsed by default — click **View** to expand the full JSON |
| Tags # | Badge showing how many tags the entry carries |
| Tags | Tag badges |
| Source | `manual` (Set page) or `factory · <Template>` (auto-generated on miss) |
| Created | Time the entry was stored |
| Expires In | Countdown — turns red when expired |
| Last Hit | Time of last cache read for this entry |
| Remove | Inline evict button |

Below the entry table, the page shows the **Key Registry** and **Tag Registry** (see below).

---

### Set Entry

**Route:** `/cache/set`

Stores a value in HybridCache via `SetAsync`.

| Field | Description |
|---|---|
| **Key** | Unique cache key (e.g. `user:42`, `product:sku-100`) |
| **Value** | Any string or JSON — stored as-is |
| **Tags** | Comma-separated tags (e.g. `users, tenant-1`) — used for bulk invalidation |
| **Expiration (minutes)** | L2 TTL. L1 TTL = `min(expiration, 2)` minutes |

**API called:**

```csharp
await hybridCache.SetAsync(
    key,
    value,
    new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(ttl),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    },
    tags: ["tag1", "tag2"]);
```

---

### Get Entry

**Route:** `/cache/get`

Retrieves a value using `GetOrCreateAsync`. The result page shows:

- **HIT** (green) — value was found in cache; factory did not run
- **MISS** (red) — value was not in cache; the **random data factory** ran and generated a new JSON payload, which is now cached and visible on the dashboard

**API called:**

```csharp
var value = await hybridCache.GetOrCreateAsync(
    key,
    async ct =>
    {
        // Called on cache miss only — generates random JSON
        var (_, json) = RandomDataFactory.Generate();
        return json;
    });
```

> On a miss the generated entry is automatically tagged `factory-generated` and the template label (e.g. `user`, `order`) so it can be bulk-evicted later.

---

### Remove by Key

**Route:** `/cache/remove`

Evicts a single entry from both L1 and L2.

**API called:**

```csharp
await hybridCache.RemoveAsync(key);
```

When using a distributed L2 backend (NCache, SQL Server), this eviction propagates to all connected server nodes.

---

### Remove by Tag

**Route:** `/cache/remove-by-tag`

Evicts **all** entries that were stored with the given tag, across both cache tiers.

**API called:**

```csharp
await hybridCache.RemoveByTagAsync(tag);
```

The result message shows how many tracked entries were evicted.

**Example tag patterns:**

| Tag | When to use |
|---|---|
| `tenant-5` | Evict all data for a specific tenant |
| `users` | Evict all user-related entries after a bulk update |
| `catalog` | Evict all product cache entries after a price change |
| `factory-generated` | Evict all auto-generated entries at once |
| `session-abc` | Evict all entries tied to a user session on logout |

---

### Stampede Test

**Route:** `/cache/stampede`  
**Nav:** ⚡ Stampede Test

Demonstrates HybridCache's built-in **stampede (cache stampede / thundering herd) protection**.

**How it works:**

1. Optionally evicts the key first to guarantee a cold cache miss
2. Creates N tasks, all calling `GetOrCreateAsync` for the same key simultaneously
3. All tasks are released at once via `Task.WhenAll`
4. The factory includes a 50ms artificial delay to widen the coalescing window
5. Results are displayed immediately after all tasks complete

**Result display:**

| Metric | Expected value |
|---|---|
| Concurrent Requests | Whatever you configured (2–100) |
| Factory Ran | **1** — HybridCache coalesces all concurrent misses |
| Successful Responses | Equal to Concurrent Requests |
| Total Time | ~50ms (factory delay), not N × 50ms |

A visual bar chart shows which request ran the factory (green) vs which ones waited for the result (blue).

**Why this matters:**

With plain `IMemoryCache`, if 50 requests arrive for an uncached key simultaneously, all 50 see a miss and invoke the factory — potentially firing 50 database queries. HybridCache uses a per-key lock so the factory runs once and all waiting callers receive the same result.

**API demonstrated:**

```csharp
// All 50 tasks call this simultaneously
var value = await hybridCache.GetOrCreateAsync(key, async ct =>
{
    // HybridCache guarantees this runs exactly once
    await Task.Delay(50, ct); // simulates real async work
    return await FetchFromDatabase(ct);
});
```

---

## Random Data Factory

**File:** `Services/RandomDataFactory.cs`

When a cache miss occurs on the Get page or during a Stampede test, the random data factory generates a realistic JSON payload instead of returning null. This means you always have inspectable data without needing a real backend.

**6 templates — 3 to 5 fields randomly selected per call:**

| Template | Example Fields |
|---|---|
| **User** | `id`, `firstName`, `lastName`, `email`, `role`, `department`, `status` |
| **Product** | `id`, `name`, `price`, `category`, `stock`, `status`, `tier` |
| **Order** | `orderId`, `customerId`, `total`, `status`, `createdAt`, `itemCount`, `priority` |
| **Session** | `sessionId`, `userId`, `ipAddress`, `expiresAt`, `role`, `country` |
| **Analytics** | `eventId`, `userId`, `action`, `city`, `country`, `timestamp` |
| **Employee** | `id`, `firstName`, `lastName`, `department`, `salary`, `createdAt`, `status` |

**Example output (User template, 4 fields):**

```json
{
  "id": 4821,
  "firstName": "Alice",
  "role": "Developer",
  "department": "Engineering"
}
```

Factory-generated entries are automatically tagged `factory-generated` and the lowercase template name (e.g. `user`, `order`), making them easy to bulk-evict via Remove by Tag.

---

## Key Registry

Displayed on the Dashboard below the active entries table.

The Key Registry is a **permanent in-memory record** of every cache key ever written during the current session. It persists through cache evictions and TTL expiry — a key that has expired from the cache still appears in the registry.

| Column | Description |
|---|---|
| Key | The cache key |
| Times Set | How many times `SetAsync` or the factory wrote this key |
| Hits | Number of successful cache reads for this key |
| Misses | Number of factory invocations for this key |
| Hit % | Per-key hit ratio |
| Last Known Tags | Tags from the most recent write |
| First Seen | When the key was first written |
| Last Seen | When the key was most recently written or read |
| Active | Whether the key currently exists in the cache |

---

## Tag Registry

Displayed on the Dashboard below the Key Registry.

The Tag Registry is a **permanent in-memory record** of every tag ever used during the current session.

| Column | Description |
|---|---|
| Tag | The tag name |
| Times Used | How many `SetAsync` calls included this tag |
| Active Entries | How many currently-active cache entries carry this tag |
| Associated Keys | Keys that have ever been stored with this tag |
| First Seen | When the tag was first used |
| Last Seen | When the tag was most recently used |
| Evict button | Quick link to Remove by Tag pre-filled with this tag |

---

## Configuration

`appsettings.json`:

```json
{
  "HybridCache": {
    "DefaultExpirationMinutes": 5,
    "LocalCacheExpirationMinutes": 2
  }
}
```

| Key | Default | Description |
|---|---|---|
| `DefaultExpirationMinutes` | `5` | Default L2 (and combined) TTL for all entries |
| `LocalCacheExpirationMinutes` | `2` | Default L1 (in-process) TTL — should be ≤ `DefaultExpirationMinutes` |

Per-entry TTL can be overridden on the Set form. Factory-generated entries always use the default TTL.

---

## Switching the L2 Cache Backend

The L2 backend is registered in a single line in `Program.cs`. Replacing it requires no other code changes.

### NCache

NCache is a distributed in-memory cache with native .NET support, suitable for multi-server and high-throughput scenarios.

**1. Add packages:**

```bash
dotnet add package Alachisoft.NCache.OpenSource.SDK
dotnet add package Alachisoft.NCache.Microsoft.Extensions.Caching
```

**2. Update `Program.cs`** — replace `AddDistributedMemoryCache()` with:

```csharp
builder.Services.AddNCacheDistributedCache(o =>
    o.CacheName = builder.Configuration.GetConnectionString("NCache"));
```

**3. Add connection string to `appsettings.json`:**

```json
{
  "ConnectionStrings": {
    "NCache": "myCache"
  }
}
```

> Ensure the NCache server is running and the named cache (`myCache`) is created before starting the app.

---

### SQL Server

**1. Add package:**

```bash
dotnet add package Microsoft.Extensions.Caching.SqlServer
```

**2. Update `Program.cs`** — replace `AddDistributedMemoryCache()` with:

```csharp
builder.Services.AddSqlServerCache(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("SqlCache");
    o.SchemaName = "dbo";
    o.TableName = "HybridCache";
});
```

**3. Provision the cache table:**

```bash
dotnet tool install --global dotnet-sql-cache
dotnet sql-cache create "<connection-string>" dbo HybridCache
```

---

## Architecture

### Cache Layers

```
Request
   │
   ▼
┌─────────────────────┐
│   HybridCache API   │  GetOrCreateAsync / SetAsync / RemoveAsync / RemoveByTagAsync
└────────┬────────────┘
         │
    ┌────▼────┐         ┌─────────────────────────────────┐
    │   L1    │◄────────│  IMemoryCache (in-process)      │  Sub-microsecond reads
    └────┬────┘         │  TTL: LocalCacheExpiration       │  No serialisation overhead
         │ MISS         └─────────────────────────────────┘
    ┌────▼────┐         ┌─────────────────────────────────┐
    │   L2    │◄────────│  IDistributedCache              │  Shared across servers
    └────┬────┘         │  Default: DistributedMemoryCache │  Survives process restarts
         │ MISS         │  Swap: NCache / SQL Server       │  (when using external L2)
    ┌────▼────┐         └─────────────────────────────────┘
    │ Factory │  RandomDataFactory.Generate() or your real data source
    └─────────┘
```

### Service Layer

`CachePlaygroundService` is a singleton that wraps `HybridCache` and exposes clean methods to the controllers:

| Method | Description |
|---|---|
| `SetAsync` | Stores a value with tags and TTL; updates key/tag registries |
| `GetOrCreateAsync` | Retrieves a value; invokes `RandomDataFactory` on miss; records hit/miss |
| `RemoveAsync` | Evicts a single key; marks it inactive in the key registry |
| `RemoveByTagAsync` | Evicts all keys with a given tag; returns count of evicted entries |
| `RunStampedeTestAsync` | Fires N concurrent `GetOrCreateAsync` calls; reports factory invocation count |
| `GetAllEntries` | Returns active metadata (pruned of expired entries) |
| `GetStats` | Returns hits, misses, hit ratio, unique key/tag counts, factory invocations |
| `GetKeyRegistry` | Returns all-time key history |
| `GetTagRegistry` | Returns all-time tag history with live active-entry counts |
| `PruneExpired` | Removes expired entries from the metadata store |

### Metadata Tracking

HybridCache does not expose a "list all keys" API. The service maintains three in-memory stores:

| Store | Type | Lifetime | Purpose |
|---|---|---|---|
| `_metadata` | `ConcurrentDictionary<string, CacheEntryMetadata>` | Cleared on expiry/remove | Powers the active entries table |
| `_keyRegistry` | `ConcurrentDictionary<string, KeyRegistryEntry>` | Session-lived (never cleared) | Powers the Key Registry table |
| `_tagRegistry` | `ConcurrentDictionary<string, TagRegistryEntry>` | Session-lived (never cleared) | Powers the Tag Registry table |

All three stores reset on app restart. If you switch to an external L2 backend, previously cached values survive a restart but the in-memory tracking does not.

---

## HybridCache API Reference

| Method | Description |
|---|---|
| `GetOrCreateAsync<T>(key, factory, options?, tags?, ct)` | Get from cache or invoke factory on miss. Stampede-safe. |
| `SetAsync<T>(key, value, options?, tags?, ct)` | Store a value explicitly, bypassing the factory pattern. |
| `RemoveAsync(key, ct)` | Evict a single entry from L1 and L2. |
| `RemoveByTagAsync(tag, ct)` | Evict all entries associated with a tag from L1 and L2. |

`HybridCacheEntryOptions`:

| Property | Description |
|---|---|
| `Expiration` | Total TTL — applies to L2 (and overall entry lifetime) |
| `LocalCacheExpiration` | L1 TTL — should be ≤ `Expiration`. Shorter = fresher data on multi-server setups |

---

## Multi-Server Testing

To observe HybridCache behaviour across multiple server instances:

1. Switch the L2 backend to **NCache** (see [Switching the L2 Cache Backend](#switching-the-l2-cache-backend))
2. Run two instances of the app on different ports:
   ```bash
   dotnet run --urls "http://localhost:5280"  # Terminal 1
   dotnet run --urls "http://localhost:5281"  # Terminal 2
   ```
3. Set an entry on instance 1 — it lands in NCache (L2)
4. Get the same key on instance 2 — L1 is cold, but L2 serves the value (hit)
5. Remove by tag on instance 1 — the eviction propagates via NCache; instance 2's L1 is also invalidated on next access
6. Use the **Stampede Test** on both instances with the same key to verify coalescing works across the shared L2

With `DistributedMemoryCache` (default), L2 is in-process per instance — cross-instance invalidation is not supported. This is intentional for local development.

---

## Roadmap

- [ ] Operation log — live feed of every Set/Get/Remove with timestamp, result, and duration
- [ ] Seed data button — one-click bulk-load of a predefined dataset with varied tags
- [ ] Sliding expiry support — toggle between absolute and sliding TTL on the Set form
- [ ] NCache integration guide with Docker Compose setup
- [ ] Expiry auto-refresh on the dashboard (JavaScript polling)
- [ ] API controller — JSON endpoints exposing all cache operations for programmatic testing
