using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using HybridCache.Serialization;
using HybridCache.LuaScripting;
using HybridCache.Notifications;

namespace HybridCache;

/// <summary>
/// Default implementation of the hybrid cache with two-tier architecture.
/// L1: In-memory cache (fast, local to the instance)
/// L2: Distributed cache (shared across instances)
/// </summary>
public class DefaultHybridCache : IHybridCache, IDisposable
{
    private readonly IMemoryCache _localCache;
    private readonly IDistributedCache? _distributedCache;
    private readonly ICacheSerializer _serializer;
    private readonly HybridCacheOptions _options;
    private readonly ILuaScriptExecutor? _scriptExecutor;
    private readonly ICacheNotificationPublisher? _notificationPublisher;
    private readonly ICacheNotificationSubscriber? _notificationSubscriber;
    private readonly CacheNotificationOptions? _notificationOptions;

    public DefaultHybridCache(
        IMemoryCache localCache,
        IOptions<HybridCacheOptions> options,
        ICacheSerializer? serializer = null,
        IDistributedCache? distributedCache = null,
        ILuaScriptExecutor? scriptExecutor = null,
        ICacheNotificationPublisher? notificationPublisher = null,
        ICacheNotificationSubscriber? notificationSubscriber = null,
        IOptions<CacheNotificationOptions>? notificationOptions = null)
    {
        _localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? new JsonCacheSerializer();
        _distributedCache = _options.EnableDistributedCache ? distributedCache : null;
        _scriptExecutor = scriptExecutor;
        _notificationPublisher = notificationPublisher;
        _notificationSubscriber = notificationSubscriber;
        _notificationOptions = notificationOptions?.Value;

        // Subscribe to notifications if enabled
        if (_notificationSubscriber != null && _notificationOptions?.EnableNotifications == true)
        {
            _ = InitializeNotificationsAsync();
        }
    }

    /// <summary>
    /// Gets the Lua script executor for executing custom Lua scripts on the distributed cache.
    /// Returns null if distributed cache is not configured or doesn't support Lua scripts.
    /// </summary>
    public ILuaScriptExecutor? ScriptExecutor => _scriptExecutor;

    private async Task InitializeNotificationsAsync()
    {
        if (_notificationSubscriber == null || _notificationOptions == null)
            return;

        await _notificationSubscriber.SubscribeAsync(HandleCacheNotificationAsync);
    }

    private async Task HandleCacheNotificationAsync(CacheChangeNotification notification)
    {
        // Auto-invalidate L1 cache if enabled
        if (_notificationOptions?.AutoInvalidateL1OnNotification == true)
        {
            var fullKey = GetFullKey(notification.Key);

            switch (notification.Operation)
            {
                case CacheOperation.Set:
                    // Optionally, you could refresh from L2 here
                    _localCache.Remove(fullKey);
                    break;

                case CacheOperation.Remove:
                case CacheOperation.Expire:
                    _localCache.Remove(fullKey);
                    break;

                case CacheOperation.Clear:
                    // Clear all L1 cache (be careful with this)
                    if (_localCache is MemoryCache memCache)
                    {
                        memCache.Compact(1.0); // Compact 100% = clear all
                    }
                    break;
            }
        }

        await Task.CompletedTask;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var fullKey = GetFullKey(key);

        // Try L1 cache first
        if (_localCache.TryGetValue(fullKey, out T? cachedValue))
        {
            return cachedValue;
        }

        // Try L2 cache if available
        if (_distributedCache is not null)
        {
            var data = await _distributedCache.GetAsync(fullKey, cancellationToken);
            if (data is not null && data.Length > 0)
            {
                var value = _serializer.Deserialize<T>(data);

                // Populate L1 cache
                if (value is not null)
                {
                    var localExpiration = _options.DefaultLocalExpiration ?? _options.DefaultExpiration;
                    _localCache.Set(fullKey, value, localExpiration);
                }

                return value;
            }
        }

        return default;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        HybridCacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        var fullKey = GetFullKey(key);
        options ??= HybridCacheEntryOptions.Default;

        // Try to get from cache
        var cachedValue = await GetAsync<T>(key, cancellationToken);
        if (cachedValue is not null)
        {
            return cachedValue;
        }

        // Create the value
        var value = await factory(cancellationToken);

        if (value is not null)
        {
            await SetAsync(key, value, options, cancellationToken);
        }

        return value;
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var fullKey = GetFullKey(key);
        options ??= HybridCacheEntryOptions.Default;

        // Set in L1 cache
        if (options.UseL1Cache)
        {
            var memoryCacheOptions = CreateMemoryCacheEntryOptions(options);
            _localCache.Set(fullKey, value, memoryCacheOptions);
        }

        // Set in L2 cache
        if (options.UseL2Cache && _distributedCache is not null && value is not null)
        {
            var data = _serializer.Serialize(value);
            var distributedCacheOptions = CreateDistributedCacheEntryOptions(options);
            await _distributedCache.SetAsync(fullKey, data, distributedCacheOptions, cancellationToken);
        }

        // Publish notification
        if (_notificationPublisher != null)
        {
            await _notificationPublisher.PublishAsync(new CacheChangeNotification
            {
                Operation = CacheOperation.Set,
                Key = key,
                Expiration = options.AbsoluteExpirationRelativeToNow ?? options.SlidingExpiration
            }, cancellationToken);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var fullKey = GetFullKey(key);

        // Remove from L1
        _localCache.Remove(fullKey);

        // Remove from L2
        if (_distributedCache is not null)
        {
            await _distributedCache.RemoveAsync(fullKey, cancellationToken);
        }

        // Publish notification
        if (_notificationPublisher != null)
        {
            await _notificationPublisher.PublishAsync(new CacheChangeNotification
            {
                Operation = CacheOperation.Remove,
                Key = key
            }, cancellationToken);
        }
    }

    public void RemoveLocal(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var fullKey = GetFullKey(key);
        _localCache.Remove(fullKey);
    }

    private string GetFullKey(string key)
    {
        if (string.IsNullOrEmpty(_options.KeyPrefix))
        {
            return key;
        }

        return $"{_options.KeyPrefix}:{key}";
    }

    private MemoryCacheEntryOptions CreateMemoryCacheEntryOptions(HybridCacheEntryOptions options)
    {
        var memoryCacheOptions = new MemoryCacheEntryOptions();

        if (options.LocalCacheExpiration.HasValue)
        {
            memoryCacheOptions.AbsoluteExpirationRelativeToNow = options.LocalCacheExpiration;
        }
        else if (options.AbsoluteExpirationRelativeToNow.HasValue)
        {
            memoryCacheOptions.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
        }
        else if (options.AbsoluteExpiration.HasValue)
        {
            memoryCacheOptions.AbsoluteExpiration = options.AbsoluteExpiration;
        }
        else if (options.SlidingExpiration.HasValue)
        {
            memoryCacheOptions.SlidingExpiration = options.SlidingExpiration;
        }
        else
        {
            var localExpiration = _options.DefaultLocalExpiration ?? _options.DefaultExpiration;
            memoryCacheOptions.AbsoluteExpirationRelativeToNow = localExpiration;
        }

        return memoryCacheOptions;
    }

    private DistributedCacheEntryOptions CreateDistributedCacheEntryOptions(HybridCacheEntryOptions options)
    {
        var distributedCacheOptions = new DistributedCacheEntryOptions();

        if (options.AbsoluteExpirationRelativeToNow.HasValue)
        {
            distributedCacheOptions.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
        }
        else if (options.AbsoluteExpiration.HasValue)
        {
            distributedCacheOptions.AbsoluteExpiration = options.AbsoluteExpiration;
        }
        else if (options.SlidingExpiration.HasValue)
        {
            distributedCacheOptions.SlidingExpiration = options.SlidingExpiration;
        }
        else
        {
            distributedCacheOptions.AbsoluteExpirationRelativeToNow = _options.DefaultExpiration;
        }

        return distributedCacheOptions;
    }

    public void Dispose()
    {
        _notificationSubscriber?.Dispose();
        GC.SuppressFinalize(this);
    }
}
