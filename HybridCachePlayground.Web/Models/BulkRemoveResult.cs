namespace HybridCachePlayground.Web.Models;

public class BulkRemoveResult
{
    public int Requested { get; set; }
    public int Removed { get; set; }
    public List<string> Keys { get; set; } = [];
    public long ElapsedMs { get; set; }
    public string? KeyPrefix { get; set; }
}
