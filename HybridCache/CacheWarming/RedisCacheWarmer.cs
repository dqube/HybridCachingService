using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HybridCache.CacheWarming;

/// <summary>
/// Implements cache warming by scanning Redis keys and loading them into memory cache.
/// </summary>
public class RedisCacheWarmer : ICacheWarmer
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly CacheWarmerOptions _options;
    private readonly HybridCacheOptions _cacheOptions;
    private readonly ILogger<RedisCacheWarmer> _logger;

    public RedisCacheWarmer(
        IMemoryCache memoryCache,
        IDistributedCache distributedCache,
        IConnectionMultiplexer redis,
        IOptions<CacheWarmerOptions> options,
        IOptions<HybridCacheOptions> cacheOptions,
        ILogger<RedisCacheWarmer> logger)
    {
        _memoryCache = memoryCache;
        _distributedCache = distributedCache;
        _redis = redis;
        _options = options.Value;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    public async Task<CacheWarmingResult> WarmCacheAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new CacheWarmingResult();

        try
        {
            _logger.LogInformation("Starting cache warming operation...");

            var database = _redis.GetDatabase();
            var endpoints = _redis.GetEndPoints();

            if (endpoints.Length == 0)
            {
                _logger.LogWarning("No Redis endpoints available for cache warming");
                return result;
            }

            var server = _redis.GetServer(endpoints[0]);

            // Determine key patterns to scan
            var patterns = GetScanPatterns();

            foreach (var pattern in patterns)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Cache warming cancelled");
                    break;
                }

                await ScanAndWarmPattern(server, database, pattern, result, cancellationToken);

                if (result.KeysLoaded >= _options.MaxKeysPerWarming)
                {
                    _logger.LogInformation("Reached maximum keys per warming operation: {MaxKeys}", _options.MaxKeysPerWarming);
                    break;
                }
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            _logger.LogInformation(
                "Cache warming completed: {Loaded} loaded, {Skipped} skipped, {Scanned} scanned in {Duration}ms",
                result.KeysLoaded, result.KeysSkipped, result.KeysScanned, result.Duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Errors.Add($"Cache warming failed: {ex.Message}");
            _logger.LogError(ex, "Error during cache warming operation");
        }

        return result;
    }

    private async Task ScanAndWarmPattern(
        IServer server,
        IDatabase database,
        string pattern,
        CacheWarmingResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Scanning keys with pattern: {Pattern}", pattern);

            var keys = server.Keys(pattern: pattern, pageSize: _options.BatchSize);
            var batch = new List<RedisKey>();

            foreach (var key in keys)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                result.KeysScanned++;
                batch.Add(key);

                if (batch.Count >= _options.BatchSize)
                {
                    await WarmBatch(database, batch, result, cancellationToken);
                    batch.Clear();
                }

                if (result.KeysLoaded >= _options.MaxKeysPerWarming)
                    break;
            }

            // Warm remaining keys
            if (batch.Count > 0)
            {
                await WarmBatch(database, batch, result, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error scanning pattern {pattern}: {ex.Message}");
            _logger.LogError(ex, "Error scanning pattern: {Pattern}", pattern);

            if (!_options.ContinueOnError)
                throw;
        }
    }

    private async Task WarmBatch(
        IDatabase database,
        List<RedisKey> keys,
        CacheWarmingResult result,
        CancellationToken cancellationToken)
    {
        var tasks = keys.Select(key => WarmKey(database, key, result, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task WarmKey(
        IDatabase database,
        RedisKey key,
        CacheWarmingResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var keyString = key.ToString();

            // Apply exclude patterns
            if (ShouldExcludeKey(keyString))
            {
                result.KeysSkipped++;
                if (_options.EnableDetailedLogging)
                    _logger.LogTrace("Skipping excluded key: {Key}", keyString);
                return;
            }

            // Remove prefix for memory cache key
            var memoryKey = RemovePrefix(keyString);

            // Fetch value from Redis with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.FetchTimeout);

            var value = await database.StringGetAsync(key);

            if (!value.HasValue)
            {
                result.KeysSkipped++;
                return;
            }

            // Get TTL from Redis
            var ttl = await database.KeyTimeToLiveAsync(key);

            // Set in memory cache
            var cacheOptions = new MemoryCacheEntryOptions();

            if (_options.L1Expiration.HasValue)
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = _options.L1Expiration.Value;
            }
            else if (ttl.HasValue && ttl.Value.TotalSeconds > 0)
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = ttl.Value;
            }
            else
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = _cacheOptions.DefaultLocalExpiration;
            }

            _memoryCache.Set(memoryKey, value.ToString(), cacheOptions);
            result.KeysLoaded++;

            if (_options.EnableDetailedLogging)
                _logger.LogTrace("Warmed key: {Key} (TTL: {TTL})", keyString, ttl?.TotalSeconds ?? 0);
        }
        catch (OperationCanceledException)
        {
            result.KeysSkipped++;
            if (_options.EnableDetailedLogging)
                _logger.LogTrace("Timeout warming key: {Key}", key);
        }
        catch (Exception ex)
        {
            result.KeysSkipped++;
            result.Errors.Add($"Error warming key {key}: {ex.Message}");

            if (_options.EnableDetailedLogging)
                _logger.LogWarning(ex, "Error warming key: {Key}", key);

            if (!_options.ContinueOnError)
                throw;
        }
    }

    private string[] GetScanPatterns()
    {
        if (_options.IncludePatterns.Length > 0)
        {
            // Use explicitly configured patterns
            return _options.IncludePatterns.Select(p => AddPrefix(p)).ToArray();
        }

        // Use key prefixes
        var prefixes = _options.KeyPrefixes.Length > 0
            ? _options.KeyPrefixes
            : new[] { _cacheOptions.KeyPrefix };

        return prefixes
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => $"{p}*")
            .ToArray();
    }

    private bool ShouldExcludeKey(string key)
    {
        if (_options.ExcludePatterns.Length == 0)
            return false;

        foreach (var pattern in _options.ExcludePatterns)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            if (Regex.IsMatch(key, regexPattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    private string AddPrefix(string key)
    {
        if (string.IsNullOrEmpty(_cacheOptions.KeyPrefix))
            return key;

        return key.StartsWith(_cacheOptions.KeyPrefix)
            ? key
            : $"{_cacheOptions.KeyPrefix}{key}";
    }

    private string RemovePrefix(string key)
    {
        if (string.IsNullOrEmpty(_cacheOptions.KeyPrefix))
            return key;

        return key.StartsWith(_cacheOptions.KeyPrefix)
            ? key.Substring(_cacheOptions.KeyPrefix.Length)
            : key;
    }
}
