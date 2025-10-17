namespace HybridCache.CacheWarming;

/// <summary>
/// Options for configuring the cache warming behavior.
/// </summary>
public class CacheWarmerOptions
{
    /// <summary>
    /// Enable or disable automatic cache warming.
    /// </summary>
    public bool EnableAutoWarming { get; set; } = true;

    /// <summary>
    /// Interval between cache warming operations.
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan WarmingInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Delay before starting the first warming operation.
    /// Default: 30 seconds (allows application to fully start)
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Key patterns to include in warming. If empty, all keys with the prefix will be warmed.
    /// Example: ["user:*", "product:*"]
    /// </summary>
    public string[] IncludePatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Key patterns to exclude from warming.
    /// Example: ["temp:*", "cache:temp:*"]
    /// </summary>
    public string[] ExcludePatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Maximum number of keys to warm in a single operation.
    /// Default: 1000 (prevents memory overflow)
    /// </summary>
    public int MaxKeysPerWarming { get; set; } = 1000;

    /// <summary>
    /// Batch size for fetching keys from Redis.
    /// Default: 100
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Timeout for each key fetch operation.
    /// Default: 5 seconds
    /// </summary>
    public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Expiration time for warmed entries in L1 cache.
    /// If null, uses the original L2 TTL (if available) or default expiration.
    /// </summary>
    public TimeSpan? L1Expiration { get; set; }

    /// <summary>
    /// Continue warming even if individual keys fail to load.
    /// Default: true
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>
    /// Log detailed information about warming operations.
    /// Default: false
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Only warm keys that match these prefixes (from HybridCacheOptions.KeyPrefix).
    /// If empty, uses the global KeyPrefix from HybridCacheOptions.
    /// </summary>
    public string[] KeyPrefixes { get; set; } = Array.Empty<string>();
}
