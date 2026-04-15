using System.ComponentModel.DataAnnotations;

namespace HybridCachePlayground.Web.Models;

public class StampedeRequest
{
    [Required(ErrorMessage = "Key is required")]
    public string Key { get; set; } = "stampede-test";

    [Range(2, 100, ErrorMessage = "Concurrency must be between 2 and 100")]
    public int Concurrency { get; set; } = 20;

    /// <summary>When true the key is evicted from cache before firing, guaranteeing a cold start.</summary>
    public bool ForceEvict { get; set; } = true;
}
