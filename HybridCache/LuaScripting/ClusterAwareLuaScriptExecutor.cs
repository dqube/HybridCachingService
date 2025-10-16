using StackExchange.Redis;
using HybridCache.Clustering;

namespace HybridCache.LuaScripting;

/// <summary>
/// Cluster-aware Lua script executor that validates hash slots and handles cluster-specific scenarios.
/// </summary>
public class ClusterAwareLuaScriptExecutor : ILuaScriptExecutor
{
    private readonly RedisLuaScriptExecutor _baseExecutor;
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisClusterOptions _clusterOptions;
    private readonly bool _isClusterMode;

    public ClusterAwareLuaScriptExecutor(
        IConnectionMultiplexer redis,
        string? keyPrefix = null,
        RedisClusterOptions? clusterOptions = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _clusterOptions = clusterOptions ?? new RedisClusterOptions();
        _baseExecutor = new RedisLuaScriptExecutor(redis, keyPrefix);
        _isClusterMode = _clusterOptions.IsClusterMode || RedisClusterHelper.IsClusterMode(redis);
    }

    public async Task<LuaScriptResult> ExecuteAsync(
        string script,
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default)
    {
        // Validate hash slots in cluster mode
        if (_isClusterMode && _clusterOptions.ValidateHashSlots && keys != null && keys.Length > 1)
        {
            if (!RedisClusterHelper.ValidateHashSlots(keys))
            {
                return LuaScriptResult.CreateFailure(
                    "All keys must map to the same hash slot in cluster mode. " +
                    "Use hash tags to ensure keys route to the same slot, e.g., {user}:profile, {user}:settings");
            }
        }

        // Execute with retry logic for cluster operations
        return await ExecuteWithRetryAsync(
            () => _baseExecutor.ExecuteAsync(script, keys, values, cancellationToken),
            _clusterOptions.RetryPolicy);
    }

    public async Task<LuaScriptResult<T>> ExecuteAsync<T>(
        string script,
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default)
    {
        // Validate hash slots in cluster mode
        if (_isClusterMode && _clusterOptions.ValidateHashSlots && keys != null && keys.Length > 1)
        {
            if (!RedisClusterHelper.ValidateHashSlots(keys))
            {
                return LuaScriptResult<T>.CreateFailure(
                    "All keys must map to the same hash slot in cluster mode. " +
                    "Use hash tags to ensure keys route to the same slot, e.g., {user}:profile, {user}:settings");
            }
        }

        // Execute with retry logic for cluster operations
        return await ExecuteWithRetryAsync(
            () => _baseExecutor.ExecuteAsync<T>(script, keys, values, cancellationToken),
            _clusterOptions.RetryPolicy);
    }

    public Task<IPreparedLuaScript> PrepareAsync(string script)
    {
        IPreparedLuaScript prepared = new ClusterAwarePreparedScript(script, this);
        return Task.FromResult(prepared);
    }

    /// <summary>
    /// Executes a Lua script with automatic hash tag wrapping for cluster mode.
    /// All keys will be wrapped with the same hash tag to ensure they route to the same slot.
    /// </summary>
    public async Task<LuaScriptResult<T>> ExecuteWithHashTagAsync<T>(
        string script,
        string hashTag,
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default)
    {
        if (_isClusterMode && _clusterOptions.UseHashTags && !string.IsNullOrEmpty(hashTag))
        {
            keys = RedisClusterHelper.WrapKeysWithHashTag(hashTag, keys ?? Array.Empty<string>());
        }

        return await ExecuteAsync<T>(script, keys, values, cancellationToken);
    }

    /// <summary>
    /// Gets cluster information.
    /// </summary>
    public Task<ClusterInfo?> GetClusterInfoAsync()
    {
        return RedisClusterHelper.GetClusterInfoAsync(_redis);
    }

    /// <summary>
    /// Validates that keys can be used together in a Lua script for cluster mode.
    /// </summary>
    public bool ValidateKeysForCluster(params string[] keys)
    {
        if (!_isClusterMode)
            return true;

        return RedisClusterHelper.ValidateHashSlots(keys);
    }

    /// <summary>
    /// Gets the hash slot for a key.
    /// </summary>
    public int GetHashSlot(string key)
    {
        return RedisClusterHelper.CalculateHashSlot(key);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        ClusterRetryPolicy retryPolicy)
    {
        var attempt = 0;
        var delay = retryPolicy.InitialDelay;

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (RedisException ex) when (IsRetryableException(ex) && attempt < retryPolicy.MaxRetries)
            {
                attempt++;

                // Wait before retry
                await Task.Delay(delay);

                // Calculate next delay with exponential backoff
                if (retryPolicy.UseExponentialBackoff)
                {
                    delay = TimeSpan.FromMilliseconds(
                        Math.Min(delay.TotalMilliseconds * 2, retryPolicy.MaxDelay.TotalMilliseconds));
                }
            }
        }
    }

    private bool IsRetryableException(RedisException ex)
    {
        // Retry on cluster-related errors
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("moved") ||
               message.Contains("ask") ||
               message.Contains("tryagain") ||
               message.Contains("clusterdown");
    }

    private class ClusterAwarePreparedScript : IPreparedLuaScript
    {
        private readonly ClusterAwareLuaScriptExecutor _executor;

        public ClusterAwarePreparedScript(string script, ClusterAwareLuaScriptExecutor executor)
        {
            Script = script;
            _executor = executor;
        }

        public string Script { get; }

        public Task<LuaScriptResult> ExecuteAsync(
            string[]? keys = null,
            object[]? values = null,
            CancellationToken cancellationToken = default)
        {
            return _executor.ExecuteAsync(Script, keys, values, cancellationToken);
        }

        public Task<LuaScriptResult<T>> ExecuteAsync<T>(
            string[]? keys = null,
            object[]? values = null,
            CancellationToken cancellationToken = default)
        {
            return _executor.ExecuteAsync<T>(Script, keys, values, cancellationToken);
        }
    }
}
