using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HybridCache.Serialization;
using HybridCache.LuaScripting;
using HybridCache.Clustering;
using HybridCache.Notifications;
using HybridCache.CacheWarming;
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
    /// Adds hybrid cache with Redis and Lua script support using ConfigurationOptions.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="redisConfigurationOptions">Redis ConfigurationOptions with detailed settings like endpoints, password, SSL, retry policies, etc.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// var redisConfig = new ConfigurationOptions
    /// {
    ///     EndPoints = { "localhost:6379", "localhost:6380" },
    ///     Password = "mypassword",
    ///     Ssl = true,
    ///     ConnectRetry = 3,
    ///     ConnectTimeout = 5000,
    ///     AbortOnConnectFail = false,
    ///     ReconnectRetryPolicy = new ExponentialRetry(5000)
    /// };
    /// services.AddHybridCacheWithRedis(redisConfig);
    /// </code>
    /// </example>
    public static IServiceCollection AddHybridCacheWithRedis(
        this IServiceCollection services,
        ConfigurationOptions redisConfigurationOptions,
        Action<HybridCacheOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (redisConfigurationOptions == null)
        {
            throw new ArgumentNullException(nameof(redisConfigurationOptions));
        }

        // Register Redis connection multiplexer
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(redisConfigurationOptions));

        // Register Redis distributed cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = redisConfigurationOptions;
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
    /// Adds hybrid cache with Redis and Lua script support using a configuration action.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureRedisOptions">An action to configure the Redis <see cref="ConfigurationOptions"/>.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddHybridCacheWithRedis(redisConfig =>
    /// {
    ///     redisConfig.EndPoints.Add("localhost:6379");
    ///     redisConfig.Password = "mypassword";
    ///     redisConfig.Ssl = true;
    ///     redisConfig.ConnectRetry = 3;
    ///     redisConfig.ConnectTimeout = 5000;
    ///     redisConfig.AbortOnConnectFail = false;
    ///     redisConfig.ReconnectRetryPolicy = new ExponentialRetry(5000);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddHybridCacheWithRedis(
        this IServiceCollection services,
        Action<ConfigurationOptions> configureRedisOptions,
        Action<HybridCacheOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureRedisOptions == null)
        {
            throw new ArgumentNullException(nameof(configureRedisOptions));
        }

        var redisConfigurationOptions = new ConfigurationOptions();
        configureRedisOptions(redisConfigurationOptions);

        return services.AddHybridCacheWithRedis(redisConfigurationOptions, configureOptions);
    }

    /// <summary>
    /// Adds hybrid cache with Redis cluster support and Lua scripts using ConfigurationOptions.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="redisConfigurationOptions">Redis ConfigurationOptions with detailed settings like endpoints, password, SSL, retry policies, etc.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <param name="configureClusterOptions">An optional action to configure the <see cref="RedisClusterOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// var redisConfig = new ConfigurationOptions
    /// {
    ///     EndPoints = { "node1:6379", "node2:6379", "node3:6379" },
    ///     Password = "mypassword",
    ///     Ssl = true,
    ///     ConnectRetry = 5,
    ///     ConnectTimeout = 10000,
    ///     AbortOnConnectFail = false,
    ///     ReconnectRetryPolicy = new ExponentialRetry(5000)
    /// };
    /// services.AddHybridCacheWithRedisCluster(redisConfig);
    /// </code>
    /// </example>
    public static IServiceCollection AddHybridCacheWithRedisCluster(
        this IServiceCollection services,
        ConfigurationOptions redisConfigurationOptions,
        Action<HybridCacheOptions>? configureOptions = null,
        Action<RedisClusterOptions>? configureClusterOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (redisConfigurationOptions == null)
        {
            throw new ArgumentNullException(nameof(redisConfigurationOptions));
        }

        // Configure cluster options
        var clusterOptions = new RedisClusterOptions { IsClusterMode = true };
        configureClusterOptions?.Invoke(clusterOptions);

        // Register Redis connection multiplexer
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(redisConfigurationOptions));

        // Register Redis distributed cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = redisConfigurationOptions;
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
    /// Adds hybrid cache with Redis cluster support and Lua scripts using a configuration action.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureRedisOptions">An action to configure the Redis <see cref="ConfigurationOptions"/>.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <param name="configureClusterOptions">An optional action to configure the <see cref="RedisClusterOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddHybridCacheWithRedisCluster(
    ///     redisConfig =>
    ///     {
    ///         redisConfig.EndPoints.Add("node1:6379");
    ///         redisConfig.EndPoints.Add("node2:6379");
    ///         redisConfig.EndPoints.Add("node3:6379");
    ///         redisConfig.Password = "mypassword";
    ///         redisConfig.Ssl = true;
    ///         redisConfig.ConnectRetry = 5;
    ///         redisConfig.AbortOnConnectFail = false;
    ///     });
    /// </code>
    /// </example>
    public static IServiceCollection AddHybridCacheWithRedisCluster(
        this IServiceCollection services,
        Action<ConfigurationOptions> configureRedisOptions,
        Action<HybridCacheOptions>? configureOptions = null,
        Action<RedisClusterOptions>? configureClusterOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureRedisOptions == null)
        {
            throw new ArgumentNullException(nameof(configureRedisOptions));
        }

        var redisConfigurationOptions = new ConfigurationOptions();
        configureRedisOptions(redisConfigurationOptions);

        return services.AddHybridCacheWithRedisCluster(redisConfigurationOptions, configureOptions, configureClusterOptions);
    }

    /// <summary>
    /// Adds hybrid cache with Redis and all optional capabilities using ConfigurationOptions.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="redisConfigurationOptions">Redis ConfigurationOptions with detailed settings.</param>
    /// <param name="configureCacheOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <param name="configureCapabilities">An action to configure which capabilities to enable.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHybridCacheWithCapabilities(
        this IServiceCollection services,
        ConfigurationOptions redisConfigurationOptions,
        Action<HybridCacheOptions>? configureCacheOptions = null,
        Action<HybridCacheCapabilities>? configureCapabilities = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (redisConfigurationOptions == null)
        {
            throw new ArgumentNullException(nameof(redisConfigurationOptions));
        }

        var capabilities = new HybridCacheCapabilities();
        configureCapabilities?.Invoke(capabilities);

        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(redisConfigurationOptions));

        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = redisConfigurationOptions;
        });

        if (capabilities.EnableClustering)
        {
            var clusterOptions = new RedisClusterOptions { IsClusterMode = true };
            capabilities.ClusterOptions?.Invoke(clusterOptions);

            services.TryAddSingleton(clusterOptions);

            services.TryAddSingleton<ILuaScriptExecutor>(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<HybridCacheOptions>>();
                var keyPrefix = options?.Value?.KeyPrefix;
                return new ClusterAwareLuaScriptExecutor(redis, keyPrefix, clusterOptions);
            });
        }
        else
        {
            services.TryAddSingleton<ILuaScriptExecutor>(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<HybridCacheOptions>>();
                var keyPrefix = options?.Value?.KeyPrefix;
                return new RedisLuaScriptExecutor(redis, keyPrefix);
            });
        }

        services.AddHybridCacheWithDistributed(configureCacheOptions);

        if (capabilities.EnableNotifications)
        {
            services.AddCacheNotifications(capabilities.NotificationOptions);
        }

        if (capabilities.EnableCacheWarming)
        {
            services.AddCacheWarming(capabilities.CacheWarmingOptions);
        }

        return services;
    }

    /// <summary>
    /// Adds hybrid cache with Redis and all optional capabilities using a configuration action.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureRedisOptions">An action to configure the Redis <see cref="ConfigurationOptions"/>.</param>
    /// <param name="configureCacheOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <param name="configureCapabilities">An action to configure which capabilities to enable.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddHybridCacheWithCapabilities(
        this IServiceCollection services,
        Action<ConfigurationOptions> configureRedisOptions,
        Action<HybridCacheOptions>? configureCacheOptions = null,
        Action<HybridCacheCapabilities>? configureCapabilities = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureRedisOptions == null)
        {
            throw new ArgumentNullException(nameof(configureRedisOptions));
        }

        var redisConfigurationOptions = new ConfigurationOptions();
        configureRedisOptions(redisConfigurationOptions);

        return services.AddHybridCacheWithCapabilities(redisConfigurationOptions, configureCacheOptions, configureCapabilities);
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

    /// <summary>
    /// Adds cache warming services that periodically pre-load data from L2 (distributed) cache
    /// into L1 (local memory) cache for improved performance.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="CacheWarmerOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// This method requires that both IMemoryCache and IConnectionMultiplexer have been registered.
    /// The cache warming background service will automatically start when the application starts.
    /// </para>
    /// <para>
    /// Cache warming is useful for:
    /// - Pre-loading frequently accessed data on application startup
    /// - Maintaining a warm cache after deployments or restarts
    /// - Reducing cold-start latency for critical data
    /// - Keeping hot data fresh in memory on a periodic basis
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddHybridCacheWithRedis("localhost:6379");
    /// services.AddCacheWarming(options =>
    /// {
    ///     options.EnableAutoWarming = true;
    ///     options.WarmingInterval = TimeSpan.FromMinutes(5);
    ///     options.IncludePatterns = new[] { "user:*", "product:*" };
    ///     options.ExcludePatterns = new[] { "temp:*" };
    ///     options.MaxKeysPerWarming = 1000;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddCacheWarming(
        this IServiceCollection services,
        Action<CacheWarmerOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Configure cache warmer options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<CacheWarmerOptions>(_ => { });
        }

        // Register cache warmer implementation
        services.TryAddSingleton<ICacheWarmer, RedisCacheWarmer>();

        // Register background service (automatically starts with the application)
        services.AddHostedService<CacheWarmerBackgroundService>();

        // Also register as singleton for manual access (statistics, manual triggers, etc.)
        services.TryAddSingleton<CacheWarmerBackgroundService>();

        return services;
    }

    /// <summary>
    /// Adds hybrid cache with Redis and all optional capabilities that can be enabled/disabled.
    /// This is the all-in-one registration method that provides fine-grained control over features.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="redisConfiguration">Redis connection string or configuration.</param>
    /// <param name="configureCacheOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <param name="configureCapabilities">An action to configure which capabilities to enable.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <example>
    /// <code>
    /// services.AddHybridCacheWithCapabilities(
    ///     "localhost:6379",
    ///     cacheOptions =>
    ///     {
    ///         cacheOptions.KeyPrefix = "myapp:";
    ///         cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(30);
    ///     },
    ///     capabilities =>
    ///     {
    ///         capabilities.EnableCacheWarming = true;
    ///         capabilities.EnableNotifications = true;
    ///         capabilities.EnableClustering = false;
    ///         capabilities.CacheWarmingOptions = options =>
    ///         {
    ///             options.WarmingInterval = TimeSpan.FromMinutes(5);
    ///             options.IncludePatterns = new[] { "user:*", "product:*" };
    ///         };
    ///         capabilities.NotificationOptions = options =>
    ///         {
    ///             options.NotificationChannel = "cache:notifications";
    ///         };
    ///     });
    /// </code>
    /// </example>
    public static IServiceCollection AddHybridCacheWithCapabilities(
        this IServiceCollection services,
        string redisConfiguration,
        Action<HybridCacheOptions>? configureCacheOptions = null,
        Action<HybridCacheCapabilities>? configureCapabilities = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrEmpty(redisConfiguration))
        {
            throw new ArgumentException("Redis configuration cannot be null or empty.", nameof(redisConfiguration));
        }

        // Build capabilities configuration
        var capabilities = new HybridCacheCapabilities();
        configureCapabilities?.Invoke(capabilities);

        // Register Redis connection multiplexer
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(redisConfiguration));

        // Register Redis distributed cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConfiguration;
        });

        // Register Lua script executor based on clustering capability
        if (capabilities.EnableClustering)
        {
            // Configure cluster options
            var clusterOptions = new RedisClusterOptions { IsClusterMode = true };
            capabilities.ClusterOptions?.Invoke(clusterOptions);

            services.TryAddSingleton(clusterOptions);

            // Register cluster-aware Lua script executor
            services.TryAddSingleton<ILuaScriptExecutor>(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<HybridCacheOptions>>();
                var keyPrefix = options?.Value?.KeyPrefix;
                return new ClusterAwareLuaScriptExecutor(redis, keyPrefix, clusterOptions);
            });
        }
        else
        {
            // Register standard Lua script executor
            services.TryAddSingleton<ILuaScriptExecutor>(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<HybridCacheOptions>>();
                var keyPrefix = options?.Value?.KeyPrefix;
                return new RedisLuaScriptExecutor(redis, keyPrefix);
            });
        }

        // Add hybrid cache with distributed support
        services.AddHybridCacheWithDistributed(configureCacheOptions);

        // Add cache notifications if enabled
        if (capabilities.EnableNotifications)
        {
            services.AddCacheNotifications(capabilities.NotificationOptions);
        }

        // Add cache warming if enabled
        if (capabilities.EnableCacheWarming)
        {
            services.AddCacheWarming(capabilities.CacheWarmingOptions);
        }

        return services;
    }

    /// <summary>
    /// Adds hybrid cache with existing Redis connection and Lua script support.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="connectionMultiplexer">The existing <see cref="IConnectionMultiplexer"/> instance.</param>
    /// <param name="configureOptions">An optional action to configure the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// This method is useful when you already have a Redis connection configured
    /// and want to use it for hybrid caching without re-creating the connection.
    /// </remarks>
    public static IServiceCollection AddHybridCacheWithRedis(
        this IServiceCollection services,
        IConnectionMultiplexer connectionMultiplexer,
        Action<HybridCacheOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (connectionMultiplexer == null)
        {
            throw new ArgumentNullException(nameof(connectionMultiplexer));
        }

        // Register existing Redis connection multiplexer
        services.AddSingleton<IConnectionMultiplexer>(connectionMultiplexer);

        // Register Redis distributed cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionMultiplexer.Configuration;
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
    [Obsolete("Use AddHybridCacheWithRedis(IConnectionMultiplexer, Action<HybridCacheOptions>?) instead.")]
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
}

/// <summary>
/// Configuration class for enabling/disabling hybrid cache capabilities.
/// </summary>
public class HybridCacheCapabilities
{
    /// <summary>
    /// Gets or sets whether to enable cache warming.
    /// When enabled, the background service will periodically pre-load data from L2 to L1 cache.
    /// Default is false.
    /// </summary>
    public bool EnableCacheWarming { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable cache change notifications.
    /// When enabled, cache changes will be published via Redis pub/sub for L1 invalidation across instances.
    /// Default is false.
    /// </summary>
    public bool EnableNotifications { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable Redis cluster support.
    /// When enabled, uses cluster-aware Lua script executor with hash slot validation.
    /// Default is false.
    /// </summary>
    public bool EnableClustering { get; set; } = false;

    /// <summary>
    /// Gets or sets the configuration action for cache warming options.
    /// Only applies if EnableCacheWarming is true.
    /// </summary>
    public Action<CacheWarmerOptions>? CacheWarmingOptions { get; set; }

    /// <summary>
    /// Gets or sets the configuration action for notification options.
    /// Only applies if EnableNotifications is true.
    /// </summary>
    public Action<CacheNotificationOptions>? NotificationOptions { get; set; }

    /// <summary>
    /// Gets or sets the configuration action for cluster options.
    /// Only applies if EnableClustering is true.
    /// </summary>
    public Action<RedisClusterOptions>? ClusterOptions { get; set; }
}


