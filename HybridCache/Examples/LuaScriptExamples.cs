using HybridCache.LuaScripting;

namespace HybridCache.Examples;

/// <summary>
/// Examples demonstrating Lua script usage with HybridCache.
/// </summary>
public class LuaScriptExamples
{
    private readonly IHybridCache _cache;
    private readonly ILuaScriptExecutor _scriptExecutor;

    public LuaScriptExamples(IHybridCache cache)
    {
        _cache = cache;
        _scriptExecutor = cache.ScriptExecutor
            ?? throw new InvalidOperationException("Lua script support not configured");
    }

    /// <summary>
    /// Example 1: Distributed locking with automatic expiration
    /// </summary>
    public async Task<T> ExecuteWithLockAsync<T>(
        string resourceKey,
        Func<Task<T>> action,
        TimeSpan timeout)
    {
        var lockToken = Guid.NewGuid().ToString();
        var lockKey = $"lock:{resourceKey}";

        // Try to acquire lock
        var acquireResult = await _scriptExecutor.ExecuteAsync<int>(
            LuaScripts.AcquireLock,
            keys: new[] { lockKey },
            values: new object[] { lockToken, (int)timeout.TotalSeconds }
        );

        if (acquireResult.Result != 1)
        {
            throw new InvalidOperationException($"Failed to acquire lock for {resourceKey}");
        }

        try
        {
            // Execute the protected action
            return await action();
        }
        finally
        {
            // Release lock
            await _scriptExecutor.ExecuteAsync<int>(
                LuaScripts.ReleaseLock,
                keys: new[] { lockKey },
                values: new object[] { lockToken }
            );
        }
    }

    /// <summary>
    /// Example 2: Rate limiting with sliding window
    /// </summary>
    public async Task<bool> CheckRateLimitAsync(
        string userId,
        int maxRequests,
        TimeSpan window)
    {
        var result = await _scriptExecutor.ExecuteAsync<int>(
            LuaScripts.RateLimitSlidingWindow,
            keys: new[] { $"ratelimit:{userId}" },
            values: new object[]
            {
                maxRequests,
                (int)window.TotalSeconds,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        );

        return result.Success && result.Result == 1;
    }

    /// <summary>
    /// Example 3: Atomic counter with automatic reset
    /// </summary>
    public async Task<long> IncrementDailyCounterAsync(string counterKey)
    {
        var result = await _scriptExecutor.ExecuteAsync<long>(
            LuaScripts.IncrementWithExpiration,
            keys: new[] { counterKey },
            values: new object[]
            {
                1,      // increment by 1
                86400   // 24 hour expiration
            }
        );

        return result.Result;
    }

    /// <summary>
    /// Example 4: Optimistic concurrency control
    /// </summary>
    public async Task<bool> UpdateWithConcurrencyCheckAsync<T>(
        string key,
        T expectedValue,
        T newValue,
        TimeSpan expiration)
    {
        var serializer = new Serialization.JsonCacheSerializer();
        var expectedBytes = serializer.Serialize(expectedValue);
        var newBytes = serializer.Serialize(newValue);

        var result = await _scriptExecutor.ExecuteAsync<int>(
            LuaScripts.CompareAndSwap,
            keys: new[] { key },
            values: new object[]
            {
                expectedBytes,
                newBytes,
                (int)expiration.TotalSeconds
            }
        );

        return result.Success && result.Result == 1;
    }

    /// <summary>
    /// Example 5: Batch get operation
    /// </summary>
    public async Task<Dictionary<string, T?>> GetMultipleAsync<T>(params string[] keys)
    {
        var result = await _scriptExecutor.ExecuteAsync(
            LuaScripts.GetMultiple,
            keys: keys
        );

        var results = new Dictionary<string, T?>();
        if (result.Success && result.Result is object[] values)
        {
            var serializer = new Serialization.JsonCacheSerializer();
            for (int i = 0; i < keys.Length; i++)
            {
                if (i < values.Length && values[i] is byte[] data)
                {
                    results[keys[i]] = serializer.Deserialize<T>(data);
                }
                else
                {
                    results[keys[i]] = default;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Example 6: Add to limited-size activity log
    /// </summary>
    public async Task<long> LogActivityAsync(string userId, string activity, int maxActivities = 50)
    {
        var serializer = new Serialization.JsonCacheSerializer();
        var activityJson = serializer.Serialize(new
        {
            Activity = activity,
            Timestamp = DateTimeOffset.UtcNow
        });

        var result = await _scriptExecutor.ExecuteAsync<long>(
            LuaScripts.AppendToLimitedList,
            keys: new[] { $"activities:{userId}" },
            values: new object[]
            {
                activityJson,
                maxActivities,
                86400  // 24 hour expiration
            }
        );

        return result.Result;
    }

    /// <summary>
    /// Example 7: Custom Lua script for conditional increment
    /// </summary>
    public async Task<long?> IncrementIfExistsAsync(string key, int incrementBy = 1)
    {
        const string script = @"
            local exists = redis.call('EXISTS', KEYS[1])
            if exists == 1 then
                return redis.call('INCRBY', KEYS[1], ARGV[1])
            end
            return nil
        ";

        var result = await _scriptExecutor.ExecuteAsync<long?>(
            script,
            keys: new[] { key },
            values: new object[] { incrementBy }
        );

        return result.Result;
    }

    /// <summary>
    /// Example 8: Cache with automatic sliding window refresh
    /// </summary>
    public async Task<T?> GetWithSlidingExpirationAsync<T>(string key, TimeSpan slidingWindow)
    {
        var result = await _scriptExecutor.ExecuteAsync(
            LuaScripts.GetAndExtendExpiration,
            keys: new[] { key },
            values: new object[] { (int)slidingWindow.TotalSeconds }
        );

        if (result.Success && result.Result is byte[] data && data.Length > 0)
        {
            var serializer = new Serialization.JsonCacheSerializer();
            return serializer.Deserialize<T>(data);
        }

        return default;
    }

    /// <summary>
    /// Example 9: Prepared script for high-performance repeated execution
    /// </summary>
    public async Task<List<long>> BulkIncrementCountersAsync(string[] counterKeys)
    {
        // Prepare the script once
        var preparedScript = await _scriptExecutor.PrepareAsync(
            LuaScripts.IncrementWithExpiration
        );

        var results = new List<long>();

        // Execute multiple times efficiently
        foreach (var key in counterKeys)
        {
            var result = await preparedScript.ExecuteAsync<long>(
                keys: new[] { key },
                values: new object[] { 1, 3600 }
            );

            if (result.Success)
            {
                results.Add(result.Result);
            }
        }

        return results;
    }

    /// <summary>
    /// Example 10: Multi-key atomic operation with custom script
    /// </summary>
    public async Task<bool> TransferBetweenCountersAsync(
        string sourceKey,
        string targetKey,
        long amount)
    {
        const string transferScript = @"
            local source = tonumber(redis.call('GET', KEYS[1])) or 0
            local amount = tonumber(ARGV[1])

            if source >= amount then
                redis.call('DECRBY', KEYS[1], amount)
                redis.call('INCRBY', KEYS[2], amount)
                return 1
            end

            return 0
        ";

        var result = await _scriptExecutor.ExecuteAsync<int>(
            transferScript,
            keys: new[] { sourceKey, targetKey },
            values: new object[] { amount }
        );

        return result.Success && result.Result == 1;
    }
}
