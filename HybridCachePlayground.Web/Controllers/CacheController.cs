using HybridCachePlayground.Web.Models;
using HybridCachePlayground.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HybridCachePlayground.Web.Controllers;

public class CacheController : Controller
{
    private readonly ICachePlaygroundService _cacheService;

    public CacheController(ICachePlaygroundService cacheService)
    {
        _cacheService = cacheService;
    }

    // ─── Set ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Set() => View(new CacheSetRequest { ExpirationMinutes = 5 });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Set(CacheSetRequest model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await _cacheService.SetAsync(model.Key, model.Value, model.ParsedTags, model.ExpirationMinutes);

        TempData["Message"] = $"Entry '{model.Key}' stored successfully with {model.ParsedTags.Count} tag(s).";
        TempData["MessageType"] = "success";
        return RedirectToAction(nameof(Set));
    }

    // ─── Get ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Get() => View(new CacheGetRequest());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Get(CacheGetRequest model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _cacheService.GetOrCreateAsync(model.Key);
        ViewData["Result"] = result;
        return View(model);
    }

    // ─── Remove by key ───────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Remove() => View(new CacheRemoveRequest());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(CacheRemoveRequest model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await _cacheService.RemoveAsync(model.Key);

        TempData["Message"] = $"Entry '{model.Key}' removed from cache.";
        TempData["MessageType"] = "success";
        return RedirectToAction(nameof(Remove));
    }

    // ─── Remove by tag ───────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult RemoveByTag() => View(new CacheRemoveByTagRequest());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveByTag(CacheRemoveByTagRequest model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var count = await _cacheService.RemoveByTagAsync(model.Tag);

        TempData["Message"] = $"Removed {count} tracked entry/entries with tag '{model.Tag}'.";
        TempData["MessageType"] = count > 0 ? "success" : "warning";
        return RedirectToAction(nameof(RemoveByTag));
    }
}
