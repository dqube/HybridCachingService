# Lua Script Support for HybridCache

The HybridCache library provides first-class support for executing Lua scripts on Redis, enabling atomic operations, custom cache logic, and advanced distributed cache patterns.

## Table of Contents

- [Overview](#overview)
- [Getting Started](#getting-started)
- [Built-in Scripts](#built-in-scripts)
- [Custom Scripts](#custom-scripts)
- [Best Practices](#best-practices)
- [Examples](#examples)

## Overview

Lua scripts execute atomically on Redis, providing:
- **Atomicity**: Multiple operations execute as a single unit
- **Performance**: Reduced network round trips
- **Consistency**: No race conditions between operations
- **Custom Logic**: Implement complex cache patterns server-side

## Getting Started

### 1. Register HybridCache with Redis and Lua Support

```csharp
// Option 1: Simple setup with connection string
services.AddHybridCacheWithRedis("localhost:6379", options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
    options.KeyPrefix = "myapp";
});

// Option 2: Use existing IConnectionMultiplexer
services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));

services.AddHybridCacheWithRedisLuaSupport(options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
});
```

### 2. Access the Script Executor

```csharp
public class MyService
{
    private readonly IHybridCache _cache;
    private readonly ILuaScriptExecutor _scriptExecutor;

    public MyService(IHybridCache cache)
    {
        _cache = cache;
        _scriptExecutor = cache.ScriptExecutor
            ?? throw new InvalidOperationException("Lua scripts not supported");
    }

    public async Task<int> IncrementCounterAsync(string key)
    {
        var result = await _scriptExecutor.ExecuteAsync<int>(
            LuaScripts.IncrementWithExpiration,
            keys: new[] { key },
            values: new object[] { 1, 300 } // increment by 1, 300s expiration
        );

        return result.Result;
    }
}
```

## Built-in Scripts

The library provides common Lua scripts in the `LuaScripts` class:

### 1. GetAndExtendExpiration

Atomically retrieves a value and extends its TTL (sliding window pattern).

```csharp
var result = await scriptExecutor.ExecuteAsync<string>(
    LuaScripts.GetAndExtendExpiration,
    keys: new[] { "session:123" },
    values: new object[] { 1800 } // 30 minutes
);

if (result.Success && result.Result != null)
{
    var sessionData = result.Result;
}
```

### 2. SetIfNotExists

Adds a value only if the key doesn't exist (cache-aside "add" operation).

```csharp
var result = await scriptExecutor.ExecuteAsync<int>(
    LuaScripts.SetIfNotExists,
    keys: new[] { "lock:resource" },
    values: new object[] { "owner-123", 60 }
);

bool lockAcquired = result.Result == 1;
```

### 3. SetIfExists

Updates a value only if the key exists.

```csharp
var result = await scriptExecutor.ExecuteAsync<int>(
    LuaScripts.SetIfExists,
    keys: new[] { "user:profile:456" },
    values: new object[] { updatedProfileJson, 3600 }
);

bool wasUpdated = result.Result == 1;
```

### 4. CompareAndSwap

Optimistic concurrency control - update only if current value matches expected.

```csharp
var result = await scriptExecutor.ExecuteAsync<int>(
    LuaScripts.CompareAndSwap,
    keys: new[] { "inventory:product:789" },
    values: new object[]
    {
        "100",      // expected value
        "99",       // new value
        3600        // expiration
    }
);

bool swapSucceeded = result.Result == 1;
```

### 5. IncrementWithExpiration

Atomically increments a counter and sets expiration for new keys.

```csharp
var result = await scriptExecutor.ExecuteAsync<long>(
    LuaScripts.IncrementWithExpiration,
    keys: new[] { "stats:page_views" },
    values: new object[] { 1, 86400 } // increment by 1, 24h expiration
);

long newCount = result.Result;
```

### 6. GetMultiple

Retrieves multiple keys in a single atomic operation.

```csharp
var result = await scriptExecutor.ExecuteAsync<string[]>(
    LuaScripts.GetMultiple,
    keys: new[] { "user:1", "user:2", "user:3" }
);

var users = result.Result; // array with values (null for missing keys)
```

### 7. SetMultiple

Sets multiple keys with the same expiration atomically.

```csharp
var result = await scriptExecutor.ExecuteAsync<int>(
    LuaScripts.SetMultiple,
    keys: new[] { "cache:a", "cache:b", "cache:c" },
    values: new object[]
    {
        "valueA", "valueB", "valueC", // values
        300  // expiration for all keys
    }
);

int keysSet = result.Result;
```

### 8. AcquireLock / ReleaseLock

Distributed locking with token validation.

```csharp
// Acquire lock
var lockToken = Guid.NewGuid().ToString();
var acquireResult = await scriptExecutor.ExecuteAsync<int>(
    LuaScripts.AcquireLock,
    keys: new[] { "lock:critical_section" },
    values: new object[] { lockToken, 30 }
);

if (acquireResult.Result == 1)
{
    try
    {
        // Critical section
        await DoWorkAsync();
    }
    finally
    {
        // Release lock
        await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.ReleaseLock,
            keys: new[] { "lock:critical_section" },
            values: new object[] { lockToken }
        );
    }
}
```

### 9. RateLimitSlidingWindow

Sliding window rate limiting.

```csharp
var result = await scriptExecutor.ExecuteAsync<int>(
    LuaScripts.RateLimitSlidingWindow,
    keys: new[] { "ratelimit:user:123" },
    values: new object[]
    {
        100,                                    // max requests
        60,                                     // window size (seconds)
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() // current timestamp
    }
);

bool isAllowed = result.Result == 1;
```

### 10. AppendToLimitedList

FIFO queue with automatic size limiting.

```csharp
var result = await scriptExecutor.ExecuteAsync<long>(
    LuaScripts.AppendToLimitedList,
    keys: new[] { "recent:activities:user:123" },
    values: new object[]
    {
        activityJson,  // value to append
        100,           // max list size
        3600           // expiration
    }
);

long newLength = result.Result;
```

## Custom Scripts

### Writing Custom Lua Scripts

```csharp
public class CustomCacheScripts
{
    public const string IncrementIfExists = @"
        local exists = redis.call('EXISTS', KEYS[1])
        if exists == 1 then
            local value = redis.call('INCRBY', KEYS[1], ARGV[1])
            redis.call('EXPIRE', KEYS[1], ARGV[2])
            return value
        end
        return nil
    ";
}

// Use the custom script
var result = await scriptExecutor.ExecuteAsync<long?>(
    CustomCacheScripts.IncrementIfExists,
    keys: new[] { "counter:active_users" },
    values: new object[] { 1, 300 }
);
```

### Prepared Scripts

For frequently executed scripts, prepare them once for better performance:

```csharp
// Prepare the script (cached on Redis server)
var preparedScript = await scriptExecutor.PrepareAsync(
    LuaScripts.CompareAndSwap
);

// Execute multiple times efficiently
for (int i = 0; i < 1000; i++)
{
    var result = await preparedScript.ExecuteAsync<int>(
        keys: new[] { $"key:{i}" },
        values: new object[] { "expectedValue", "newValue", 300 }
    );
}
```

## Best Practices

### 1. Keep Scripts Small and Focused
```csharp
// Good: Single responsibility
public const string GetAndRefresh = @"
    local value = redis.call('GET', KEYS[1])
    if value then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
    return value
";

// Avoid: Too much logic in a single script
```

### 2. Handle Nil Values Properly
```csharp
var result = await scriptExecutor.ExecuteAsync<string>(script, keys, values);

if (result.Success)
{
    if (result.Result != null)
    {
        // Key exists
    }
    else
    {
        // Key doesn't exist (nil in Lua)
    }
}
```

### 3. Use Typed Results
```csharp
// Specify the expected return type
var intResult = await scriptExecutor.ExecuteAsync<int>(script, keys, values);
var stringResult = await scriptExecutor.ExecuteAsync<string>(script, keys, values);
var arrayResult = await scriptExecutor.ExecuteAsync<string[]>(script, keys, values);
```

### 4. Error Handling
```csharp
var result = await scriptExecutor.ExecuteAsync<int>(script, keys, values);

if (!result.Success)
{
    _logger.LogError("Lua script failed: {Error}", result.ErrorMessage);
    // Fallback logic
}
```

## Examples

### Example 1: Atomic Counter with Auto-Reset

```csharp
public class RequestCounter
{
    private readonly ILuaScriptExecutor _scriptExecutor;

    private const string IncrementOrResetScript = @"
        local count = redis.call('GET', KEYS[1])
        if count then
            local newCount = tonumber(count) + 1
            if newCount > tonumber(ARGV[2]) then
                redis.call('SET', KEYS[1], '1')
                redis.call('EXPIRE', KEYS[1], ARGV[1])
                return 1
            else
                redis.call('INCR', KEYS[1])
                return newCount
            end
        else
            redis.call('SETEX', KEYS[1], ARGV[1], '1')
            return 1
        end
    ";

    public async Task<long> IncrementRequestAsync(string userId)
    {
        var result = await _scriptExecutor.ExecuteAsync<long>(
            IncrementOrResetScript,
            keys: new[] { $"requests:{userId}" },
            values: new object[]
            {
                3600,  // 1 hour expiration
                1000   // reset after 1000 requests
            }
        );

        return result.Result;
    }
}
```

### Example 2: Cached Leaderboard with Score Update

```csharp
private const string UpdateLeaderboardScript = @"
    redis.call('ZADD', KEYS[1], ARGV[1], ARGV[2])
    redis.call('EXPIRE', KEYS[1], ARGV[3])

    local rank = redis.call('ZREVRANK', KEYS[1], ARGV[2])
    local score = redis.call('ZSCORE', KEYS[1], ARGV[2])

    return {rank, score}
";

public async Task<(long rank, double score)> UpdateScoreAsync(
    string leaderboardKey,
    string playerId,
    double score)
{
    var result = await _scriptExecutor.ExecuteAsync(
        UpdateLeaderboardScript,
        keys: new[] { leaderboardKey },
        values: new object[] { score, playerId, 3600 }
    );

    var array = result.GetResult<object[]>();
    return (Convert.ToInt64(array[0]), Convert.ToDouble(array[1]));
}
```

### Example 3: Cache with Probabilistic Early Expiration

Prevents cache stampede using XFetch algorithm.

```csharp
private const string XFetchScript = @"
    local value = redis.call('GET', KEYS[1])
    local ttl = redis.call('TTL', KEYS[1])

    if value and ttl > 0 then
        local delta = tonumber(ARGV[1])
        local beta = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])

        -- Calculate if we should refresh early
        local threshold = delta * beta * math.log(math.random())

        if ttl < -threshold then
            return {value, 1}  -- return value + refresh flag
        else
            return {value, 0}  -- return value only
        end
    end

    return {nil, 1}  -- cache miss, needs refresh
";

public async Task<T> GetOrRefreshAsync<T>(
    string key,
    Func<Task<T>> factory,
    TimeSpan expiration)
{
    var result = await _scriptExecutor.ExecuteAsync(
        XFetchScript,
        keys: new[] { key },
        values: new object[]
        {
            expiration.TotalSeconds,
            1.0,  // beta parameter
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }
    );

    var resultArray = result.GetResult<object[]>();
    var cachedValue = resultArray?[0];
    var shouldRefresh = Convert.ToInt32(resultArray?[1]) == 1;

    if (shouldRefresh)
    {
        var newValue = await factory();
        await _cache.SetAsync(key, newValue,
            HybridCacheEntryOptions.WithAbsoluteExpiration(expiration));
        return newValue;
    }

    return _cache.Serializer.Deserialize<T>((byte[])cachedValue);
}
```

## Advanced Patterns

### Circuit Breaker with Lua

```csharp
private const string CircuitBreakerScript = @"
    local state = redis.call('GET', KEYS[1])
    local failures = tonumber(redis.call('GET', KEYS[2])) or 0
    local threshold = tonumber(ARGV[1])
    local timeout = tonumber(ARGV[2])

    if state == 'open' then
        local ttl = redis.call('TTL', KEYS[1])
        if ttl <= 0 then
            redis.call('SET', KEYS[1], 'half-open')
            return 'half-open'
        end
        return 'open'
    end

    if failures >= threshold then
        redis.call('SETEX', KEYS[1], timeout, 'open')
        return 'open'
    end

    return 'closed'
";
```

## Lua Script Reference

### Redis Commands Available
- String: GET, SET, SETEX, INCR, INCRBY, DECR, etc.
- Hash: HGET, HSET, HINCRBY, HGETALL, etc.
- List: LPUSH, RPUSH, LPOP, LLEN, LRANGE, etc.
- Set: SADD, SREM, SISMEMBER, SMEMBERS, etc.
- Sorted Set: ZADD, ZRANGE, ZRANK, ZINCRBY, etc.
- Keys: EXISTS, EXPIRE, TTL, DEL, etc.

### Lua Syntax Tips
```lua
-- Variables
local value = redis.call('GET', KEYS[1])

-- Conditionals
if value then
    -- do something
end

-- Loops
for i = 1, #KEYS do
    redis.call('DEL', KEYS[i])
end

-- Return values
return value           -- single value
return {val1, val2}   -- array
```

## Performance Considerations

1. **Script Size**: Keep scripts small (< 1KB recommended)
2. **Prepare Frequently Used Scripts**: Use `PrepareAsync()` for scripts executed repeatedly
3. **Batch Operations**: Combine multiple operations in a single script
4. **Avoid Long-Running Scripts**: Scripts block Redis; keep execution under 5ms
5. **Key Prefixes**: Use the configured key prefix automatically applied by the executor

## Troubleshooting

### Script Execution Fails

```csharp
var result = await scriptExecutor.ExecuteAsync<int>(script, keys, values);

if (!result.Success)
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
    // Common issues:
    // - Syntax error in Lua script
    // - Wrong number of KEYS or ARGV
    // - Redis connection issue
}
```

### Type Conversion Issues

```csharp
// Redis returns integers for some operations
var result = await scriptExecutor.ExecuteAsync<long>(script, keys, values);

// For arrays, use appropriate type
var arrayResult = await scriptExecutor.ExecuteAsync<object[]>(script, keys, values);
```

## License

MIT License
