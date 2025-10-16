using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HybridCache.Serialization;
using HybridCache.LuaScripting;
using HybridCache.Clustering;
using HybridCache.Notifications;
using StackExchange.Redis;

namespace HybridCache.DependencyInjection;

/// <summary>
/// Extension methods for setting up hybrid cache services in an <see cref="IServiceCollection" />.
/// </summary>
public static class HybridCacheServiceCollectionExtensions
{
    /// <summary>
    /// Adds hybrid cache services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHybridCache(
        this IServiceCollection services,
        Action<HybridCacheOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Add memory cache if not already registered
        services.AddMemoryCache();

        // Register the cache serializer
        services.TryAddSingleton<ICacheSerializer, JsonCacheSerializer>();

        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<HybridCacheOptions>(_ => { });
        }

        // Register the hybrid cache
        services.TryAddSingleton<IHybridCache, DefaultHybridCache>();

        return services;
    }

    /// <summary>
    /// Adds hybrid cache services with distributed cache support.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// This method requires that a distributed cache implementation (e.g., Redis, SQL Server)
    /// has been registered before calling this method using AddStackExchangeRedisCache,
    /// AddDistributedSqlServerCache, etc.
    /// </remarks>
    public static IServiceCollection AddHybridCacheWithDistributed(
        this IServiceCollection services,
        Action<HybridCacheOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Add base hybrid cache services
        services.AddHybridCache(options =>
        {
            options.EnableDistributedCache = true;
            configureOptions?.Invoke(options);
        });

        return services;
    }

    /// <summary>
    /// Adds hybrid cache services with a custom serializer.
    /// </summary>
    /// <typeparam name="TSerializer">The type of the cache serializer.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHybridCache<TSerializer>(
        this IServiceCollection services,
        Action<HybridCacheOptions>? configureOptions = null)
        where TSerializer : class, ICacheSerializer
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddMemoryCache();

        // Register custom serializer
        services.TryAddSingleton<ICacheSerializer, TSerializer>();

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<HybridCacheOptions>(_ => { });
        }

        services.TryAddSingleton<IHybridCache, DefaultHybridCache>();

        return services;
    }

    /// <summary>
    /// Adds hybrid cache with Redis and Lua script support.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="redisConfiguration">Redis connection string or configuration.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHybridCacheWithRedis(
        this IServiceCollection services,
        string redisConfiguration,
        Action<HybridCacheOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrEmpty(redisConfiguration))
        {
            throw new ArgumentException("Redis configuration cannot be null or empty.", nameof(redisConfiguration));
        }

        // Register Redis connection multiplexer
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(redisConfiguration));

        // Register Redis distributed cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConfiguration;
        });

        // Register Lua script executor
        services.TryAddSingleton<ILuaScriptExecutor>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var options = sp.GetService<Microsoft.Extensions.Options.IOptions<HybridCacheOptions>>();
            var keyPrefix = options?.Value?.KeyPrefix;
            return new RedisLuaScriptExecutor(redis, keyPrefix);
        });

        // Add hybrid cache with distributed support
        services.AddHybridCacheWithDistributed(configureOptions);

        return services;
    }

    /// <summary>
    /// Adds hybrid cache with existing Redis connection and Lua script support.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// This method requires that an IConnectionMultiplexer has been registered before calling this method.
    /// </remarks>
    public static IServiceCollection AddHybridCacheWithRedisLuaSupport(
        this IServiceCollection services,
        Action<HybridCacheOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Register Lua script executor (requires IConnectionMultiplexer to be already registered)
        services.TryAddSingleton<ILuaScriptExecutor>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var options = sp.GetService<Microsoft.Extensions.Options.IOptions<HybridCacheOptions>>();
            var keyPrefix = options?.Value?.KeyPrefix;
            return new RedisLuaScriptExecutor(redis, keyPrefix);
        });

        // Add hybrid cache with distributed support
        services.AddHybridCacheWithDistributed(configureOptions);

        return services;
    }

    /// <summary>
    /// Adds hybrid cache with Redis cluster support and Lua scripts.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="redisConfiguration">Redis cluster connection string.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <param name="configureClusterOptions">An optional action to configure the <see cref="RedisClusterOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHybridCacheWithRedisCluster(
        this IServiceCollection services,
        string redisConfiguration,
        Action<HybridCacheOptions>? configureOptions = null,
        Action<RedisClusterOptions>? configureClusterOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrEmpty(redisConfiguration))
        {
            throw new ArgumentException("Redis configuration cannot be null or empty.", nameof(redisConfiguration));
        }

        // Configure cluster options
        var clusterOptions = new RedisClusterOptions { IsClusterMode = true };
        configureClusterOptions?.Invoke(clusterOptions);

        // Register Redis connection multiplexer
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(redisConfiguration));

        // Register Redis distributed cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConfiguration;
        });

        // Register cluster options
        services.TryAddSingleton(clusterOptions);

        // Register cluster-aware Lua script executor
        services.TryAddSingleton<ILuaScriptExecutor>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var options = sp.GetService<Microsoft.Extensions.Options.IOptions<HybridCacheOptions>>();
            var keyPrefix = options?.Value?.KeyPrefix;
            return new ClusterAwareLuaScriptExecutor(redis, keyPrefix, clusterOptions);
        });

        // Add hybrid cache with distributed support
        services.AddHybridCacheWithDistributed(configureOptions);

        return services;
    }

    /// <summary>
    /// Adds cache change notifications using Redis pub/sub.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="CacheNotificationOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// This method requires that an IConnectionMultiplexer has been registered.
    /// Notifications enable automatic L1 cache invalidation across multiple instances.
    /// </remarks>
    public static IServiceCollection AddCacheNotifications(
        this IServiceCollection services,
        Action<CacheNotificationOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Configure notification options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<CacheNotificationOptions>(options =>
            {
                options.EnableNotifications = true;
            });
        }

        // Register notification service as both publisher and subscriber
        services.TryAddSingleton<RedisCacheNotificationService>();
        services.TryAddSingleton<ICacheNotificationPublisher>(sp =>
            sp.GetRequiredService<RedisCacheNotificationService>());
        services.TryAddSingleton<ICacheNotificationSubscriber>(sp =>
            sp.GetRequiredService<RedisCacheNotificationService>());

        return services;
    }
}


