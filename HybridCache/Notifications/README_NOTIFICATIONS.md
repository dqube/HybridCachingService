# Cache Change Notifications

Complete guide for using cache change notifications to keep distributed instances in sync and automatically invalidate L1 caches.

## Table of Contents

- [Overview](#overview)
- [How It Works](#how-it-works)
- [Setup](#setup)
- [Notification Types](#notification-types)
- [Configuration](#configuration)
- [Examples](#examples)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

## Overview

Cache change notifications solve a critical problem in distributed caching: **L1 cache inconsistency across multiple application instances**.

### The Problem

Without notifications:
```
Instance A: Updates cache → L2 updated, but Instance B's L1 still has old data
Instance B: Reads from L1 cache → Gets stale data ❌
```

### The Solution

With notifications:
```
Instance A: Updates cache → Publishes notification via Redis pub/sub
Instance B: Receives notification → Automatically invalidates L1 cache
Instance B: Next read → Gets fresh data from L2 ✅
```

## How It Works

### Architecture

```
┌─────────────────────────────────────┐
│     Instance A                      │
│  ┌─────────┐    ┌──────────────┐  │
│  │L1 Cache │    │  Publisher   │  │
│  └─────────┘    └──────┬───────┘  │
└───────────────────────┼─────────────┘
                        │
                   Redis Pub/Sub
                   (notifications)
                        │
┌───────────────────────┼─────────────┐
│     Instance B        │             │
│  ┌─────────┐    ┌────▼─────────┐  │
│  │L1 Cache │◄───│  Subscriber  │  │
│  └─────────┘    └──────────────┘  │
└─────────────────────────────────────┘
```

### Event Flow

1. **Instance A** modifies cache (Set/Remove)
2. **Publisher** publishes notification to Redis channel
3. **Redis** broadcasts to all subscribers
4. **Instance B** receives notification
5. **Subscriber** invalidates L1 cache automatically
6. Next read on **Instance B** fetches fresh data from L2

## Setup

### Basic Setup

```csharp
// Startup.cs or Program.cs
services.AddHybridCacheWithRedis("localhost:6379", options =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(10);
});

// Add cache notifications
services.AddCacheNotifications(options =>
{
    options.EnableNotifications = true;
    options.AutoInvalidateL1OnNotification = true;
    options.NotificationChannel = "cache:notifications";
});
```

### With Custom Configuration

```csharp
services.AddCacheNotifications(options =>
{
    options.EnableNotifications = true;
    options.AutoInvalidateL1OnNotification = true;
    options.NotificationChannel = "myapp:cache:events";
    options.InstanceId = Environment.MachineName; // Custom instance ID
    options.IgnoreSelfNotifications = true;

    // Only notify on these operations
    options.NotifyOnOperations = new[]
    {
        CacheOperation.Set,
        CacheOperation.Remove
    };

    // Only send notifications for specific keys
    options.IncludeKeyPatterns = new[]
    {
        "user:*",
        "product:*",
        "session:*"
    };

    // Exclude temporary/internal keys
    options.ExcludeKeyPatterns = new[]
    {
        "temp:*",
        "internal:*"
    };
});
```

## Notification Types

### CacheOperation Enum

```csharp
public enum CacheOperation
{
    Set,           // Value added or updated
    Remove,        // Value removed
    Expire,        // Value expired
    BulkRemove,    // Multiple values removed
    Clear,         // Cache cleared
    Refresh        // Value refreshed (expiration extended)
}
```

### Notification Structure

```csharp
public class CacheChangeNotification
{
    public CacheOperation Operation { get; set; }
    public string Key { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? SourceInstance { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public TimeSpan? Expiration { get; set; }
}
```

## Configuration

### CacheNotificationOptions

```csharp
public class CacheNotificationOptions
{
    // Enable/disable notifications
    public bool EnableNotifications { get; set; } = false;

    // Redis pub/sub channel name
    public string NotificationChannel { get; set; } = "cache:notifications";

    // Unique instance identifier
    public string? InstanceId { get; set; }

    // Auto-invalidate L1 cache when notification received
    public bool AutoInvalidateL1OnNotification { get; set; } = true;

    // Which operations trigger notifications
    public CacheOperation[] NotifyOnOperations { get; set; }

    // Ignore notifications from same instance
    public bool IgnoreSelfNotifications { get; set; } = true;

    // Key patterns to include (wildcards supported)
    public string[]? IncludeKeyPatterns { get; set; }

    // Key patterns to exclude
    public string[]? ExcludeKeyPatterns { get; set; }
}
```

## Examples

### Example 1: Basic Multi-Instance Synchronization

```csharp
// Instance A
public class UserService
{
    private readonly IHybridCache _cache;

    public async Task UpdateUserAsync(int userId, User user)
    {
        // Update database
        await _db.Users.UpdateAsync(user);

        // Update cache - notification automatically sent
        await _cache.SetAsync($"user:{userId}", user);

        // All other instances will automatically invalidate their L1 cache
    }
}

// Instance B
// When Instance A updates, this instance automatically:
// 1. Receives notification
// 2. Invalidates L1 cache for "user:{userId}"
// 3. Next GetAsync() fetches fresh data from L2
```

### Example 2: Custom Notification Handler

```csharp
public class CustomCacheNotificationHandler : ICacheNotificationHandler
{
    private readonly ILogger<CustomCacheNotificationHandler> _logger;
    private readonly IEventBus _eventBus;

    public CustomCacheNotificationHandler(
        ILogger<CustomCacheNotificationHandler> logger,
        IEventBus eventBus)
    {
        _logger = logger;
        _eventBus = eventBus;
    }

    public async Task HandleNotificationAsync(
        CacheChangeNotification notification,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Cache changed: {Operation} on {Key} from {Source}",
            notification.Operation,
            notification.Key,
            notification.SourceInstance);

        // Custom logic: publish domain event
        if (notification.Key.StartsWith("user:"))
        {
            var userId = notification.Key.Split(':')[1];
            await _eventBus.PublishAsync(new UserCacheChangedEvent
            {
                UserId = int.Parse(userId),
                Operation = notification.Operation.ToString()
            });
        }

        // Custom logic: update analytics
        await _analytics.TrackCacheInvalidationAsync(notification);
    }
}

// Register custom handler
services.AddSingleton<ICacheNotificationHandler, CustomCacheNotificationHandler>();
```

### Example 3: Selective Notifications by Pattern

```csharp
services.AddCacheNotifications(options =>
{
    options.EnableNotifications = true;

    // Only notify for user and product caches
    options.IncludeKeyPatterns = new[]
    {
        "user:*",
        "product:*"
    };

    // Don't notify for temporary data
    options.ExcludeKeyPatterns = new[]
    {
        "temp:*",
        "cache:stats:*"
    };
});

// This WILL send notification (matches pattern)
await cache.SetAsync("user:123", userData);

// This will NOT send notification (excluded pattern)
await cache.SetAsync("temp:processing:abc", tempData);
```

### Example 4: Multi-Tenant Cache Invalidation

```csharp
public class TenantCacheService
{
    private readonly IHybridCache _cache;

    public async Task InvalidateTenantCacheAsync(string tenantId)
    {
        // Remove all tenant cache entries
        var pattern = $"{{tenant:{tenantId}}}:*";

        // This would require a custom method or Lua script
        await InvalidateCacheByPatternAsync(pattern);

        // Publish notification for all instances
        // (handled automatically by the cache)
    }

    public async Task UpdateTenantSettingsAsync(
        string tenantId,
        TenantSettings settings)
    {
        await _db.UpdateSettingsAsync(tenantId, settings);

        // Update cache - all instances notified
        await _cache.SetAsync(
            $"{{tenant:{tenantId}}}:settings",
            settings
        );
    }
}
```

### Example 5: Notification Metrics and Monitoring

```csharp
public class CacheNotificationMetrics : ICacheNotificationHandler
{
    private readonly IMetrics _metrics;

    public async Task HandleNotificationAsync(
        CacheChangeNotification notification,
        CancellationToken cancellationToken = default)
    {
        // Track notification metrics
        _metrics.Increment("cache.notifications.received", 1, new Dictionary<string, string>
        {
            { "operation", notification.Operation.ToString() },
            { "source", notification.SourceInstance ?? "unknown" }
        });

        // Track L1 invalidations
        if (notification.Operation == CacheOperation.Remove)
        {
            _metrics.Increment("cache.l1.invalidations", 1);
        }

        // Measure notification latency
        var latency = DateTimeOffset.UtcNow - notification.Timestamp;
        _metrics.Histogram("cache.notification.latency_ms", latency.TotalMilliseconds);

        await Task.CompletedTask;
    }
}
```

### Example 6: Graceful Degradation

```csharp
public class ResilientCacheService
{
    private readonly IHybridCache _cache;
    private readonly ILogger _logger;

    public async Task<T?> GetWithFallbackAsync<T>(string key)
    {
        try
        {
            return await _cache.GetAsync<T>(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed for key: {Key}", key);

            // If notifications fail, system still works
            // just with potentially stale L1 caches
            return default;
        }
    }

    public async Task SetWithNotificationAsync<T>(string key, T value)
    {
        try
        {
            await _cache.SetAsync(key, value);
            // Notification sent automatically
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache update failed for key: {Key}", key);

            // Consider: should we throw? Or just log and continue?
            // Depends on your requirements
        }
    }
}
```

## Best Practices

### 1. Use Appropriate Key Patterns

```csharp
// ✅ GOOD: Structured keys easy to filter
"user:123"
"product:456"
"session:abc"

// ❌ BAD: Inconsistent or hard to filter
"userdata_123"
"Product-456"
"ABC_session"
```

### 2. Configure Selective Notifications

```csharp
// ✅ GOOD: Only notify for important cache entries
options.IncludeKeyPatterns = new[] { "user:*", "order:*" };

// ❌ BAD: Notifying for everything (high overhead)
options.IncludeKeyPatterns = null; // All keys!
```

### 3. Handle Notification Failures Gracefully

```csharp
// Notifications are fire-and-forget
// System continues working even if notifications fail
// L1 caches just might be stale for longer

// Monitor notification failures
services.AddLogging(builder =>
{
    builder.AddFilter("HybridCache.Notifications", LogLevel.Warning);
});
```

### 4. Use IgnoreSelfNotifications

```csharp
// ✅ GOOD: Avoid invalidating cache you just set
options.IgnoreSelfNotifications = true;

// When Instance A sets a value:
// - L1 and L2 are updated
// - Notification sent
// - Instance A ignores its own notification
// - Other instances invalidate their L1
```

### 5. Set Unique Instance IDs

```csharp
options.InstanceId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}";

// Helps with:
// - Debugging
// - Monitoring
// - Avoiding self-invalidation loops
```

## Performance Considerations

### Notification Overhead

- **Redis Pub/Sub**: ~0.1-1ms latency
- **Network**: Minimal bandwidth (notifications are small)
- **L1 Invalidation**: Microseconds

### When to Use Notifications

✅ **Use when:**
- Multiple application instances
- L1 cache consistency is important
- Data changes relatively infrequently

❌ **Consider alternatives when:**
- Single instance (notifications unnecessary)
- L1 TTL is very short (<1 second)
- Data changes extremely frequently (>1000/sec)

### Optimization Tips

```csharp
// 1. Use selective patterns
options.IncludeKeyPatterns = new[] { "critical:*" };

// 2. Longer L1 expiration for read-heavy workloads
cacheOptions.DefaultLocalExpiration = TimeSpan.FromMinutes(5);

// 3. Shorter L1 expiration for write-heavy workloads
cacheOptions.DefaultLocalExpiration = TimeSpan.FromSeconds(30);
```

## Troubleshooting

### Notifications Not Received

**Symptoms**: L1 caches not invalidating

**Checklist:**
1. ✅ `EnableNotifications = true`
2. ✅ Redis connection working
3. ✅ Same notification channel on all instances
4. ✅ Operation included in `NotifyOnOperations`
5. ✅ Key matches `IncludeKeyPatterns`
6. ✅ Key doesn't match `ExcludeKeyPatterns`

**Debug:**
```csharp
// Enable detailed logging
builder.Services.AddLogging(config =>
{
    config.AddFilter("HybridCache.Notifications", LogLevel.Debug);
});
```

### Notifications Too Frequent

**Symptoms**: High Redis pub/sub traffic

**Solutions:**
```csharp
// 1. Use selective patterns
options.IncludeKeyPatterns = new[] { "important:*" };

// 2. Reduce notification types
options.NotifyOnOperations = new[] { CacheOperation.Remove };

// 3. Use longer L1 expiration (fewer updates needed)
```

### Self-Invalidation Loops

**Symptoms**: L1 cache constantly being invalidated

**Solution:**
```csharp
// Ensure this is set
options.IgnoreSelfNotifications = true;

// Use unique instance IDs
options.InstanceId = Guid.NewGuid().ToString();
```

## Advanced Scenarios

### Delayed Notifications

```csharp
// For eventually consistent scenarios
public class DelayedNotificationPublisher
{
    public async Task PublishWithDelayAsync(
        CacheChangeNotification notification,
        TimeSpan delay)
    {
        await Task.Delay(delay);
        await _publisher.PublishAsync(notification);
    }
}
```

### Batched Notifications

```csharp
// For bulk operations
public async Task InvalidateMultipleAsync(string[] keys)
{
    foreach (var key in keys)
    {
        await _cache.RemoveAsync(key);
        // Each removal sends notification
    }

    // Alternative: single notification for bulk operation
    await _publisher.PublishAsync(new CacheChangeNotification
    {
        Operation = CacheOperation.BulkRemove,
        Key = string.Join(",", keys),
        Metadata = new Dictionary<string, string>
        {
            { "count", keys.Length.ToString() }
        }
    });
}
```

## Testing

### Unit Testing

```csharp
[Fact]
public async Task SetAsync_ShouldPublishNotification()
{
    // Arrange
    var publisherMock = new Mock<ICacheNotificationPublisher>();
    var cache = new DefaultHybridCache(
        // ... other dependencies
        notificationPublisher: publisherMock.Object
    );

    // Act
    await cache.SetAsync("test:key", "value");

    // Assert
    publisherMock.Verify(p => p.PublishAsync(
        It.Is<CacheChangeNotification>(n =>
            n.Operation == CacheOperation.Set &&
            n.Key == "test:key"),
        It.IsAny<CancellationToken>()),
        Times.Once);
}
```

### Integration Testing

```csharp
[Fact]
public async Task Notification_ShouldInvalidateL1Across Instances()
{
    // Create two cache instances (simulating two servers)
    var instance1 = CreateCacheInstance("instance-1");
    var instance2 = CreateCacheInstance("instance-2");

    // Instance 1: Set value
    await instance1.SetAsync("test:key", "value1");

    // Instance 2: Should have value in L1
    var value2 = await instance2.GetAsync<string>("test:key");
    Assert.Equal("value1", value2);

    // Instance 1: Update value
    await instance1.SetAsync("test:key", "value2");

    // Wait for notification
    await Task.Delay(100);

    // Instance 2: L1 should be invalidated, gets new value
    var updatedValue = await instance2.GetAsync<string>("test:key");
    Assert.Equal("value2", updatedValue);
}
```

## Summary

Cache change notifications provide:
- ✅ **Automatic L1 invalidation** across instances
- ✅ **Strong consistency** for distributed caches
- ✅ **Simple setup** with Redis pub/sub
- ✅ **Flexible configuration** with patterns and filters
- ✅ **Extensible** with custom handlers
- ✅ **Monitoring** capabilities

Perfect for multi-instance applications where cache consistency matters!

---

**See Also:**
- [Main Documentation](../README.md)
- [Lua Scripts Guide](../LuaScripting/README_LUA.md)
- [Cluster Support](../Clustering/README_CLUSTER.md)
