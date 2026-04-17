using HybridCachePlayground.Web.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Serilog;

// ─── Bootstrap Serilog early so startup errors are captured ──────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Log.Information("HybridCache Playground starting up");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ─── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ─── Caching ──────────────────────────────────────────────────────────────
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
            Expiration = TimeSpan.FromMinutes(
                builder.Configuration.GetValue("HybridCache:DefaultExpirationMinutes", 5)),
            LocalCacheExpiration = TimeSpan.FromMinutes(
                builder.Configuration.GetValue("HybridCache:LocalCacheExpirationMinutes", 2))
        };
    });

    builder.Services.AddSingleton<ICachePlaygroundService, CachePlaygroundService>();

    // ─── MVC ──────────────────────────────────────────────────────────────────
    builder.Services.AddControllersWithViews();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    // Serilog HTTP request logging — replaces default ASP.NET request logs
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";
    });

    app.UseRouting();
    app.UseAuthorization();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
