using Microsoft.Extensions.DependencyInjection;
using HybridCache.CacheWarming;
using HybridCache.Notifications;
using HybridCache.Clustering;

namespace HybridCache.DependencyInjection;

/// <summary>
/// Examples demonstrating the capabilities-based registration method.
/// This shows how to use AddHybridCacheWithCapabilities to selectively enable features.
/// </summary>
public static class CapabilitiesBasedRegistrationExamples
{
    /// <summary>
    /// Example 1: Enable all capabilities (full-featured setup)
    /// </summary>
    public static void Example1_AllCapabilitiesEnabled(IServiceCollection services, string redisConnectionString)
    {
        services.AddHybridCacheWithCapabilities(
            redisConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = "myapp:";
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(30);
                cacheOptions.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
            },
            capabilities =>
            {
                // Enable all capabilities
                capabilities.EnableCacheWarming = true;
                capabilities.EnableNotifications = true;
                capabilities.EnableClustering = true;

                // Configure cache warming
                capabilities.CacheWarmingOptions = options =>
                {
                    options.EnableAutoWarming = true;
                    options.WarmingInterval = TimeSpan.FromMinutes(5);
                    options.IncludePatterns = new[] { "user:*", "product:*", "config:*" };
                    options.ExcludePatterns = new[] { "temp:*", "session:*" };
                    options.MaxKeysPerWarming = 2000;
                    options.BatchSize = 100;
                };

                // Configure notifications
                capabilities.NotificationOptions = options =>
                {
                    options.EnableNotifications = true;
                    options.NotificationChannel = "cache:notifications";
                    options.AutoInvalidateL1OnNotification = true;
                    options.NotifyOnOperations = new[]
                    {
                        CacheOperation.Set,
                        CacheOperation.Remove,
                        CacheOperation.Clear
                    };
                };

                // Configure clustering
                capabilities.ClusterOptions = options =>
                {
                    options.IsClusterMode = true;
                    options.UseHashTags = true;
                    options.ValidateHashSlots = true;
                };
            });
    }

    /// <summary>
    /// Example 2: Only cache warming enabled (startup optimization)
    /// </summary>
    public static void Example2_OnlyCacheWarming(IServiceCollection services, string redisConnectionString)
    {
        services.AddHybridCacheWithCapabilities(
            redisConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = "startup:";
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(15);
            },
            capabilities =>
            {
                // Only enable cache warming
                capabilities.EnableCacheWarming = true;
                capabilities.EnableNotifications = false;
                capabilities.EnableClustering = false;

                capabilities.CacheWarmingOptions = options =>
                {
                    options.EnableAutoWarming = true;
                    options.WarmingInterval = TimeSpan.FromMinutes(10);
                    options.IncludePatterns = new[] { "config:*", "reference:*" };
                    options.MaxKeysPerWarming = 500;
                };
            });
    }

    /// <summary>
    /// Example 3: Only notifications enabled (multi-instance coordination)
    /// </summary>
    public static void Example3_OnlyNotifications(IServiceCollection services, string redisConnectionString)
    {
        services.AddHybridCacheWithCapabilities(
            redisConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = "distributed:";
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(20);
            },
            capabilities =>
            {
                // Only enable notifications
                capabilities.EnableCacheWarming = false;
                capabilities.EnableNotifications = true;
                capabilities.EnableClustering = false;

                capabilities.NotificationOptions = options =>
                {
                    options.EnableNotifications = true;
                    options.NotificationChannel = "app:cache:changes";
                    options.AutoInvalidateL1OnNotification = true;
                    options.IgnoreSelfNotifications = true;
                    options.IncludeKeyPatterns = new[] { "user:*", "session:*" };
                };
            });
    }

    /// <summary>
    /// Example 4: Only clustering enabled (Redis cluster deployment)
    /// </summary>
    public static void Example4_OnlyClustering(IServiceCollection services, string redisClusterConnectionString)
    {
        services.AddHybridCacheWithCapabilities(
            redisClusterConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = "cluster:";
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(30);
            },
            capabilities =>
            {
                // Only enable clustering
                capabilities.EnableCacheWarming = false;
                capabilities.EnableNotifications = false;
                capabilities.EnableClustering = true;

                capabilities.ClusterOptions = options =>
                {
                    options.IsClusterMode = true;
                    options.UseHashTags = true;
                    options.ValidateHashSlots = true;
                    options.RetryPolicy = new ClusterRetryPolicy
                    {
                        MaxRetries = 5,
                        InitialDelay = TimeSpan.FromMilliseconds(100),
                        MaxDelay = TimeSpan.FromSeconds(3),
                        UseExponentialBackoff = true
                    };
                };
            });
    }

    /// <summary>
    /// Example 5: Warming + Notifications (no clustering)
    /// </summary>
    public static void Example5_WarmingAndNotifications(IServiceCollection services, string redisConnectionString)
    {
        services.AddHybridCacheWithCapabilities(
            redisConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = "app:";
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(20);
                cacheOptions.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
            },
            capabilities =>
            {
                // Enable warming and notifications, but not clustering
                capabilities.EnableCacheWarming = true;
                capabilities.EnableNotifications = true;
                capabilities.EnableClustering = false;

                capabilities.CacheWarmingOptions = options =>
                {
                    options.EnableAutoWarming = true;
                    options.WarmingInterval = TimeSpan.FromMinutes(3);
                    options.IncludePatterns = new[] { "product:featured:*", "category:*" };
                    options.MaxKeysPerWarming = 1000;
                };

                capabilities.NotificationOptions = options =>
                {
                    options.EnableNotifications = true;
                    options.NotificationChannel = "cache:sync";
                    options.AutoInvalidateL1OnNotification = true;
                };
            });
    }

    /// <summary>
    /// Example 6: No capabilities enabled (basic hybrid cache only)
    /// </summary>
    public static void Example6_NoCapabilities(IServiceCollection services, string redisConnectionString)
    {
        services.AddHybridCacheWithCapabilities(
            redisConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = "simple:";
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(10);
            },
            capabilities =>
            {
                // All capabilities disabled - just basic hybrid cache
                capabilities.EnableCacheWarming = false;
                capabilities.EnableNotifications = false;
                capabilities.EnableClustering = false;
            });
    }

    /// <summary>
    /// Example 7: Environment-based capability configuration
    /// </summary>
    public static void Example7_EnvironmentBased(
        IServiceCollection services,
        string redisConnectionString,
        bool isProduction,
        bool isCluster)
    {
        services.AddHybridCacheWithCapabilities(
            redisConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = "env:";
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(20);
            },
            capabilities =>
            {
                // Enable warming only in production
                capabilities.EnableCacheWarming = isProduction;

                // Always enable notifications for multi-instance coordination
                capabilities.EnableNotifications = true;

                // Enable clustering based on infrastructure
                capabilities.EnableClustering = isCluster;

                if (isProduction)
                {
                    capabilities.CacheWarmingOptions = options =>
                    {
                        options.EnableAutoWarming = true;
                        options.WarmingInterval = TimeSpan.FromMinutes(5);
                        options.IncludePatterns = new[] { "user:*", "product:*" };
                        options.MaxKeysPerWarming = 5000;
                    };
                }

                capabilities.NotificationOptions = options =>
                {
                    options.EnableNotifications = true;
                    options.NotificationChannel = isProduction ? "prod:cache" : "dev:cache";
                };

                if (isCluster)
                {
                    capabilities.ClusterOptions = options =>
                    {
                        options.IsClusterMode = true;
                        options.UseHashTags = true;
                    };
                }
            });
    }

    /// <summary>
    /// Example 8: Configuration from appsettings.json
    /// </summary>
    public static void Example8_FromConfiguration(
        IServiceCollection services,
        string redisConnectionString,
        CacheCapabilitiesConfig config)
    {
        services.AddHybridCacheWithCapabilities(
            redisConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = config.KeyPrefix;
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(config.DefaultExpirationMinutes);
            },
            capabilities =>
            {
                capabilities.EnableCacheWarming = config.EnableWarming;
                capabilities.EnableNotifications = config.EnableNotifications;
                capabilities.EnableClustering = config.EnableClustering;

                if (config.EnableWarming)
                {
                    capabilities.CacheWarmingOptions = options =>
                    {
                        options.EnableAutoWarming = true;
                        options.WarmingInterval = TimeSpan.FromMinutes(config.WarmingIntervalMinutes);
                        options.IncludePatterns = config.WarmingPatterns;
                        options.MaxKeysPerWarming = config.MaxWarmingKeys;
                    };
                }

                if (config.EnableNotifications)
                {
                    capabilities.NotificationOptions = options =>
                    {
                        options.EnableNotifications = true;
                        options.NotificationChannel = config.NotificationChannel;
                    };
                }

                if (config.EnableClustering)
                {
                    capabilities.ClusterOptions = options =>
                    {
                        options.IsClusterMode = true;
                        options.UseHashTags = config.UseHashTags;
                    };
                }
            });
    }
}

/// <summary>
/// Configuration class that can be bound from appsettings.json
/// </summary>
public class CacheCapabilitiesConfig
{
    public string KeyPrefix { get; set; } = "app:";
    public int DefaultExpirationMinutes { get; set; } = 20;

    // Capability flags
    public bool EnableWarming { get; set; } = false;
    public bool EnableNotifications { get; set; } = false;
    public bool EnableClustering { get; set; } = false;

    // Warming config
    public int WarmingIntervalMinutes { get; set; } = 5;
    public string[] WarmingPatterns { get; set; } = Array.Empty<string>();
    public int MaxWarmingKeys { get; set; } = 1000;

    // Notification config
    public string NotificationChannel { get; set; } = "cache:notifications";

    // Clustering config
    public bool UseHashTags { get; set; } = true;
}

/// <summary>
/// Example appsettings.json structure:
///
/// {
///   "CacheCapabilities": {
///     "KeyPrefix": "myapp:",
///     "DefaultExpirationMinutes": 20,
///     "EnableWarming": true,
///     "EnableNotifications": true,
///     "EnableClustering": false,
///     "WarmingIntervalMinutes": 5,
///     "WarmingPatterns": ["user:*", "product:*"],
///     "MaxWarmingKeys": 1000,
///     "NotificationChannel": "cache:notifications",
///     "UseHashTags": true
///   }
/// }
///
/// Usage in Program.cs:
///
/// var cacheConfig = builder.Configuration
///     .GetSection("CacheCapabilities")
///     .Get<CacheCapabilitiesConfig>();
///
/// services.AddHybridCacheWithCapabilities(
///     builder.Configuration.GetConnectionString("Redis"),
///     cacheOptions => { },
///     capabilities => CapabilitiesBasedRegistrationExamples.Example8_FromConfiguration(
///         services,
///         redisConnectionString,
///         cacheConfig));
/// </summary>
public static class AppSettingsExample { }
