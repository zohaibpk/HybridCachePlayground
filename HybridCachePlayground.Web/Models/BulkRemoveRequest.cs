using System.ComponentModel.DataAnnotations;

namespace HybridCachePlayground.Web.Models;

public class BulkRemoveRequest : IValidatableObject
{
    /// <summary>Remove all registry keys that start with this prefix.</summary>
    public string? KeyPrefix { get; set; }

    /// <summary>Comma- or newline-separated explicit keys to remove.</summary>
    public string? Keys { get; set; }

    public List<string> ParsedExplicitKeys =>
        string.IsNullOrWhiteSpace(Keys)
            ? []
            : Keys.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(KeyPrefix) && string.IsNullOrWhiteSpace(Keys))
            yield return new ValidationResult(
                "Enter a key prefix or at least one explicit key.",
                [nameof(KeyPrefix), nameof(Keys)]);
    }
}
