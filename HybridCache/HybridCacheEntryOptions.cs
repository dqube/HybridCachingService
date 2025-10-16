namespace HybridCache;

/// <summary>
/// Options for configuring cache entry behavior in the hybrid cache.
/// </summary>
public class HybridCacheEntryOptions
{
    /// <summary>
    /// Gets or sets the absolute expiration time relative to now.
    /// </summary>
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

    /// <summary>
    /// Gets or sets the absolute expiration time.
    /// </summary>
    public DateTimeOffset? AbsoluteExpiration { get; set; }

    /// <summary>
    /// Gets or sets the sliding expiration time.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>
    /// Gets or sets whether to store this entry in the L1 (in-memory) cache.
    /// Default is true.
    /// </summary>
    public bool UseL1Cache { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to store this entry in the L2 (distributed) cache.
    /// Default is true.
    /// </summary>
    public bool UseL2Cache { get; set; } = true;

    /// <summary>
    /// Gets or sets the local (L1) cache expiration time.
    /// If not set, uses the same expiration as the distributed cache.
    /// </summary>
    public TimeSpan? LocalCacheExpiration { get; set; }

    /// <summary>
    /// Creates a new instance with default settings.
    /// </summary>
    public static HybridCacheEntryOptions Default => new();

    /// <summary>
    /// Creates options with absolute expiration relative to now.
    /// </summary>
    public static HybridCacheEntryOptions WithAbsoluteExpiration(TimeSpan expiration) =>
        new() { AbsoluteExpirationRelativeToNow = expiration };

    /// <summary>
    /// Creates options with sliding expiration.
    /// </summary>
    public static HybridCacheEntryOptions WithSlidingExpiration(TimeSpan expiration) =>
        new() { SlidingExpiration = expiration };
}
