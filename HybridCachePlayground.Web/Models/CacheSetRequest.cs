using System.ComponentModel.DataAnnotations;

namespace HybridCachePlayground.Web.Models;

public class CacheSetRequest
{
    [Required(ErrorMessage = "Key is required")]
    [StringLength(200, MinimumLength = 1)]
    public string Key { get; set; } = string.Empty;

    [Required(ErrorMessage = "Value is required")]
    public string Value { get; set; } = string.Empty;

    /// <summary>Comma-separated list of tags, e.g. "users,tenant-1"</summary>
    public string? Tags { get; set; }

    [Range(1, 1440, ErrorMessage = "Expiration must be between 1 and 1440 minutes")]
    public int ExpirationMinutes { get; set; } = 5;

    public List<string> ParsedTags =>
        string.IsNullOrWhiteSpace(Tags)
            ? []
            : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
