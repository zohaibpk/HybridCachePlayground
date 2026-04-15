using System.ComponentModel.DataAnnotations;

namespace HybridCachePlayground.Web.Models;

public class CacheGetRequest
{
    [Required(ErrorMessage = "Key is required")]
    public string Key { get; set; } = string.Empty;
}
