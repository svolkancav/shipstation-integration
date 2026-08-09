using System.ComponentModel.DataAnnotations;

namespace ShipStation.Integration.Configuration;

public sealed class ShipStationOptions
{
    public const string SectionName = "ShipStation";

    [Required]
    public Uri BaseAddress { get; set; } = new("https://ssapi.shipstation.com");

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// ShipStation allows 40 requests per minute per account. When the remaining
    /// quota drops to this value the transport starts pacing requests instead of
    /// waiting for a 429.
    /// </summary>
    [Range(0, 40)]
    public int RateLimitBuffer { get; set; } = 2;

    /// <summary>
    /// Upper bound on how long a single request will sit waiting for the rate
    /// limit window to reset before giving up.
    /// </summary>
    public TimeSpan MaxThrottleDelay { get; set; } = TimeSpan.FromSeconds(70);

    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}
