using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HybridCache.DependencyInjection;

namespace HybridCache.CacheWarming;

/// <summary>
/// Example demonstrating how to register and use cache warming services.
/// </summary>
public static class CacheWarmingUsageExample
{
    /// <summary>
    /// Example 1: Basic cache warming setup with Redis
    /// </summary>
    public static void Example1_BasicSetup(IServiceCollection services, string redisConnectionString)
    {
        // Register hybrid cache with Redis
        services.AddHybridCacheWithRedis(redisConnectionString, options =>
        {
            options.KeyPrefix = "myapp:";
            options.DefaultLocalExpiration = TimeSpan.FromMinutes(10);
        });

        // Add cache warming with default settings
        services.AddCacheWarming(options =>
        {
            options.EnableAutoWarming = true;
            options.WarmingInterval = TimeSpan.FromMinutes(5);
            options.IncludePatterns = new[] { "user:*", "product:*" };
        });
    }

    /// <summary>
    /// Example 2: Advanced cache warming for high-traffic e-commerce site
    /// </summary>
    public static void Example2_HighTrafficEcommerce(IServiceCollection services, string redisConnectionString)
    {
        services.AddHybridCacheWithRedis(redisConnectionString, options =>
        {
            options.KeyPrefix = "ecommerce:";
            options.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
        });

        services.AddCacheWarming(options =>
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
        });
    }

    /// <summary>
    /// Example 3: Multi-tenant SaaS application
    /// </summary>
    public static void Example3_MultiTenant(IServiceCollection services, string redisConnectionString)
    {
        services.AddHybridCacheWithRedis(redisConnectionString, options =>
        {
            options.KeyPrefix = "saas:";
        });

        services.AddCacheWarming(options =>
        {
            options.EnableAutoWarming = true;
            options.WarmingInterval = TimeSpan.FromMinutes(10);

            // Warm specific tenant data
            options.IncludePatterns = new[]
            {
                "tenant:premium:*",       // All premium tenants
                "shared:*"                // Shared reference data
            };

            options.MaxKeysPerWarming = 2000;
            options.BatchSize = 100;
        });
    }

    /// <summary>
    /// Example 4: Startup-only warming (no periodic refresh)
    /// </summary>
    public static void Example4_StartupOnly(IServiceCollection services, IHost app, string redisConnectionString)
    {
        services.AddHybridCacheWithRedis(redisConnectionString);

        services.AddCacheWarming(options =>
        {
            options.EnableAutoWarming = false; // No periodic warming
            options.IncludePatterns = new[] { "config:*", "reference:*" };
        });

        // Manually trigger warming after startup
        app.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStarted.Register(async () =>
            {
                var warmer = app.Services.GetRequiredService<CacheWarmerBackgroundService>();
                await warmer.TriggerWarmingAsync();
            });
    }

    /// <summary>
    /// Example 5: Environment-specific configuration
    /// </summary>
    public static void Example5_EnvironmentSpecific(
        IServiceCollection services,
        string redisConnectionString,
        IHostEnvironment environment)
    {
        services.AddHybridCacheWithRedis(redisConnectionString);

        var isProduction = environment.IsProduction();

        services.AddCacheWarming(options =>
        {
            options.EnableAutoWarming = isProduction; // Only in production

            options.WarmingInterval = isProduction
                ? TimeSpan.FromMinutes(5)     // Production: frequent
                : TimeSpan.FromMinutes(30);   // Development: rare

            options.IncludePatterns = new[] { "user:*", "product:*" };
            options.MaxKeysPerWarming = isProduction ? 5000 : 100;
            options.EnableDetailedLogging = !isProduction; // Debug in dev
        });
    }

    /// <summary>
    /// Example 6: Using cache warming with monitoring endpoint
    /// </summary>
    public static void Example6_WithMonitoring(IServiceCollection services, string redisConnectionString)
    {
        services.AddHybridCacheWithRedis(redisConnectionString);
        services.AddCacheWarming(options =>
        {
            options.EnableAutoWarming = true;
            options.WarmingInterval = TimeSpan.FromMinutes(5);
            options.IncludePatterns = new[] { "user:*", "product:*" };
        });

        // The CacheWarmerBackgroundService is automatically registered as a singleton
        // and can be injected into controllers for monitoring:
        //
        // [ApiController]
        // [Route("api/cache")]
        // public class CacheManagementController : ControllerBase
        // {
        //     private readonly CacheWarmerBackgroundService _warmerService;
        //
        //     public CacheManagementController(CacheWarmerBackgroundService warmerService)
        //     {
        //         _warmerService = warmerService;
        //     }
        //
        //     [HttpGet("warming/stats")]
        //     public IActionResult GetStats() => Ok(_warmerService.GetStatistics());
        //
        //     [HttpPost("warming/trigger")]
        //     public async Task<IActionResult> TriggerWarming()
        //     {
        //         var result = await _warmerService.TriggerWarmingAsync();
        //         return Ok(result);
        //     }
        // }
    }

    /// <summary>
    /// Example 7: Complete production setup with all features
    /// </summary>
    public static void Example7_ProductionComplete(IServiceCollection services, string redisConnectionString)
    {
        // 1. Register hybrid cache with Redis cluster support
        services.AddHybridCacheWithRedisCluster(
            redisConnectionString,
            cacheOptions =>
            {
                cacheOptions.KeyPrefix = "myapp:";
                cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(30);
                cacheOptions.DefaultLocalExpiration = TimeSpan.FromMinutes(5);
                cacheOptions.EnableDistributedCache = true;
            },
            clusterOptions =>
            {
                clusterOptions.IsClusterMode = true;
                clusterOptions.UseHashTags = true;
            });

        // 2. Add cache notifications for multi-instance invalidation
        services.AddCacheNotifications(notifyOptions =>
        {
            notifyOptions.EnableNotifications = true;
            notifyOptions.NotificationChannel = "cache:notifications";
            notifyOptions.NotifyOnOperations = new[]
            {
                Notifications.CacheOperation.Set,
                Notifications.CacheOperation.Remove,
                Notifications.CacheOperation.Clear
            };
        });

        // 3. Add cache warming for hot data pre-loading
        services.AddCacheWarming(warmingOptions =>
        {
            warmingOptions.EnableAutoWarming = true;
            warmingOptions.WarmingInterval = TimeSpan.FromMinutes(5);
            warmingOptions.InitialDelay = TimeSpan.FromSeconds(30);

            warmingOptions.IncludePatterns = new[]
            {
                "config:*",
                "user:active:*",
                "product:featured:*",
                "category:*"
            };

            warmingOptions.ExcludePatterns = new[]
            {
                "temp:*",
                "session:*",
                "lock:*"
            };

            warmingOptions.MaxKeysPerWarming = 3000;
            warmingOptions.BatchSize = 150;
            warmingOptions.FetchTimeout = TimeSpan.FromSeconds(5);
            warmingOptions.L1Expiration = TimeSpan.FromMinutes(5);
            warmingOptions.ContinueOnError = true;
            warmingOptions.EnableDetailedLogging = false;
        });

        // 4. Add health checks (optional - requires Microsoft.Extensions.Diagnostics.HealthChecks)
        // services.AddHealthChecks()
        //     .AddCheck<CacheWarmingHealthCheck>("cache_warming");
    }
}
