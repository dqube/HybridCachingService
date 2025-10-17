namespace HybridCache.CacheWarming;

/// <summary>
/// Defines a strategy for warming up the L1 (memory) cache from L2 (distributed) cache.
/// </summary>
public interface ICacheWarmer
{
    /// <summary>
    /// Warm up the L1 cache by fetching values from L2 based on the configured strategy.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Statistics about the warming operation</returns>
    Task<CacheWarmingResult> WarmCacheAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a cache warming operation.
/// </summary>
public class CacheWarmingResult
{
    /// <summary>
    /// Total number of keys scanned in L2 cache.
    /// </summary>
    public int KeysScanned { get; set; }

    /// <summary>
    /// Number of keys successfully loaded into L1 cache.
    /// </summary>
    public int KeysLoaded { get; set; }

    /// <summary>
    /// Number of keys that failed to load.
    /// </summary>
    public int KeysSkipped { get; set; }

    /// <summary>
    /// Time taken for the warming operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Timestamp when warming completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Any errors encountered during warming.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}
