using HybridCachePlayground.Web.Services;
using Microsoft.Extensions.Caching.Hybrid;

var builder = WebApplication.CreateBuilder(args);

// ─── Caching ──────────────────────────────────────────────────────────────────
// L1: IMemoryCache  (built-in, enabled automatically by AddHybridCache)
// L2: DistributedMemoryCache  — in-process, great for development and demos
//
// TO SWITCH TO NCACHE (distributed, multi-server):
//   1. dotnet add package Alachisoft.NCache.OpenSource.SDK
//   2. dotnet add package Alachisoft.NCache.Microsoft.Extensions.Caching
//   3. Replace the line below with:
//      builder.Services.AddNCacheDistributedCache(o =>
//          o.CacheName = builder.Configuration.GetConnectionString("NCache"));
//
// TO SWITCH TO SQL SERVER:
//   1. dotnet add package Microsoft.Extensions.Caching.SqlServer
//   2. Replace the line below with:
//      builder.Services.AddSqlServerCache(o => { o.ConnectionString = ...; o.SchemaName = "dbo"; o.TableName = "HybridCache"; });
builder.Services.AddDistributedMemoryCache();

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        // Default TTL for the combined (L1+L2) entry
        Expiration = TimeSpan.FromMinutes(
            builder.Configuration.GetValue("HybridCache:DefaultExpirationMinutes", 5)),

        // L1 (in-process) entry lives shorter — forces L2 read on cache-warm servers
        LocalCacheExpiration = TimeSpan.FromMinutes(
            builder.Configuration.GetValue("HybridCache:LocalCacheExpirationMinutes", 2))
    };
});

builder.Services.AddSingleton<ICachePlaygroundService, CachePlaygroundService>();

// ─── MVC ──────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
