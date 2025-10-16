# HybridCache

A high-performance two-tier caching library for .NET 8+ that combines in-memory (L1) and distributed (L2) caching for optimal performance and scalability.

## Features

- **Two-Tier Architecture**: Fast in-memory cache (L1) backed by distributed cache (L2)
- **Cache Change Notifications**: Automatic L1 invalidation across instances via Redis pub/sub
- **Redis Cluster Support**: Full support for Redis partitioning/sharding with automatic hash slot validation
- **Lua Script Support**: Execute atomic operations on Redis with built-in and custom Lua scripts
- **Flexible Configuration**: Configure expiration, cache levels, and serialization per entry
- **Multiple Backends**: Works with any `IDistributedCache` implementation (Redis, SQL Server, etc.)
- **Dependency Injection**: First-class support for Microsoft.Extensions.DependencyInjection
- **Type-Safe**: Generic API with full null-safety support
- **Async/Await**: Fully asynchronous operations with cancellation token support
- **Extensible Serialization**: Default JSON serialization with support for custom serializers

## Architecture

```
┌─────────────────────────────────────┐
│     Application Code                │
└───────────┬─────────────────────────┘
            │
            ▼
┌─────────────────────────────────────┐
│     IHybridCache                    │
└───────────┬─────────────────────────┘
            │
     ┌──────┴──────┐
     ▼             ▼
┌─────────┐   ┌──────────────┐
│L1 Cache │   │ L2 Cache     │
│(Memory) │   │(Distributed) │
└─────────┘   └──────────────┘
   Fast          Shared
   Local         Scalable
```

## Installation

Add the package to your project:

```bash
dotnet add package HybridCache
```

## Quick Start

### 1. Register Services

**Basic setup (in-memory only):**

```csharp
services.AddHybridCache(options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
});
```

**With distributed cache (Redis example):**

```csharp
// First, add a distributed cache implementation
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

// Then add hybrid cache
services.AddHybridCacheWithDistributed(options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
    options.DefaultLocalExpiration = TimeSpan.FromMinutes(2);
    options.KeyPrefix = "myapp";
});
```

**With Redis and Lua script support:**

```csharp
// All-in-one setup with Redis and Lua support
services.AddHybridCacheWithRedis("localhost:6379", options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
    options.KeyPrefix = "myapp";
});
```

**With Redis Cluster (partitioning/sharding):**

```csharp
// Redis Cluster setup with automatic hash slot validation
services.AddHybridCacheWithRedisCluster(
    "node1:6379,node2:6379,node3:6379",
    cacheOptions =>
    {
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(10);
        cacheOptions.KeyPrefix = "myapp";
    },
    clusterOptions =>
    {
        clusterOptions.ValidateHashSlots = true;  // Validate multi-key ops
        clusterOptions.UseHashTags = true;        // Auto-wrap with hash tags
    }
);
```

**With cache change notifications:**

```csharp
// Setup Redis and cache
services.AddHybridCacheWithRedis("localhost:6379", options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
});

// Add notifications for automatic L1 invalidation across instances
services.AddCacheNotifications(options =>
{
    options.EnableNotifications = true;
    options.AutoInvalidateL1OnNotification = true;
});
```

### 2. Use in Your Code

```csharp
public class UserService
{
    private readonly IHybridCache _cache;
    private readonly IUserRepository _repository;

    public UserService(IHybridCache cache, IUserRepository repository)
    {
        _cache = cache;
        _repository = repository;
    }

    public async Task<User> GetUserAsync(int userId, CancellationToken ct = default)
    {
        var key = $"user:{userId}";

        return await _cache.GetOrCreateAsync(
            key,
            async cancellationToken =>
            {
                // This factory is only called if the value is not in cache
                return await _repository.GetUserByIdAsync(userId, cancellationToken);
            },
            options: HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(15)),
            cancellationToken: ct
        );
    }

    public async Task UpdateUserAsync(User user, CancellationToken ct = default)
    {
        await _repository.UpdateUserAsync(user, ct);

        // Invalidate cache after update
        await _cache.RemoveAsync($"user:{user.Id}", ct);
    }
}
```

## API Reference

### IHybridCache Methods

#### GetAsync&lt;T&gt;
Gets a value from the cache.

```csharp
Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
```

#### GetOrCreateAsync&lt;T&gt;
Gets a value from cache or creates it using a factory function.

```csharp
Task<T> GetOrCreateAsync<T>(
    string key,
    Func<CancellationToken, Task<T>> factory,
    HybridCacheEntryOptions? options = null,
    CancellationToken cancellationToken = default);
```

#### SetAsync&lt;T&gt;
Sets a value in the cache.

```csharp
Task SetAsync<T>(
    string key,
    T value,
    HybridCacheEntryOptions? options = null,
    CancellationToken cancellationToken = default);
```

#### RemoveAsync
Removes a value from both L1 and L2 caches.

```csharp
Task RemoveAsync(string key, CancellationToken cancellationToken = default);
```

#### RemoveLocal
Removes a value from L1 (local) cache only.

```csharp
void RemoveLocal(string key);
```

## Configuration Options

### HybridCacheOptions

```csharp
services.AddHybridCache(options =>
{
    // Default expiration for all cache entries
    options.DefaultExpiration = TimeSpan.FromMinutes(5);

    // Different expiration for L1 cache (faster eviction from memory)
    options.DefaultLocalExpiration = TimeSpan.FromMinutes(2);

    // Enable/disable distributed cache
    options.EnableDistributedCache = true;

    // Key prefix for all cache entries
    options.KeyPrefix = "myapp";

    // Compression settings (future feature)
    options.EnableCompression = false;
    options.CompressionThreshold = 1024;
});
```

### HybridCacheEntryOptions

Configure individual cache entries:

```csharp
var options = new HybridCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
    SlidingExpiration = TimeSpan.FromMinutes(15),
    UseL1Cache = true,  // Store in memory cache
    UseL2Cache = true,  // Store in distributed cache
    LocalCacheExpiration = TimeSpan.FromMinutes(5)  // Different L1 expiration
};

await cache.SetAsync("key", value, options);
```

Helper methods:

```csharp
// Absolute expiration
var options = HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(10));

// Sliding expiration
var options = HybridCacheEntryOptions.WithSlidingExpiration(TimeSpan.FromMinutes(5));
```

## Cache Patterns

### 1. Cache-Aside (Lazy Loading)

```csharp
public async Task<Product> GetProductAsync(int productId)
{
    return await _cache.GetOrCreateAsync(
        $"product:{productId}",
        async ct => await _db.Products.FindAsync(productId, ct),
        HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromHours(1))
    );
}
```

### 2. Write-Through

```csharp
public async Task UpdateProductAsync(Product product)
{
    await _db.UpdateAsync(product);
    await _cache.SetAsync(
        $"product:{product.Id}",
        product,
        HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromHours(1))
    );
}
```

### 3. Cache Invalidation

```csharp
public async Task DeleteProductAsync(int productId)
{
    await _db.DeleteAsync(productId);
    await _cache.RemoveAsync($"product:{productId}");
}
```

### 4. L1-Only Caching (Local-Heavy Data)

```csharp
var options = new HybridCacheEntryOptions
{
    UseL1Cache = true,
    UseL2Cache = false,  // Don't store in distributed cache
    LocalCacheExpiration = TimeSpan.FromMinutes(5)
};

await _cache.SetAsync("session-data", sessionData, options);
```

## Lua Script Support

HybridCache provides powerful Lua script execution for atomic Redis operations. See the [Lua Scripts Documentation](LuaScripting/README_LUA.md) for comprehensive examples.

### Quick Example

```csharp
public class RateLimiter
{
    private readonly IHybridCache _cache;

    public RateLimiter(IHybridCache cache)
    {
        _cache = cache;
    }

    public async Task<bool> IsAllowedAsync(string userId)
    {
        var scriptExecutor = _cache.ScriptExecutor
            ?? throw new InvalidOperationException("Lua scripts not supported");

        // Use built-in rate limiting script
        var result = await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.RateLimitSlidingWindow,
            keys: new[] { $"ratelimit:{userId}" },
            values: new object[]
            {
                100,  // max requests
                60,   // window size (seconds)
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        );

        return result.Success && result.Result == 1;
    }
}
```

### Built-in Lua Scripts

- `GetAndExtendExpiration` - Sliding window refresh
- `SetIfNotExists` / `SetIfExists` - Conditional updates
- `CompareAndSwap` - Optimistic concurrency control
- `IncrementWithExpiration` - Atomic counters
- `AcquireLock` / `ReleaseLock` - Distributed locking
- `RateLimitSlidingWindow` - Rate limiting
- `GetMultiple` / `SetMultiple` - Batch operations
- And more...

See [LuaScripting/README_LUA.md](LuaScripting/README_LUA.md) for detailed documentation.

## Custom Serializer

Implement `ICacheSerializer` for custom serialization:

```csharp
public class MessagePackSerializer : ICacheSerializer
{
    public byte[] Serialize<T>(T value)
    {
        return MessagePackSerializer.Serialize(value);
    }

    public T? Deserialize<T>(byte[] data)
    {
        return MessagePackSerializer.Deserialize<T>(data);
    }
}

// Register with DI
services.AddHybridCache<MessagePackSerializer>(options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
});
```

## Best Practices

1. **Use appropriate expiration times**: L1 cache should expire faster than L2 to keep memory usage low
2. **Key naming conventions**: Use structured keys like `{entity}:{id}` for better organization
3. **Invalidate on updates**: Always invalidate cache when data changes
4. **Monitor cache hit rates**: Track L1 and L2 hit rates to optimize configuration
5. **Use shorter L1 expiration**: Keep frequently-changing data in L1 for shorter periods
6. **Leverage GetOrCreateAsync**: Simplifies cache-aside pattern and prevents cache stampede

## Performance Tips

- L1 cache is served from memory (~nanoseconds)
- L2 cache typically takes 1-10ms depending on the backend
- Use `LocalCacheExpiration` to keep hot data in L1 longer
- Consider cache warming for critical data on application startup
- Use `RemoveLocal()` to clear stale L1 data without affecting L2

## Dependencies

- Microsoft.Extensions.Caching.Abstractions (>= 8.0.0)
- Microsoft.Extensions.Caching.Memory (>= 8.0.0)
- Microsoft.Extensions.DependencyInjection.Abstractions (>= 8.0.0)
- Microsoft.Extensions.Options (>= 8.0.0)
- System.Text.Json (>= 8.0.0)
- StackExchange.Redis (>= 2.8.0) - For Lua script support

## Documentation

- [Main Documentation](README.md) - This file
- [Cache Notifications Guide](Notifications/README_NOTIFICATIONS.md) - Distributed cache invalidation
- [Lua Script Guide](LuaScripting/README_LUA.md) - Complete Lua scripting documentation
- [Redis Cluster Guide](Clustering/README_CLUSTER.md) - Redis cluster/partitioning support

## License

MIT License
