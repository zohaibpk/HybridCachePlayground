using System.Diagnostics;
using HybridCachePlayground.Web.Models;
using HybridCachePlayground.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HybridCachePlayground.Web.Controllers;

public class HomeController : Controller
{
    private readonly ICachePlaygroundService _cacheService;
    private readonly INotificationService _notifications;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        ICachePlaygroundService cacheService,
        INotificationService notifications,
        ILogger<HomeController> logger)
    {
        _cacheService  = cacheService;
        _notifications = notifications;
        _logger        = logger;
    }

    public IActionResult Index()
    {
        var recent = _notifications.GetRecent(1000);
        var vm = new DashboardViewModel
        {
            Entries              = _cacheService.GetAllEntries().ToList(),
            Stats                = _cacheService.GetStats(),
            KeyRegistry          = _cacheService.GetKeyRegistry().ToList(),
            TagRegistry          = _cacheService.GetTagRegistry().ToList(),
            TotalNotifications   = recent.Count,
            UnreadNotifications  = _notifications.GetUnreadCount()
        };

        _logger.LogDebug(
            "Dashboard loaded | ActiveEntries: {Active} | Hits: {Hits} | Misses: {Misses} | HitRatio: {Ratio}%",
            vm.Stats.ActiveEntries, vm.Stats.Hits, vm.Stats.Misses, vm.Stats.HitRatio);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickRemove(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _logger.LogInformation("Quick remove | Key: {Key}", key);
            await _cacheService.RemoveAsync(key);
        }

        TempData["Message"] = $"Entry '{key}' removed.";
        TempData["MessageType"] = "success";
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
