using System.Diagnostics;
using HybridCachePlayground.Web.Models;
using HybridCachePlayground.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HybridCachePlayground.Web.Controllers;

public class HomeController : Controller
{
    private readonly ICachePlaygroundService _cacheService;

    public HomeController(ICachePlaygroundService cacheService)
    {
        _cacheService = cacheService;
    }

    public IActionResult Index()
    {
        var vm = new DashboardViewModel
        {
            Entries = _cacheService.GetAllEntries().ToList(),
            Stats = _cacheService.GetStats()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickRemove(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            await _cacheService.RemoveAsync(key);

        TempData["Message"] = $"Entry '{key}' removed.";
        TempData["MessageType"] = "success";
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
