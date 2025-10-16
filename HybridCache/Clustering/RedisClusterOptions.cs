namespace HybridCache.Clustering;

/// <summary>
/// Configuration options for Redis cluster support.
/// </summary>
public class RedisClusterOptions
{
    /// <summary>
    /// Gets or sets whether the Redis instance is running in cluster mode.
    /// Default is false.
    /// </summary>
    public bool IsClusterMode { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to use hash tags for Lua script key routing.
    /// When true, keys will be wrapped with hash tags {key} to ensure they route to the same slot.
    /// Default is true when IsClusterMode is true.
    /// </summary>
    public bool UseHashTags { get; set; } = true;

    /// <summary>
    /// Gets or sets the hash tag delimiter.
    /// Default is { and }.
    /// </summary>
    public string HashTagStart { get; set; } = "{";

    /// <summary>
    /// Gets or sets the hash tag end delimiter.
    /// </summary>
    public string HashTagEnd { get; set; } = "}";

    /// <summary>
    /// Gets or sets whether to validate that all keys in a Lua script map to the same hash slot.
    /// This is required for multi-key Lua scripts in cluster mode.
    /// Default is true.
    /// </summary>
    public bool ValidateHashSlots { get; set; } = true;

    /// <summary>
    /// Gets or sets the retry policy for cluster operations.
    /// </summary>
    public ClusterRetryPolicy RetryPolicy { get; set; } = new ClusterRetryPolicy();
}

/// <summary>
/// Retry policy for cluster operations.
/// </summary>
public class ClusterRetryPolicy
{
    /// <summary>
    /// Gets or sets the maximum number of retries for cluster operations.
    /// Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the initial retry delay.
    /// Default is 100ms.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the maximum retry delay.
    /// Default is 2 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets whether to use exponential backoff.
    /// Default is true.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;
}
