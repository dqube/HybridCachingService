# Redis Cluster Support for HybridCache

Complete guide for using HybridCache with Redis Cluster (partitioning/sharding) for scalability and high availability.

## Table of Contents

- [Overview](#overview)
- [How Redis Cluster Works](#how-redis-cluster-works)
- [Setup](#setup)
- [Hash Slots and Key Routing](#hash-slots-and-key-routing)
- [Lua Scripts in Cluster Mode](#lua-scripts-in-cluster-mode)
- [Best Practices](#best-practices)
- [Examples](#examples)
- [Troubleshooting](#troubleshooting)

## Overview

Redis Cluster provides:
- **Automatic partitioning** across multiple nodes
- **High availability** with master-slave replication
- **Horizontal scalability** up to 1000 nodes
- **Automatic failover** when master nodes fail

HybridCache fully supports Redis Cluster with:
- ✅ Automatic cluster detection
- ✅ Hash slot validation for Lua scripts
- ✅ Hash tag support for multi-key operations
- ✅ Retry logic for cluster redirections
- ✅ Cluster-aware script execution

## How Redis Cluster Works

### Hash Slots

Redis Cluster uses **16,384 hash slots** to distribute keys:
- Each key is mapped to a slot using CRC16 hash
- Slots are distributed across cluster nodes
- **Multi-key operations must use keys in the same slot**

```
Key "user:123" → CRC16("user:123") % 16384 = Slot 5461
Key "user:456" → CRC16("user:456") % 16384 = Slot 8934
```

### Hash Tags

Use **hash tags** to force keys to the same slot:

```
{user}:profile  → Hashes on "user"
{user}:settings → Hashes on "user"  (same slot!)
```

Only the content within `{ }` is used for hash calculation.

## Setup

### Option 1: Using ConfigurationOptions with Action

```csharp
using StackExchange.Redis;

services.AddHybridCacheWithRedisCluster(
    redisConfig =>
    {
        redisConfig.EndPoints.Add("node1:6379");
        redisConfig.EndPoints.Add("node2:6379");
        redisConfig.EndPoints.Add("node3:6379");
        redisConfig.Password = "mypassword";
        redisConfig.Ssl = true;
        redisConfig.ConnectRetry = 5;
        redisConfig.ConnectTimeout = 10000;
        redisConfig.AbortOnConnectFail = false;
        redisConfig.ReconnectRetryPolicy = new ExponentialRetry(5000);
    },
    cacheOptions =>
    {
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(10);
        cacheOptions.KeyPrefix = "myapp";
    },
    clusterOptions =>
    {
        clusterOptions.IsClusterMode = true;
        clusterOptions.ValidateHashSlots = true;  // Validate multi-key ops
        clusterOptions.UseHashTags = true;        // Auto-wrap with hash tags
    }
);
```

### Option 2: Using ConfigurationOptions Object

```csharp
using StackExchange.Redis;

var redisConfig = new ConfigurationOptions
{
    EndPoints = { "node1:6379", "node2:6379", "node3:6379" },
    Password = "mypassword",
    Ssl = true,
    ConnectRetry = 5,
    ConnectTimeout = 10000,
    AbortOnConnectFail = false,
    ReconnectRetryPolicy = new ExponentialRetry(5000)
};

services.AddHybridCacheWithRedisCluster(
    redisConfig,
    cacheOptions =>
    {
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(10);
        cacheOptions.KeyPrefix = "myapp";
    },
    clusterOptions =>
    {
        clusterOptions.IsClusterMode = true;
        clusterOptions.ValidateHashSlots = true;
        clusterOptions.UseHashTags = true;
    }
);
```

### Option 3: Manual Configuration

```csharp
var configOptions = ConfigurationOptions.Parse("node1:6379,node2:6379,node3:6379");
configOptions.AbortOnConnectFail = false;
configOptions.ConnectRetry = 3;

var redis = ConnectionMultiplexer.Connect(configOptions);
services.AddSingleton<IConnectionMultiplexer>(redis);

services.AddHybridCacheWithRedisLuaSupport(options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
});
```

### Cluster Configuration Options

```csharp
public class RedisClusterOptions
{
    // Whether Redis is in cluster mode
    public bool IsClusterMode { get; set; } = true;

    // Use hash tags for multi-key operations
    public bool UseHashTags { get; set; } = true;

    // Validate all keys in Lua scripts map to same slot
    public bool ValidateHashSlots { get; set; } = true;

    // Retry policy for cluster operations
    public ClusterRetryPolicy RetryPolicy { get; set; }
}
```

## Hash Slots and Key Routing

### Single-Key Operations

Single-key operations work automatically:

```csharp
// These work in cluster mode without any changes
await cache.SetAsync("user:123", userData);
var user = await cache.GetAsync<User>("user:123");
await cache.RemoveAsync("user:123");
```

### Multi-Key Operations with Hash Tags

For operations on multiple keys, use hash tags:

```csharp
// ❌ BAD: Keys likely in different slots
var keys = new[] { "user:123:profile", "user:123:settings" };

// ✅ GOOD: Keys guaranteed in same slot
var keys = new[] { "{user:123}:profile", "{user:123}:settings" };
```

### Helper Methods

```csharp
// Check if keys can be used together
var executor = (ClusterAwareLuaScriptExecutor)cache.ScriptExecutor;
bool canUse = executor.ValidateKeysForCluster("key1", "key2", "key3");

// Calculate hash slot for a key
int slot = executor.GetHashSlot("user:123");

// Wrap keys with same hash tag
var wrappedKeys = RedisClusterHelper.WrapKeysWithHashTag(
    "user123",
    "profile", "settings", "preferences"
);
// Result: {user123}:profile, {user123}:settings, {user123}:preferences
```

## Lua Scripts in Cluster Mode

### The Challenge

Lua scripts in Redis Cluster require all keys to be in the **same hash slot**.

### Automatic Validation

HybridCache validates hash slots automatically:

```csharp
// ❌ This will fail in cluster mode
var result = await scriptExecutor.ExecuteAsync<int>(
    LuaScripts.GetMultiple,
    keys: new[] { "user:123", "user:456" }  // Different slots!
);

if (!result.Success)
{
    Console.WriteLine(result.ErrorMessage);
    // "All keys must map to the same hash slot in cluster mode..."
}
```

### Solution 1: Use Hash Tags

```csharp
// ✅ Use hash tags to ensure same slot
var result = await scriptExecutor.ExecuteAsync<int>(
    LuaScripts.GetMultiple,
    keys: new[] { "{user}:123", "{user}:456" }  // Same slot!
);
```

### Solution 2: ExecuteWithHashTag Method

```csharp
var executor = (ClusterAwareLuaScriptExecutor)cache.ScriptExecutor;

var result = await executor.ExecuteWithHashTagAsync<string[]>(
    LuaScripts.GetMultiple,
    hashTag: "user",  // All keys wrapped with {user}
    keys: new[] { "profile:123", "settings:123" }
);
// Actual keys used: {user}:profile:123, {user}:settings:123
```

### Solution 3: Design Single-Slot Scripts

```csharp
// Design scripts that only need keys from one slot
const string GetUserDataScript = @"
    local profile = redis.call('GET', KEYS[1] .. ':profile')
    local settings = redis.call('GET', KEYS[1] .. ':settings')
    return {profile, settings}
";

var result = await scriptExecutor.ExecuteAsync(
    GetUserDataScript,
    keys: new[] { "{user}:123" }  // Only one base key needed
);
```

## Best Practices

### 1. Key Design for Clusters

```csharp
// ✅ GOOD: Use entity-based hash tags
"{user:123}:profile"
"{user:123}:settings"
"{user:123}:sessions"

// ✅ GOOD: Use tenant-based hash tags for multi-tenancy
"{tenant:acme}:user:123"
"{tenant:acme}:user:456"

// ❌ BAD: No hash tags for related data
"user:123:profile"
"user:123:settings"  // May be on different nodes!
```

### 2. Script Design

```csharp
// ✅ GOOD: Single-entity scripts
const string UpdateUserScript = @"
    redis.call('SET', KEYS[1] .. ':name', ARGV[1])
    redis.call('SET', KEYS[1] .. ':email', ARGV[2])
    return 1
";

// Use with: KEYS[1] = {user:123}

// ❌ AVOID: Cross-entity scripts
const string BadScript = @"
    local user1 = redis.call('GET', KEYS[1])  -- user:123
    local user2 = redis.call('GET', KEYS[2])  -- user:456 (different slot!)
    return user1 .. user2
";
```

### 3. Batch Operations

```csharp
// Group operations by hash slot
public async Task UpdateUserDataAsync(string userId, UserData data)
{
    var hashTag = $"user:{userId}";

    await cache.SetAsync($"{{{hashTag}}}:profile", data.Profile);
    await cache.SetAsync($"{{{hashTag}}}:settings", data.Settings);
    await cache.SetAsync($"{{{hashTag}}}:preferences", data.Preferences);
}
```

### 4. Error Handling

```csharp
try
{
    var result = await scriptExecutor.ExecuteAsync<int>(
        script,
        keys: keys,
        values: values
    );

    if (!result.Success)
    {
        if (result.ErrorMessage.Contains("hash slot"))
        {
            // Handle hash slot validation error
            _logger.LogWarning("Keys not in same slot: {Keys}", string.Join(", ", keys));
        }
    }
}
catch (RedisException ex) when (ex.Message.Contains("MOVED"))
{
    // Cluster topology changed, StackExchange.Redis will retry automatically
    _logger.LogInformation("Cluster slot moved, retrying...");
}
```

## Examples

### Example 1: User Session Management

```csharp
public class ClusterUserSessionManager
{
    private readonly IHybridCache _cache;
    private readonly ILuaScriptExecutor _scriptExecutor;

    public ClusterUserSessionManager(IHybridCache cache)
    {
        _cache = cache;
        _scriptExecutor = cache.ScriptExecutor!;
    }

    // Store session data ensuring all keys in same slot
    public async Task CreateSessionAsync(string userId, SessionData session)
    {
        var hashTag = $"session:{userId}";

        await _cache.SetAsync(
            $"{{{hashTag}}}:data",
            session,
            HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromHours(1))
        );

        await _cache.SetAsync(
            $"{{{hashTag}}}:lastAccess",
            DateTimeOffset.UtcNow,
            HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromHours(1))
        );
    }

    // Atomic session validation with Lua
    public async Task<bool> ValidateAndRefreshSessionAsync(string userId)
    {
        const string script = @"
            local sessionKey = KEYS[1] .. ':data'
            local lastAccessKey = KEYS[1] .. ':lastAccess'
            local ttl = tonumber(ARGV[1])

            local session = redis.call('GET', sessionKey)
            if session then
                redis.call('EXPIRE', sessionKey, ttl)
                redis.call('SETEX', lastAccessKey, ttl, ARGV[2])
                return 1
            end
            return 0
        ";

        var result = await _scriptExecutor.ExecuteAsync<int>(
            script,
            keys: new[] { $"{{session:{userId}}}" },
            values: new object[] { 3600, DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
        );

        return result.Success && result.Result == 1;
    }
}
```

### Example 2: Multi-Tenant Cache

```csharp
public class TenantCacheManager
{
    private readonly ClusterAwareLuaScriptExecutor _scriptExecutor;

    public async Task<Dictionary<string, T>> GetTenantDataAsync<T>(
        string tenantId,
        params string[] dataKeys)
    {
        // All tenant data in same slot using tenant hash tag
        var keysWithHashTag = dataKeys
            .Select(k => $"{{tenant:{tenantId}}}:{k}")
            .ToArray();

        var result = await _scriptExecutor.ExecuteAsync(
            LuaScripts.GetMultiple,
            keys: keysWithHashTag
        );

        // Process results...
        var data = new Dictionary<string, T>();
        // ... deserialization logic
        return data;
    }

    public async Task SetTenantDataAsync(
        string tenantId,
        Dictionary<string, object> data,
        TimeSpan expiration)
    {
        // Use ExecuteWithHashTag for automatic hash tag wrapping
        var keys = data.Keys.ToArray();
        var values = data.Values
            .Concat(new object[] { (int)expiration.TotalSeconds })
            .ToArray();

        await _scriptExecutor.ExecuteWithHashTagAsync<int>(
            LuaScripts.SetMultiple,
            hashTag: $"tenant:{tenantId}",
            keys: keys,
            values: values
        );
    }
}
```

### Example 3: Rate Limiting per Tenant

```csharp
public class ClusterRateLimiter
{
    private readonly IHybridCache _cache;

    public async Task<bool> CheckRateLimitAsync(
        string tenantId,
        string userId,
        int maxRequests,
        TimeSpan window)
    {
        // Use tenant as hash tag so all tenant rate limits in same slot
        var key = $"{{tenant:{tenantId}}}:ratelimit:{userId}";

        var result = await _cache.ScriptExecutor!.ExecuteAsync<int>(
            LuaScripts.RateLimitSlidingWindow,
            keys: new[] { key },
            values: new object[]
            {
                maxRequests,
                (int)window.TotalSeconds,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        );

        return result.Success && result.Result == 1;
    }
}
```

### Example 4: Cluster Info and Health Check

```csharp
public class ClusterHealthService
{
    private readonly ClusterAwareLuaScriptExecutor _executor;

    public async Task<ClusterHealthStatus> GetHealthAsync()
    {
        var clusterInfo = await _executor.GetClusterInfoAsync();

        if (clusterInfo == null)
        {
            return new ClusterHealthStatus
            {
                IsHealthy = false,
                Message = "Not in cluster mode"
            };
        }

        return new ClusterHealthStatus
        {
            IsHealthy = clusterInfo.ClusterState == "ok",
            NodeCount = clusterInfo.NodeCount,
            ClusterState = clusterInfo.ClusterState,
            Nodes = clusterInfo.Nodes
        };
    }

    public async Task<Dictionary<int, int>> GetSlotDistributionAsync()
    {
        // Get how many keys are in each slot
        var distribution = new Dictionary<int, int>();

        for (int i = 0; i < 100; i++)
        {
            var key = $"test:key:{i}";
            var slot = _executor.GetHashSlot(key);
            distribution[slot] = distribution.GetValueOrDefault(slot, 0) + 1;
        }

        return distribution;
    }
}
```

## Troubleshooting

### Error: "All keys must map to the same hash slot"

**Cause**: Multi-key Lua script with keys in different slots.

**Solution**: Use hash tags to ensure keys are in the same slot:

```csharp
// Before: Different slots
var keys = new[] { "user:123", "user:456" };

// After: Same slot
var keys = new[] { "{user}:123", "{user}:456" };
```

### Error: "MOVED 3999 127.0.0.1:7001"

**Cause**: Slot has been reassigned to a different node.

**Solution**: StackExchange.Redis handles this automatically with retries. No action needed.

### Error: "CROSSSLOT Keys in request don't hash to the same slot"

**Cause**: Redis detected keys in different slots.

**Solution**:
1. Enable hash slot validation: `clusterOptions.ValidateHashSlots = true`
2. Use hash tags in your keys
3. Use `ExecuteWithHashTagAsync()` method

### Poor Performance

**Symptoms**: Slow operations, high latency

**Solutions**:
1. **Check key distribution**: Ensure keys are evenly distributed across slots
2. **Avoid hotspots**: Don't overuse the same hash tag
3. **Use connection pooling**: Reuse IConnectionMultiplexer
4. **Enable pipelining**: Batch operations when possible

```csharp
// Check slot distribution
var executor = (ClusterAwareLuaScriptExecutor)cache.ScriptExecutor;
var slots = new[] { "key1", "key2", "key3", "key4" }
    .Select(k => executor.GetHashSlot(k))
    .ToArray();

Console.WriteLine($"Slots: {string.Join(", ", slots)}");
// Should see different slots for good distribution
```

## Testing with Redis Cluster

### Local Cluster Setup (Docker)

```bash
# Create a local 3-node cluster
docker run -d --name redis-cluster \
  -p 7000-7005:7000-7005 \
  -e "CLUSTER_ONLY=yes" \
  -e "IP=0.0.0.0" \
  redis:7-alpine redis-cluster
```

### Connection String

```csharp
// For local cluster
"localhost:7000,localhost:7001,localhost:7002"

// For production
"node1.redis.example.com:6379,node2.redis.example.com:6379,node3.redis.example.com:6379"
```

## Performance Considerations

1. **Hash Tag Strategy**: Balance between co-location and distribution
   - Too broad: Data concentrated on few nodes (hotspots)
   - Too granular: Can't do multi-key operations

2. **Key Design**: Plan hash tags based on access patterns
   ```csharp
   // User-centric application
   "{user:123}:*"  // All user data in same slot

   // Multi-tenant SaaS
   "{tenant:acme}:*"  // All tenant data in same slot
   ```

3. **Network Hops**: Operations on same slot = 1 hop, different slots = multiple hops

## Summary

✅ **Do:**
- Use hash tags for related keys
- Design scripts for single-slot operations
- Validate hash slots in development
- Plan key design around access patterns
- Use `AddHybridCacheWithRedisCluster()` for automatic setup

❌ **Don't:**
- Assume keys will be co-located without hash tags
- Use multi-key operations without hash tags
- Overuse the same hash tag (creates hotspots)
- Forget to handle cluster-specific errors

## Additional Resources

- [Redis Cluster Specification](https://redis.io/topics/cluster-spec)
- [Redis Cluster Tutorial](https://redis.io/topics/cluster-tutorial)
- [StackExchange.Redis Cluster Docs](https://stackexchange.github.io/StackExchange.Redis/Cluster)

---

**Need Help?** Check the [main documentation](../README.md) or [Lua scripting guide](../LuaScripting/README_LUA.md).
