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
- **Tag management** — assign multiple tags per entry, then bulk-evict by tag
- **Live dashboard** — see all tracked entries, their tags, TTL countdown, and hit/miss counters
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
git clone https://github.com/<your-username>/HybridCachePlayground.git
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

Navigate to the URL shown in the terminal (typically `https://localhost:5001` or `http://localhost:5000`).

---

## Project Structure

```
HybridCachePlayground/
├── HybridCachePlayground.sln
├── .gitignore
├── README.md
└── HybridCachePlayground.Web/
    ├── Controllers/
    │   ├── HomeController.cs          # Dashboard + quick-remove action
    │   └── CacheController.cs         # Set, Get, Remove, RemoveByTag actions
    ├── Models/
    │   ├── CacheEntryMetadata.cs      # Tracked entry (key, value, tags, expiry)
    │   ├── CacheStats.cs              # Hit count, miss count, hit ratio
    │   ├── CacheSetRequest.cs         # Form model for Set
    │   ├── CacheGetRequest.cs         # Form model for Get
    │   ├── CacheGetResult.cs          # Result of a Get (hit/miss flag + value)
    │   ├── CacheRemoveRequest.cs      # Form model for Remove by key / by tag
    │   └── DashboardViewModel.cs      # Entries + stats for the dashboard view
    ├── Services/
    │   ├── ICachePlaygroundService.cs # Service interface
    │   └── CachePlaygroundService.cs  # HybridCache wrapper + metadata tracker
    ├── Views/
    │   ├── Home/
    │   │   └── Index.cshtml           # Dashboard
    │   ├── Cache/
    │   │   ├── Set.cshtml
    │   │   ├── Get.cshtml
    │   │   ├── Remove.cshtml
    │   │   └── RemoveByTag.cshtml
    │   └── Shared/
    │       └── _Layout.cshtml         # Bootstrap 5 layout + nav
    ├── Program.cs                     # DI registration + middleware pipeline
    └── appsettings.json               # TTL defaults
```

---

## Pages & Usage

### Dashboard

**Route:** `/`

The home page shows a live snapshot of all tracked cache entries:

- **Stats row** — active entry count, total hits, total misses, hit ratio percentage
- **Entry table** — key, value preview, tags (as badges), creation time, time-until-expiry, last accessed time
- **Inline Remove** — remove any individual entry without leaving the dashboard
- Expired entries are visually struck-through and pruned automatically on each load

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
- **MISS** (red) — value was not in cache; factory ran (returns `null` in this playground since there is no real data source)

**API called:**

```csharp
var value = await hybridCache.GetOrCreateAsync(
    key,
    async ct =>
    {
        // Only called on a cache miss
        // Stampede-safe: runs once even under concurrent requests
        return await FetchFromSource(ct);
    });
```

> **Stampede protection:** if 50 requests arrive for the same uncached key simultaneously, HybridCache serialises them — the factory runs exactly once and the result is shared with all waiting callers.

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

The dashboard count shows how many tracked entries were evicted.

**Example tag patterns:**

| Tag | When to use |
|---|---|
| `tenant-5` | Evict all data for a specific tenant |
| `users` | Evict all user-related entries after a bulk update |
| `catalog` | Evict all product cache entries after a price change |
| `session-abc` | Evict all entries tied to a user session on logout |

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

Per-entry TTL can be overridden on the Set form.

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
    │ Factory │  Value computed here, then populated into L2 and L1
    └─────────┘
```

### Service Layer

`CachePlaygroundService` is a singleton that wraps `HybridCache` and exposes clean methods to the controllers:

| Method | Description |
|---|---|
| `SetAsync` | Stores a value with tags and TTL |
| `GetOrCreateAsync` | Retrieves a value; detects and reports hit vs miss |
| `RemoveAsync` | Evicts a single key |
| `RemoveByTagAsync` | Evicts all keys with a given tag; returns count of evicted entries |
| `GetAllEntries` | Returns the tracked metadata list (for the dashboard) |
| `GetStats` | Returns hit/miss counts and active entry count |
| `PruneExpired` | Removes expired entries from the metadata store |

### Metadata Tracking

HybridCache does not expose a "list all keys" API. To power the dashboard, `CachePlaygroundService` maintains a `ConcurrentDictionary<string, CacheEntryMetadata>` in memory alongside the cache. This dictionary stores:

- Key, value, tags
- Creation time and expiry time
- Last accessed time (updated on cache hits)

Entries are pruned from the dictionary automatically when the dashboard loads or when they are explicitly removed. Note that the metadata dictionary is in-process — it resets on app restart, even if an external L2 cache retains the values.

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
   dotnet run --urls "https://localhost:5001"  # Terminal 1
   dotnet run --urls "https://localhost:5002"  # Terminal 2
   ```
3. Set an entry on instance 1 — it lands in NCache (L2)
4. Get the same key on instance 2 — L1 is cold, but L2 serves the value (hit)
5. Remove by tag on instance 1 — the eviction propagates via NCache; instance 2's L1 is also invalidated on next access

With `DistributedMemoryCache` (default), L2 is in-process per instance — cross-instance invalidation is not supported. This is intentional for local development.

---

## Roadmap

- [ ] Add an API controller exposing all operations as JSON endpoints
- [ ] NCache integration guide with Docker Compose setup
- [ ] Cache warm-up / pre-population script
- [ ] Expiry auto-refresh on the dashboard (JavaScript polling)
- [ ] Request timeline view — visualise stampede protection under concurrent load
