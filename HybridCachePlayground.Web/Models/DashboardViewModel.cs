namespace HybridCachePlayground.Web.Models;

public class DashboardViewModel
{
    public List<CacheEntryMetadata> Entries { get; set; } = [];
    public CacheStats Stats { get; set; } = new();
}
