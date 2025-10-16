namespace HybridCache;

/// <summary>
/// Configuration options for the hybrid cache.
/// </summary>
public class HybridCacheOptions
{
    /// <summary>
    /// Gets or sets the default expiration time for cache entries.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the default local (L1) cache expiration time.
    /// If not set, uses DefaultExpiration.
    /// </summary>
    public TimeSpan? DefaultLocalExpiration { get; set; }

    /// <summary>
    /// Gets or sets whether to enable the distributed (L2) cache.
    /// If false, only the in-memory cache will be used.
    /// Default is true.
    /// </summary>
    public bool EnableDistributedCache { get; set; } = true;

    /// <summary>
    /// Gets or sets the key prefix for all cache entries.
    /// Useful for multi-tenant scenarios or cache versioning.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Gets or sets whether to compress values before storing in distributed cache.
    /// Default is false.
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// Gets or sets the minimum size in bytes for compression to be applied.
    /// Default is 1024 bytes (1 KB).
    /// </summary>
    public int CompressionThreshold { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the instance name for the distributed cache.
    /// This will be prepended to all cache keys.
    /// </summary>
    public string? InstanceName { get; set; }
}
