# Cache Warming

## Overview

Cache warming is a performance optimization technique that pre-loads frequently accessed data from the L2 (distributed) cache into the L1 (local memory) cache. This ensures that your application has hot data readily available in memory, reducing latency and improving response times.

## Table of Contents

- [Why Cache Warming?](#why-cache-warming)
- [How It Works](#how-it-works)
- [Getting Started](#getting-started)
- [Configuration Options](#configuration-options)
- [Usage Examples](#usage-examples)
- [Monitoring & Statistics](#monitoring--statistics)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

---

## Why Cache Warming?

### The Problem

In a hybrid caching architecture:
- **L1 (Memory) Cache**: Fast but initially empty after application restart
- **L2 (Redis) Cache**: Persistent but slower due to network latency

When your application starts or restarts, the L1 cache is cold (empty). The first requests for cached data will experience:
- Higher latency (must fetch from Redis)
- Increased load on Redis
- Potential "thundering herd" if many instances restart simultaneously

### The Solution

Cache warming proactively loads frequently accessed keys from Redis into local memory:
- **Faster startup**: Critical data is available immediately
- **Reduced latency**: First requests hit L1 instead of L2
- **Smoother traffic**: Gradual warming prevents Redis spikes
- **Better user experience**: No "cold start" slowness

### Common Use Cases

1. **Application Startup**: Load critical configuration and reference data
2. **Periodic Refresh**: Keep hot data fresh in memory every few minutes
3. **After Deployments**: Quickly restore cache state after rolling updates
4. **Scale-Out Events**: New instances get warmed caches immediately

---

## How It Works

```
┌─────────────────────────────────────────────────────────┐
│  CacheWarmerBackgroundService (Hosted Service)          │
│  - Runs on configurable interval (default: 5 min)       │
│  - Initial delay: 30 seconds after startup              │
└────────────────┬────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────┐
│  RedisCacheWarmer                                       │
│  1. Scan Redis for keys matching patterns              │
│  2. Fetch values in batches                            │
│  3. Store in local memory cache                        │
│  4. Preserve TTL from Redis                            │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
     ┌─────────────────────────┐
     │  Pattern Matching       │
     │  - Include: user:*      │
     │  - Exclude: temp:*      │
     │  - Max keys: 1000       │
     └─────────────────────────┘
                  │
                  ▼
     ┌─────────────────────────┐
     │  Batch Processing       │
     │  - Batch size: 100      │
     │  - Parallel fetch       │
     │  - Timeout: 5 seconds   │
     └─────────────────────────┘
                  │
                  ▼
     ┌─────────────────────────┐
     │  L1 Memory Cache        │
     │  ✓ Keys loaded          │
     │  ✓ TTL preserved        │
     │  ✓ Ready for requests   │
     └─────────────────────────┘
```

### Warming Cycle

1. **Initialization**: Background service starts with configured delay
2. **Pattern Resolution**: Builds Redis scan patterns from configuration
3. **Key Scanning**: Uses Redis SCAN to iterate over matching keys
4. **Batch Processing**: Groups keys into batches for efficient fetching
5. **Value Loading**: Fetches values from Redis with timeout protection
6. **TTL Preservation**: Reads Redis TTL and applies to L1 cache
7. **Memory Storage**: Stores values in local memory cache
8. **Statistics**: Tracks keys scanned, loaded, skipped, and errors
9. **Scheduling**: Waits for configured interval, then repeats

---

## Getting Started

### 1. Install the Package

```bash
dotnet add package HybridCache
```

### 2. Register Services

```csharp
using HybridCache;
using HybridCache.CacheWarming;

var builder = WebApplication.CreateBuilder(args);

// Register hybrid cache with Redis
builder.Services.AddHybridCacheWithRedis(
    builder.Configuration.GetConnectionString("Redis"),
    options =>
    {
        options.KeyPrefix = "myapp:";
        options.DefaultLocalExpiration = TimeSpan.FromMinutes(10);
    });

// Enable cache warming
builder.Services.AddCacheWarming(options =>
{
    options.EnableAutoWarming = true;
    options.WarmingInterval = TimeSpan.FromMinutes(5);
    options.IncludePatterns = new[] { "user:*", "product:*" };
});

var app = builder.Build();
app.Run();
```

### 3. Verify It's Working

Check logs on application startup:

```
[Information] Cache warming background service started. Initial delay: 00:00:30, Interval: 00:05:00
[Information] Starting cache warming cycle at 2024-01-15 10:30:00
[Information] Cache warming completed successfully. Loaded: 523, Skipped: 12, Scanned: 535, Duration: 1234ms
```

---

## Configuration Options

### CacheWarmerOptions

```csharp
public class CacheWarmerOptions
{
    // Core Settings
    public bool EnableAutoWarming { get; set; } = true;
    public TimeSpan WarmingInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);

    // Pattern Matching
    public string[] IncludePatterns { get; set; } = Array.Empty<string>();
    public string[] ExcludePatterns { get; set; } = Array.Empty<string>();
    public string[] KeyPrefixes { get; set; } = Array.Empty<string>();

    // Performance Limits
    public int MaxKeysPerWarming { get; set; } = 1000;
    public int BatchSize { get; set; } = 100;
    public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // Expiration Control
    public TimeSpan? L1Expiration { get; set; } = null;

    // Error Handling
    public bool ContinueOnError { get; set; } = true;

    // Logging
    public bool EnableDetailedLogging { get; set; } = false;
}
```

### Option Details

#### **EnableAutoWarming**
- **Type**: `bool`
- **Default**: `true`
- **Description**: Enable or disable automatic cache warming on startup and periodic intervals
- **Example**:
  ```csharp
  options.EnableAutoWarming = false; // Disable auto-warming, use manual trigger only
  ```

#### **WarmingInterval**
- **Type**: `TimeSpan`
- **Default**: `5 minutes`
- **Description**: How often the warming cycle runs
- **Recommendations**:
  - High-traffic apps: 2-3 minutes
  - Medium-traffic apps: 5-10 minutes
  - Low-traffic apps: 15-30 minutes
- **Example**:
  ```csharp
  options.WarmingInterval = TimeSpan.FromMinutes(3);
  ```

#### **InitialDelay**
- **Type**: `TimeSpan`
- **Default**: `30 seconds`
- **Description**: Delay before the first warming operation (allows application to fully start)
- **Example**:
  ```csharp
  options.InitialDelay = TimeSpan.FromSeconds(10); // Quick startup
  ```

#### **IncludePatterns**
- **Type**: `string[]`
- **Default**: Empty (warms all keys with prefix)
- **Description**: Redis key patterns to include in warming
- **Pattern Syntax**: Supports wildcards (`*`)
- **Example**:
  ```csharp
  options.IncludePatterns = new[]
  {
      "user:*",           // All user keys
      "product:active:*", // Active products only
      "config:*",         // All configuration
      "cache:static:*"    // Static data
  };
  ```

#### **ExcludePatterns**
- **Type**: `string[]`
- **Default**: Empty
- **Description**: Redis key patterns to exclude from warming
- **Example**:
  ```csharp
  options.ExcludePatterns = new[]
  {
      "temp:*",           // Temporary data
      "session:*",        // User sessions
      "lock:*",           // Distributed locks
      "queue:*"           // Job queues
  };
  ```

#### **KeyPrefixes**
- **Type**: `string[]`
- **Default**: Empty (uses `HybridCacheOptions.KeyPrefix`)
- **Description**: Specific key prefixes to warm (overrides global prefix)
- **Example**:
  ```csharp
  options.KeyPrefixes = new[] { "app1:", "app2:" }; // Multi-tenant scenario
  ```

#### **MaxKeysPerWarming**
- **Type**: `int`
- **Default**: `1000`
- **Description**: Maximum number of keys to warm in a single cycle (prevents memory overflow)
- **Recommendations**:
  - Small instances (< 1GB memory): 500-1000
  - Medium instances (1-4GB memory): 1000-5000
  - Large instances (> 4GB memory): 5000-10000
- **Example**:
  ```csharp
  options.MaxKeysPerWarming = 2000;
  ```

#### **BatchSize**
- **Type**: `int`
- **Default**: `100`
- **Description**: Number of keys to fetch from Redis in parallel
- **Recommendations**:
  - Redis cluster: 50-100 (lower to distribute load)
  - Single Redis instance: 100-200
- **Example**:
  ```csharp
  options.BatchSize = 50; // Smaller batches for Redis cluster
  ```

#### **FetchTimeout**
- **Type**: `TimeSpan`
- **Default**: `5 seconds`
- **Description**: Timeout for fetching each individual key
- **Example**:
  ```csharp
  options.FetchTimeout = TimeSpan.FromSeconds(3); // Faster timeout
  ```

#### **L1Expiration**
- **Type**: `TimeSpan?`
- **Default**: `null` (uses Redis TTL or default expiration)
- **Description**: Override expiration time for warmed keys in L1 cache
- **Use Cases**:
  - Force shorter L1 TTL than L2
  - Uniform expiration for all warmed keys
- **Example**:
  ```csharp
  options.L1Expiration = TimeSpan.FromMinutes(5); // All warmed keys expire in 5 min
  ```

#### **ContinueOnError**
- **Type**: `bool`
- **Default**: `true`
- **Description**: Continue warming even if individual keys fail
- **Example**:
  ```csharp
  options.ContinueOnError = false; // Stop on first error
  ```

#### **EnableDetailedLogging**
- **Type**: `bool`
- **Default**: `false`
- **Description**: Log each key being warmed (verbose, use for debugging only)
- **Example**:
  ```csharp
  options.EnableDetailedLogging = true; // Enable for troubleshooting
  ```

---

## Usage Examples

### Example 1: Basic Configuration

```csharp
// Warm all keys with default settings
builder.Services.AddCacheWarming(options =>
{
    // Uses defaults:
    // - Interval: 5 minutes
    // - Max keys: 1000
    // - Batch size: 100
});
```

### Example 2: High-Traffic E-Commerce Site

```csharp
builder.Services.AddCacheWarming(options =>
{
    options.EnableAutoWarming = true;
    options.WarmingInterval = TimeSpan.FromMinutes(2); // Frequent refresh
    options.InitialDelay = TimeSpan.FromSeconds(15);   // Quick startup

    // Warm critical business data
    options.IncludePatterns = new[]
    {
        "product:featured:*",     // Featured products
        "category:*",             // All categories
        "config:*",               // App configuration
        "pricing:*",              // Pricing data
        "inventory:popular:*"     // Popular items inventory
    };

    // Skip volatile data
    options.ExcludePatterns = new[]
    {
        "cart:*",                 // Shopping carts
        "session:*",              // User sessions
        "analytics:temp:*"        // Temporary analytics
    };

    options.MaxKeysPerWarming = 5000;  // Large catalog
    options.BatchSize = 200;           // Large batches
    options.L1Expiration = TimeSpan.FromMinutes(3); // Short L1 TTL
    options.ContinueOnError = true;
    options.EnableDetailedLogging = false;
});
```

### Example 3: Multi-Tenant SaaS Application

```csharp
builder.Services.AddCacheWarming(options =>
{
    options.EnableAutoWarming = true;
    options.WarmingInterval = TimeSpan.FromMinutes(10);

    // Warm specific tenant data
    options.IncludePatterns = new[]
    {
        "tenant:123:config:*",    // Tenant 123 configuration
        "tenant:123:users:*",     // Tenant 123 users
        "tenant:456:config:*",    // Tenant 456 configuration
        "shared:*"                // Shared reference data
    };

    options.MaxKeysPerWarming = 2000;
    options.BatchSize = 100;
    options.ContinueOnError = true;
});
```

### Example 4: Startup-Only Warming

```csharp
builder.Services.AddCacheWarming(options =>
{
    options.EnableAutoWarming = false; // No periodic warming
    options.IncludePatterns = new[] { "config:*", "reference:*" };
});

// Manually trigger warming after startup
var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(async () =>
{
    var warmer = app.Services.GetRequiredService<CacheWarmerBackgroundService>();
    await warmer.TriggerWarmingAsync();
});

app.Run();
```

### Example 5: Conditional Warming Based on Environment

```csharp
var isProduction = builder.Environment.IsProduction();

builder.Services.AddCacheWarming(options =>
{
    options.EnableAutoWarming = isProduction; // Only in production

    options.WarmingInterval = isProduction
        ? TimeSpan.FromMinutes(5)     // Production: frequent
        : TimeSpan.FromMinutes(30);   // Development: rare

    options.IncludePatterns = new[] { "user:*", "product:*" };
    options.MaxKeysPerWarming = isProduction ? 5000 : 100;
    options.EnableDetailedLogging = !isProduction; // Debug in dev
});
```

---

## Monitoring & Statistics

### Access Statistics via Dependency Injection

```csharp
[ApiController]
[Route("api/cache")]
public class CacheManagementController : ControllerBase
{
    private readonly CacheWarmerBackgroundService _warmerService;

    public CacheManagementController(CacheWarmerBackgroundService warmerService)
    {
        _warmerService = warmerService;
    }

    [HttpGet("warming/stats")]
    public IActionResult GetWarmingStatistics()
    {
        var stats = _warmerService.GetStatistics();
        return Ok(stats);
    }

    [HttpPost("warming/trigger")]
    public async Task<IActionResult> TriggerWarmingManually()
    {
        var result = await _warmerService.TriggerWarmingAsync();
        return Ok(new
        {
            keysScanned = result.KeysScanned,
            keysLoaded = result.KeysLoaded,
            keysSkipped = result.KeysSkipped,
            duration = result.Duration.TotalMilliseconds,
            errors = result.Errors
        });
    }

    [HttpGet("warming/status")]
    public IActionResult GetWarmingStatus()
    {
        return Ok(new
        {
            enabled = _warmerService.IsEnabled,
            lastRun = _warmerService.LastRun,
            nextRun = _warmerService.NextRun,
            lastResult = _warmerService.LastResult
        });
    }
}
```

### Statistics Response Example

```json
{
  "enabled": true,
  "lastRun": "2024-01-15T10:35:00Z",
  "nextRun": "2024-01-15T10:40:00Z",
  "lastResult": {
    "keysScanned": 1250,
    "keysLoaded": 1000,
    "keysSkipped": 250,
    "duration": 2340.5,
    "completedAt": "2024-01-15T10:35:02Z",
    "errorCount": 0,
    "errors": []
  },
  "configuration": {
    "warmingInterval": 300,
    "initialDelay": 30,
    "maxKeysPerWarming": 1000,
    "batchSize": 100,
    "includePatterns": ["user:*", "product:*"],
    "excludePatterns": ["temp:*"]
  }
}
```

### Health Check Integration

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

public class CacheWarmingHealthCheck : IHealthCheck
{
    private readonly CacheWarmerBackgroundService _warmerService;

    public CacheWarmingHealthCheck(CacheWarmerBackgroundService warmerService)
    {
        _warmerService = warmerService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_warmerService.IsEnabled)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy("Cache warming is disabled"));
        }

        var lastResult = _warmerService.LastResult;
        if (lastResult == null)
        {
            return Task.FromResult(
                HealthCheckResult.Degraded("Cache warming has not run yet"));
        }

        if (lastResult.Errors.Count > 0)
        {
            return Task.FromResult(
                HealthCheckResult.Degraded(
                    $"Cache warming completed with {lastResult.Errors.Count} errors",
                    data: new Dictionary<string, object>
                    {
                        ["keysLoaded"] = lastResult.KeysLoaded,
                        ["errors"] = lastResult.Errors
                    }));
        }

        return Task.FromResult(
            HealthCheckResult.Healthy(
                $"Cache warming healthy. {lastResult.KeysLoaded} keys loaded",
                data: new Dictionary<string, object>
                {
                    ["lastRun"] = _warmerService.LastRun,
                    ["keysLoaded"] = lastResult.KeysLoaded,
                    ["duration"] = lastResult.Duration.TotalMilliseconds
                }));
    }
}

// Register health check
builder.Services.AddHealthChecks()
    .AddCheck<CacheWarmingHealthCheck>("cache_warming");
```

### Application Insights Logging

```csharp
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

public class CacheWarmingTelemetry
{
    private readonly TelemetryClient _telemetry;
    private readonly CacheWarmerBackgroundService _warmerService;

    public CacheWarmingTelemetry(
        TelemetryClient telemetry,
        CacheWarmerBackgroundService warmerService)
    {
        _telemetry = telemetry;
        _warmerService = warmerService;
    }

    public void TrackWarmingCycle()
    {
        var result = _warmerService.LastResult;
        if (result == null) return;

        // Track as custom event
        _telemetry.TrackEvent("CacheWarming",
            properties: new Dictionary<string, string>
            {
                ["Status"] = result.Errors.Count > 0 ? "WithErrors" : "Success"
            },
            metrics: new Dictionary<string, double>
            {
                ["KeysScanned"] = result.KeysScanned,
                ["KeysLoaded"] = result.KeysLoaded,
                ["KeysSkipped"] = result.KeysSkipped,
                ["Duration"] = result.Duration.TotalMilliseconds,
                ["ErrorCount"] = result.Errors.Count
            });

        // Track as metric
        _telemetry.TrackMetric("CacheWarming.KeysLoaded", result.KeysLoaded);
        _telemetry.TrackMetric("CacheWarming.Duration", result.Duration.TotalMilliseconds);
    }
}
```

---

## Best Practices

### 1. Pattern Selection Strategy

**DO**: Warm frequently accessed, relatively static data
```csharp
options.IncludePatterns = new[]
{
    "config:*",          // App configuration (rarely changes)
    "category:*",        // Product categories (stable)
    "user:premium:*",    // Premium users (smaller set)
    "reference:*"        // Reference data (static)
};
```

**DON'T**: Warm volatile or user-specific data
```csharp
// ❌ BAD - These change too frequently
options.IncludePatterns = new[]
{
    "cart:*",            // Shopping carts (user-specific, volatile)
    "session:*",         // Sessions (temporary)
    "lock:*",            // Distributed locks (ephemeral)
    "analytics:*"        // Analytics counters (constantly changing)
};
```

### 2. Memory Management

**Calculate memory usage before warming**:
```csharp
// Estimate: 1000 keys × 10 KB average value = 10 MB
// Add 30% overhead for metadata = 13 MB total

options.MaxKeysPerWarming = 1000; // Safe for apps with > 100 MB available memory
```

**Monitor memory after enabling warming**:
```bash
# Windows
dotnet-counters monitor --process-id <PID> System.Runtime

# Linux
cat /proc/<PID>/status | grep VmRSS
```

### 3. Timing Recommendations

**Startup scenario**:
```csharp
options.InitialDelay = TimeSpan.FromSeconds(30);  // Allow dependencies to initialize
options.WarmingInterval = TimeSpan.FromMinutes(5); // Regular refresh
```

**High-traffic scenario**:
```csharp
options.InitialDelay = TimeSpan.FromSeconds(10);  // Quick startup
options.WarmingInterval = TimeSpan.FromMinutes(2); // Frequent refresh
```

**Low-traffic scenario**:
```csharp
options.InitialDelay = TimeSpan.FromMinutes(1);    // Relaxed startup
options.WarmingInterval = TimeSpan.FromMinutes(15); // Infrequent refresh
```

### 4. Batch Size Tuning

**Redis standalone**:
```csharp
options.BatchSize = 200; // Larger batches OK
```

**Redis cluster**:
```csharp
options.BatchSize = 50; // Smaller batches to distribute load
```

**High latency network**:
```csharp
options.BatchSize = 100;
options.FetchTimeout = TimeSpan.FromSeconds(10); // Longer timeout
```

### 5. Error Handling

**Production recommendation**:
```csharp
options.ContinueOnError = true;  // Don't fail entire warming on single key error
options.EnableDetailedLogging = false; // Reduce log noise
```

**Development/troubleshooting**:
```csharp
options.ContinueOnError = false; // Stop on first error for debugging
options.EnableDetailedLogging = true; // See each key operation
```

### 6. Multi-Environment Configuration

```csharp
builder.Services.AddCacheWarming(options =>
{
    var env = builder.Environment;

    options.EnableAutoWarming = env.IsProduction() || env.IsStaging();

    options.WarmingInterval = env.IsProduction()
        ? TimeSpan.FromMinutes(5)
        : TimeSpan.FromMinutes(30);

    options.MaxKeysPerWarming = env.IsProduction() ? 5000 : 100;
    options.EnableDetailedLogging = env.IsDevelopment();
});
```

### 7. Testing Cache Warming

```csharp
// Integration test example
[Fact]
public async Task CacheWarming_ShouldLoadKeysIntoMemory()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddHybridCacheWithRedis("localhost:6379");
    services.AddCacheWarming(options =>
    {
        options.EnableAutoWarming = false; // Manual trigger only
        options.IncludePatterns = new[] { "test:*" };
    });

    var provider = services.BuildServiceProvider();
    var warmer = provider.GetRequiredService<ICacheWarmer>();
    var cache = provider.GetRequiredService<IHybridCache>();

    // Pre-populate Redis
    await cache.SetAsync("test:key1", "value1");
    await cache.SetAsync("test:key2", "value2");

    // Act
    var result = await warmer.WarmCacheAsync();

    // Assert
    Assert.Equal(2, result.KeysLoaded);
    Assert.Equal(0, result.Errors.Count);
}
```

---

## Troubleshooting

### Issue: No Keys Being Loaded

**Symptoms**:
```
Cache warming completed successfully. Loaded: 0, Skipped: 0, Scanned: 0
```

**Possible Causes**:
1. No keys in Redis matching patterns
2. Key prefix mismatch
3. Exclude patterns too broad

**Solutions**:
```csharp
// 1. Check Redis for actual keys
// redis-cli KEYS "myapp:*"

// 2. Verify key prefix matches
options.IncludePatterns = new[] { "user:*" };
// Ensure Redis has keys like: "myapp:user:123" (if KeyPrefix is "myapp:")

// 3. Enable detailed logging
options.EnableDetailedLogging = true;

// 4. Simplify patterns
options.IncludePatterns = new[] { "*" }; // Warm ALL keys (testing only)
options.ExcludePatterns = Array.Empty<string>(); // Remove exclusions
```

### Issue: High Memory Usage

**Symptoms**:
- Application memory increases significantly after warming
- Out of memory exceptions

**Solutions**:
```csharp
// 1. Reduce max keys
options.MaxKeysPerWarming = 500; // Lower limit

// 2. Use shorter L1 expiration
options.L1Expiration = TimeSpan.FromMinutes(2);

// 3. Be more selective with patterns
options.IncludePatterns = new[] { "config:*" }; // Only critical data

// 4. Increase warming interval
options.WarmingInterval = TimeSpan.FromMinutes(10); // Less frequent
```

### Issue: Slow Warming Operations

**Symptoms**:
```
Cache warming completed successfully. Duration: 30000ms (30 seconds)
```

**Solutions**:
```csharp
// 1. Reduce max keys
options.MaxKeysPerWarming = 500;

// 2. Increase batch size (if network allows)
options.BatchSize = 200;

// 3. Reduce fetch timeout
options.FetchTimeout = TimeSpan.FromSeconds(3);

// 4. Check Redis latency
// redis-cli --latency

// 5. Check network latency to Redis
// ping your-redis-server
```

### Issue: Frequent Errors

**Symptoms**:
```
Cache warming completed with 50 errors. Loaded: 950, Skipped: 50
```

**Solutions**:
```csharp
// 1. Enable detailed logging
options.EnableDetailedLogging = true;

// 2. Check error messages
var stats = _warmerService.GetStatistics();
var errors = stats.lastResult.errors; // Review specific errors

// 3. Increase fetch timeout
options.FetchTimeout = TimeSpan.FromSeconds(10);

// 4. Check Redis connectivity
// redis-cli PING

// 5. Verify Redis permissions
// Ensure connection string has read permissions
```

### Issue: Background Service Not Starting

**Symptoms**:
- No warming logs appearing
- Statistics show `enabled: false`

**Solutions**:
```csharp
// 1. Verify EnableAutoWarming is true
options.EnableAutoWarming = true;

// 2. Check service registration
builder.Services.AddCacheWarming(/* options */);

// 3. Verify hosted service is registered
// Should happen automatically with AddCacheWarming

// 4. Check application logs
// Look for "Cache warming is disabled" message

// 5. Manually trigger to test
var warmer = app.Services.GetRequiredService<CacheWarmerBackgroundService>();
var result = await warmer.TriggerWarmingAsync();
```

### Issue: Keys Expiring Too Quickly

**Symptoms**:
- L1 cache hits low despite warming
- Keys not available after warming

**Solutions**:
```csharp
// 1. Override L1 expiration
options.L1Expiration = TimeSpan.FromMinutes(10); // Explicit TTL

// 2. Check Redis TTL
// redis-cli TTL "myapp:user:123"

// 3. Verify default expiration
builder.Services.AddHybridCacheWithRedis(redis, cacheOpts =>
{
    cacheOpts.DefaultLocalExpiration = TimeSpan.FromMinutes(15);
});
```

---

## Performance Metrics

### Typical Performance Characteristics

| Metric | Small (< 100 keys) | Medium (100-1000 keys) | Large (1000-5000 keys) |
|--------|-------------------|----------------------|----------------------|
| **Duration** | < 1 second | 1-5 seconds | 5-15 seconds |
| **Memory Impact** | < 1 MB | 1-10 MB | 10-50 MB |
| **Redis Load** | Negligible | Low | Moderate |
| **CPU Usage** | < 5% | 5-15% | 15-30% |

### Optimization Tips

1. **Reduce warming frequency for static data**:
   ```csharp
   options.WarmingInterval = TimeSpan.FromMinutes(30); // Reference data
   ```

2. **Use exclude patterns liberally**:
   ```csharp
   options.ExcludePatterns = new[] { "temp:*", "session:*", "lock:*" };
   ```

3. **Batch size tuning**:
   - Network latency < 1ms: BatchSize = 200
   - Network latency 1-10ms: BatchSize = 100
   - Network latency > 10ms: BatchSize = 50

4. **Monitor and adjust**:
   ```csharp
   // Add Application Insights tracking
   _telemetry.TrackMetric("CacheWarming.Duration", result.Duration.TotalMilliseconds);
   _telemetry.TrackMetric("CacheWarming.KeysLoaded", result.KeysLoaded);
   ```

---

## Related Documentation

- [Main HybridCache README](../README.md)
- [Cache Notifications](../Notifications/README_NOTIFICATIONS.md)
- [Redis Clustering](../Clustering/README_CLUSTER.md)
- [Lua Scripting](../LuaScripting/README_LUA.md)

---

## Summary

Cache warming is a powerful optimization for applications using hybrid caching:

✅ **Pre-loads hot data** from Redis into memory
✅ **Reduces cold start latency** after deployments
✅ **Improves user experience** with faster response times
✅ **Configurable patterns** for selective warming
✅ **Built-in monitoring** with statistics and health checks
✅ **Error resilient** with continue-on-error support

Configure it once, and your application will maintain a warm cache automatically!
