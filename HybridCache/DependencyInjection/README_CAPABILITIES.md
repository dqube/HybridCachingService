# Capabilities-Based Registration

## Overview

The `AddHybridCacheWithCapabilities` method provides a unified, flexible way to register HybridCache with fine-grained control over which features are enabled. This is the **recommended approach** for production applications as it makes configuration explicit and easier to manage.

## Why Use Capabilities-Based Registration?

### Problems with Individual Registration Methods

**Traditional approach:**
```csharp
// Multiple registration calls - easy to miss something
services.AddHybridCacheWithRedis("localhost:6379");
services.AddCacheNotifications(options => { /* config */ });
services.AddCacheWarming(options => { /* config */ });
// Did I enable clustering? Did I configure it correctly?
```

**Problems:**
- ❌ Scattered configuration across multiple method calls
- ❌ Easy to forget to register a feature
- ❌ Unclear which features are enabled
- ❌ Hard to enable/disable features conditionally
- ❌ Difficult to configure from external sources (appsettings.json)

### Solution: Capabilities-Based Registration

**New approach:**
```csharp
services.AddHybridCacheWithCapabilities(
    "localhost:6379",
    cacheOptions => { /* cache config */ },
    capabilities =>
    {
        capabilities.EnableCacheWarming = true;     // ✓ Explicit
        capabilities.EnableNotifications = true;    // ✓ Visible
        capabilities.EnableClustering = false;      // ✓ Clear

        capabilities.CacheWarmingOptions = options => { /* config */ };
        capabilities.NotificationOptions = options => { /* config */ };
    });
```

**Benefits:**
- ✅ All configuration in one place
- ✅ Explicit control via boolean flags
- ✅ Self-documenting code
- ✅ Easy to enable/disable features
- ✅ Perfect for environment-based configuration
- ✅ Simple to bind to appsettings.json

---

## Quick Start

### Basic Usage

```csharp
using HybridCache.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHybridCacheWithCapabilities(
    builder.Configuration.GetConnectionString("Redis")!,
    cacheOptions =>
    {
        cacheOptions.KeyPrefix = "myapp:";
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(30);
        cacheOptions.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
    },
    capabilities =>
    {
        // Enable features you need
        capabilities.EnableCacheWarming = true;
        capabilities.EnableNotifications = true;
        capabilities.EnableClustering = false;

        // Configure enabled features
        capabilities.CacheWarmingOptions = options =>
        {
            options.WarmingInterval = TimeSpan.FromMinutes(5);
            options.IncludePatterns = new[] { "user:*", "product:*" };
        };

        capabilities.NotificationOptions = options =>
        {
            options.NotificationChannel = "cache:notifications";
        };
    });
```

---

## Capabilities Reference

### HybridCacheCapabilities Class

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableCacheWarming` | `bool` | `false` | Enable automatic cache warming from L2 to L1 |
| `EnableNotifications` | `bool` | `false` | Enable cache change notifications via Redis pub/sub |
| `EnableClustering` | `bool` | `false` | Enable Redis cluster support with hash slot validation |
| `CacheWarmingOptions` | `Action<CacheWarmerOptions>?` | `null` | Configuration for cache warming (only if enabled) |
| `NotificationOptions` | `Action<CacheNotificationOptions>?` | `null` | Configuration for notifications (only if enabled) |
| `ClusterOptions` | `Action<RedisClusterOptions>?` | `null` | Configuration for clustering (only if enabled) |

---

## Usage Scenarios

### Scenario 1: Enable All Features (Production)

```csharp
services.AddHybridCacheWithCapabilities(
    redisConnectionString,
    cacheOptions =>
    {
        cacheOptions.KeyPrefix = "prod:";
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(30);
    },
    capabilities =>
    {
        // Enable everything for production
        capabilities.EnableCacheWarming = true;
        capabilities.EnableNotifications = true;
        capabilities.EnableClustering = true;

        capabilities.CacheWarmingOptions = options =>
        {
            options.WarmingInterval = TimeSpan.FromMinutes(5);
            options.IncludePatterns = new[] { "user:*", "product:*", "config:*" };
            options.MaxKeysPerWarming = 5000;
        };

        capabilities.NotificationOptions = options =>
        {
            options.NotificationChannel = "prod:cache";
            options.AutoInvalidateL1OnNotification = true;
        };

        capabilities.ClusterOptions = options =>
        {
            options.IsClusterMode = true;
            options.UseHashTags = true;
        };
    });
```

### Scenario 2: Only Cache Warming (Startup Optimization)

```csharp
services.AddHybridCacheWithCapabilities(
    redisConnectionString,
    cacheOptions =>
    {
        cacheOptions.KeyPrefix = "app:";
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(15);
    },
    capabilities =>
    {
        // Only warming, no notifications or clustering
        capabilities.EnableCacheWarming = true;
        capabilities.EnableNotifications = false;
        capabilities.EnableClustering = false;

        capabilities.CacheWarmingOptions = options =>
        {
            options.WarmingInterval = TimeSpan.FromMinutes(10);
            options.IncludePatterns = new[] { "config:*", "reference:*" };
            options.MaxKeysPerWarming = 500;
        };
    });
```

### Scenario 3: Only Notifications (Multi-Instance Coordination)

```csharp
services.AddHybridCacheWithCapabilities(
    redisConnectionString,
    cacheOptions =>
    {
        cacheOptions.KeyPrefix = "app:";
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(20);
    },
    capabilities =>
    {
        // Only notifications for cache synchronization
        capabilities.EnableCacheWarming = false;
        capabilities.EnableNotifications = true;
        capabilities.EnableClustering = false;

        capabilities.NotificationOptions = options =>
        {
            options.NotificationChannel = "cache:sync";
            options.AutoInvalidateL1OnNotification = true;
            options.IgnoreSelfNotifications = true;
        };
    });
```

### Scenario 4: No Extra Features (Basic Hybrid Cache)

```csharp
services.AddHybridCacheWithCapabilities(
    redisConnectionString,
    cacheOptions =>
    {
        cacheOptions.KeyPrefix = "simple:";
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(10);
    },
    capabilities =>
    {
        // All features disabled - basic two-tier cache only
        capabilities.EnableCacheWarming = false;
        capabilities.EnableNotifications = false;
        capabilities.EnableClustering = false;
    });
```

### Scenario 5: Environment-Based Configuration

```csharp
var isProduction = builder.Environment.IsProduction();
var isClusterDeployment = builder.Configuration.GetValue<bool>("UseRedisCluster");

services.AddHybridCacheWithCapabilities(
    redisConnectionString,
    cacheOptions =>
    {
        cacheOptions.KeyPrefix = isProduction ? "prod:" : "dev:";
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(20);
    },
    capabilities =>
    {
        // Enable warming only in production
        capabilities.EnableCacheWarming = isProduction;

        // Always enable notifications
        capabilities.EnableNotifications = true;

        // Enable clustering based on deployment
        capabilities.EnableClustering = isClusterDeployment;

        if (isProduction)
        {
            capabilities.CacheWarmingOptions = options =>
            {
                options.WarmingInterval = TimeSpan.FromMinutes(5);
                options.IncludePatterns = new[] { "user:*", "product:*" };
                options.MaxKeysPerWarming = 5000;
            };
        }

        capabilities.NotificationOptions = options =>
        {
            options.NotificationChannel = isProduction ? "prod:cache" : "dev:cache";
        };
    });
```

---

## Configuration from appsettings.json

### 1. Define Configuration Class

```csharp
public class CacheCapabilitiesConfig
{
    public string KeyPrefix { get; set; } = "app:";
    public int DefaultExpirationMinutes { get; set; } = 20;

    // Capability flags
    public bool EnableWarming { get; set; } = false;
    public bool EnableNotifications { get; set; } = false;
    public bool EnableClustering { get; set; } = false;

    // Warming settings
    public int WarmingIntervalMinutes { get; set; } = 5;
    public string[] WarmingPatterns { get; set; } = Array.Empty<string>();
    public int MaxWarmingKeys { get; set; } = 1000;

    // Notification settings
    public string NotificationChannel { get; set; } = "cache:notifications";

    // Clustering settings
    public bool UseHashTags { get; set; } = true;
}
```

### 2. Add to appsettings.json

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "CacheCapabilities": {
    "KeyPrefix": "myapp:",
    "DefaultExpirationMinutes": 30,
    "EnableWarming": true,
    "EnableNotifications": true,
    "EnableClustering": false,
    "WarmingIntervalMinutes": 5,
    "WarmingPatterns": ["user:*", "product:*", "config:*"],
    "MaxWarmingKeys": 2000,
    "NotificationChannel": "cache:notifications",
    "UseHashTags": true
  }
}
```

### 3. Register in Program.cs

```csharp
// Bind configuration
var cacheConfig = builder.Configuration
    .GetSection("CacheCapabilities")
    .Get<CacheCapabilitiesConfig>()
    ?? new CacheCapabilitiesConfig();

// Register with capabilities
services.AddHybridCacheWithCapabilities(
    builder.Configuration.GetConnectionString("Redis")!,
    cacheOptions =>
    {
        cacheOptions.KeyPrefix = cacheConfig.KeyPrefix;
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(cacheConfig.DefaultExpirationMinutes);
    },
    capabilities =>
    {
        capabilities.EnableCacheWarming = cacheConfig.EnableWarming;
        capabilities.EnableNotifications = cacheConfig.EnableNotifications;
        capabilities.EnableClustering = cacheConfig.EnableClustering;

        if (cacheConfig.EnableWarming)
        {
            capabilities.CacheWarmingOptions = options =>
            {
                options.EnableAutoWarming = true;
                options.WarmingInterval = TimeSpan.FromMinutes(cacheConfig.WarmingIntervalMinutes);
                options.IncludePatterns = cacheConfig.WarmingPatterns;
                options.MaxKeysPerWarming = cacheConfig.MaxWarmingKeys;
            };
        }

        if (cacheConfig.EnableNotifications)
        {
            capabilities.NotificationOptions = options =>
            {
                options.NotificationChannel = cacheConfig.NotificationChannel;
            };
        }

        if (cacheConfig.EnableClustering)
        {
            capabilities.ClusterOptions = options =>
            {
                options.IsClusterMode = true;
                options.UseHashTags = cacheConfig.UseHashTags;
            };
        }
    });
```

---

## Feature Comparison: Individual vs. Capabilities

### Individual Registration Methods

```csharp
// Step 1: Register hybrid cache
services.AddHybridCacheWithRedis("localhost:6379", options =>
{
    options.KeyPrefix = "myapp:";
});

// Step 2: Add notifications (separate call)
services.AddCacheNotifications(options =>
{
    options.NotificationChannel = "cache:sync";
});

// Step 3: Add warming (separate call)
services.AddCacheWarming(options =>
{
    options.WarmingInterval = TimeSpan.FromMinutes(5);
});

// Step 4: For clustering, use different initial method
// services.AddHybridCacheWithRedisCluster(...) instead
```

**Pros:**
- Flexible - can add features incrementally
- Granular control

**Cons:**
- Configuration scattered across multiple calls
- Easy to miss features
- Hard to see what's enabled at a glance
- Difficult to enable/disable conditionally

### Capabilities-Based Registration

```csharp
services.AddHybridCacheWithCapabilities(
    "localhost:6379",
    cacheOptions => { /* cache config */ },
    capabilities =>
    {
        capabilities.EnableCacheWarming = true;
        capabilities.EnableNotifications = true;
        capabilities.EnableClustering = true;

        capabilities.CacheWarmingOptions = options => { /* config */ };
        capabilities.NotificationOptions = options => { /* config */ };
        capabilities.ClusterOptions = options => { /* config */ };
    });
```

**Pros:**
- ✅ All configuration in one place
- ✅ Self-documenting (clear what's enabled)
- ✅ Easy to enable/disable features
- ✅ Perfect for configuration binding
- ✅ Works with any feature combination

**Cons:**
- Slightly more verbose for simple scenarios

---

## Best Practices

### 1. Use Capabilities for Production

```csharp
// ✅ GOOD - Explicit and clear
services.AddHybridCacheWithCapabilities(
    redisConnectionString,
    cacheOptions => { /* config */ },
    capabilities =>
    {
        capabilities.EnableCacheWarming = true;
        capabilities.EnableNotifications = true;
        // All features visible
    });

// ❌ AVOID - Scattered configuration
services.AddHybridCacheWithRedis(redisConnectionString);
services.AddCacheNotifications();
services.AddCacheWarming();
// What's enabled? What's configured?
```

### 2. Environment-Based Configuration

```csharp
// ✅ GOOD - Easy to enable/disable per environment
var isProduction = builder.Environment.IsProduction();

capabilities.EnableCacheWarming = isProduction;
capabilities.EnableNotifications = true;
capabilities.EnableClustering = isProduction;
```

### 3. Configuration from External Sources

```csharp
// ✅ GOOD - Driven by appsettings.json
var config = builder.Configuration.Get<CacheCapabilitiesConfig>();

capabilities.EnableCacheWarming = config.EnableWarming;
capabilities.EnableNotifications = config.EnableNotifications;
capabilities.EnableClustering = config.EnableClustering;
```

### 4. Feature Flags

```csharp
// ✅ GOOD - Works perfectly with feature flags
var featureFlags = builder.Services.BuildServiceProvider()
    .GetRequiredService<IFeatureManager>();

capabilities.EnableCacheWarming = await featureFlags.IsEnabledAsync("CacheWarming");
capabilities.EnableNotifications = await featureFlags.IsEnabledAsync("CacheNotifications");
```

---

## Migration Guide

### From Individual Methods to Capabilities

**Before:**
```csharp
services.AddHybridCacheWithRedis("localhost:6379", options =>
{
    options.KeyPrefix = "app:";
    options.DefaultExpiration = TimeSpan.FromMinutes(30);
});

services.AddCacheNotifications(options =>
{
    options.NotificationChannel = "cache:sync";
});

services.AddCacheWarming(options =>
{
    options.WarmingInterval = TimeSpan.FromMinutes(5);
    options.IncludePatterns = new[] { "user:*" };
});
```

**After:**
```csharp
services.AddHybridCacheWithCapabilities(
    "localhost:6379",
    cacheOptions =>
    {
        cacheOptions.KeyPrefix = "app:";
        cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(30);
    },
    capabilities =>
    {
        capabilities.EnableNotifications = true;
        capabilities.EnableCacheWarming = true;

        capabilities.NotificationOptions = options =>
        {
            options.NotificationChannel = "cache:sync";
        };

        capabilities.CacheWarmingOptions = options =>
        {
            options.WarmingInterval = TimeSpan.FromMinutes(5);
            options.IncludePatterns = new[] { "user:*" };
        };
    });
```

---

## Summary

The **capabilities-based registration** approach provides:

✅ **Centralized Configuration** - All settings in one place
✅ **Explicit Control** - Boolean flags make it clear what's enabled
✅ **Self-Documenting** - Easy to understand at a glance
✅ **Environment-Friendly** - Perfect for conditional configuration
✅ **Configuration Binding** - Works great with appsettings.json
✅ **Feature Flags** - Integrates easily with feature management

**Recommendation:** Use `AddHybridCacheWithCapabilities` for production applications where you need clear, maintainable configuration management.

---

## Related Documentation

- [Cache Warming Guide](../CacheWarming/README_CACHE_WARMING.md)
- [Cache Notifications Guide](../Notifications/README_NOTIFICATIONS.md)
- [Redis Clustering Guide](../Clustering/README_CLUSTER.md)
- [Main HybridCache README](../README.md)
