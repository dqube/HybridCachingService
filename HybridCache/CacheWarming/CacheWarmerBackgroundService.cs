using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridCache.CacheWarming;

/// <summary>
/// Background service that periodically warms the L1 cache from L2 cache.
/// </summary>
public class CacheWarmerBackgroundService : BackgroundService
{
    private readonly ICacheWarmer _cacheWarmer;
    private readonly CacheWarmerOptions _options;
    private readonly ILogger<CacheWarmerBackgroundService> _logger;
    private CacheWarmingResult? _lastResult;
    private DateTimeOffset? _lastRun;
    private DateTimeOffset? _nextRun;

    public CacheWarmerBackgroundService(
        ICacheWarmer cacheWarmer,
        IOptions<CacheWarmerOptions> options,
        ILogger<CacheWarmerBackgroundService> logger)
    {
        _cacheWarmer = cacheWarmer;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets the result of the last warming operation.
    /// </summary>
    public CacheWarmingResult? LastResult => _lastResult;

    /// <summary>
    /// Gets the timestamp of the last warming operation.
    /// </summary>
    public DateTimeOffset? LastRun => _lastRun;

    /// <summary>
    /// Gets the scheduled time for the next warming operation.
    /// </summary>
    public DateTimeOffset? NextRun => _nextRun;

    /// <summary>
    /// Gets whether auto warming is enabled.
    /// </summary>
    public bool IsEnabled => _options.EnableAutoWarming;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableAutoWarming)
        {
            _logger.LogInformation("Cache warming is disabled. Background service will not run.");
            return;
        }

        _logger.LogInformation(
            "Cache warming background service started. Initial delay: {InitialDelay}, Interval: {Interval}",
            _options.InitialDelay, _options.WarmingInterval);

        // Wait for initial delay
        try
        {
            await Task.Delay(_options.InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cache warming background service stopped during initial delay");
            return;
        }

        // Run warming loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _lastRun = DateTimeOffset.UtcNow;
                _nextRun = _lastRun.Value.Add(_options.WarmingInterval);

                _logger.LogInformation("Starting cache warming cycle at {Time}", _lastRun);

                _lastResult = await _cacheWarmer.WarmCacheAsync(stoppingToken);

                if (_lastResult.Errors.Count > 0)
                {
                    _logger.LogWarning(
                        "Cache warming completed with {ErrorCount} errors. Loaded: {Loaded}, Skipped: {Skipped}, Duration: {Duration}ms",
                        _lastResult.Errors.Count, _lastResult.KeysLoaded, _lastResult.KeysSkipped, _lastResult.Duration.TotalMilliseconds);
                }
                else
                {
                    _logger.LogInformation(
                        "Cache warming completed successfully. Loaded: {Loaded}, Skipped: {Skipped}, Scanned: {Scanned}, Duration: {Duration}ms",
                        _lastResult.KeysLoaded, _lastResult.KeysSkipped, _lastResult.KeysScanned, _lastResult.Duration.TotalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during cache warming cycle");

                _lastResult = new CacheWarmingResult
                {
                    Errors = new List<string> { $"Unhandled error: {ex.Message}" }
                };
            }

            // Wait for next cycle
            try
            {
                _logger.LogDebug("Next cache warming scheduled at {NextRun}", _nextRun);
                await Task.Delay(_options.WarmingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Cache warming background service stopped");
                break;
            }
        }
    }

    /// <summary>
    /// Manually trigger a cache warming operation.
    /// </summary>
    public async Task<CacheWarmingResult> TriggerWarmingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Manual cache warming triggered");

        _lastRun = DateTimeOffset.UtcNow;
        _lastResult = await _cacheWarmer.WarmCacheAsync(cancellationToken);

        return _lastResult;
    }

    /// <summary>
    /// Gets statistics about the background service.
    /// </summary>
    public object GetStatistics()
    {
        return new
        {
            enabled = IsEnabled,
            lastRun = _lastRun,
            nextRun = _nextRun,
            lastResult = _lastResult == null ? null : new
            {
                keysScanned = _lastResult.KeysScanned,
                keysLoaded = _lastResult.KeysLoaded,
                keysSkipped = _lastResult.KeysSkipped,
                duration = _lastResult.Duration.TotalMilliseconds,
                completedAt = _lastResult.CompletedAt,
                errorCount = _lastResult.Errors.Count,
                errors = _lastResult.Errors
            },
            configuration = new
            {
                warmingInterval = _options.WarmingInterval.TotalSeconds,
                initialDelay = _options.InitialDelay.TotalSeconds,
                maxKeysPerWarming = _options.MaxKeysPerWarming,
                batchSize = _options.BatchSize,
                includePatterns = _options.IncludePatterns,
                excludePatterns = _options.ExcludePatterns
            }
        };
    }
}
