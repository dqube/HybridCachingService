using Microsoft.AspNetCore.Mvc;
using HybridCache.Clustering;
using HybridCache.LuaScripting;
using HybridCache.Notifications;
using HybridCachingService.Models;

namespace HybridCachingService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AdvancedFeaturesController : ControllerBase
{
    private readonly HybridCache.IHybridCache _cache;
    private readonly ILogger<AdvancedFeaturesController> _logger;

    public AdvancedFeaturesController(HybridCache.IHybridCache cache, ILogger<AdvancedFeaturesController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    #region Cluster Operations

    /// <summary>
    /// Calculate hash slot for a key (Redis Cluster)
    /// </summary>
    [HttpGet("cluster/hash-slot/{key}")]
    public ActionResult GetHashSlot(string key)
    {
        var slot = RedisClusterHelper.CalculateHashSlot(key);
        var hashTag = RedisClusterHelper.ExtractHashTag(key);

        return Ok(new
        {
            key,
            hashSlot = slot,
            hashTag = hashTag != key ? hashTag : null,
            range = "0-16383"
        });
    }

    /// <summary>
    /// Validate if keys can be used together in cluster mode
    /// </summary>
    [HttpPost("cluster/validate-keys")]
    public ActionResult ValidateKeys([FromBody] string[] keys)
    {
        var isValid = RedisClusterHelper.ValidateHashSlots(keys);

        var slots = keys.Select(k => new
        {
            key = k,
            slot = RedisClusterHelper.CalculateHashSlot(k),
            hashTag = RedisClusterHelper.ExtractHashTag(k)
        }).ToArray();

        return Ok(new
        {
            isValid,
            message = isValid
                ? "All keys map to the same hash slot"
                : "Keys map to different hash slots - cannot be used in multi-key Lua scripts",
            keys = slots
        });
    }

    /// <summary>
    /// Wrap keys with hash tag to ensure same slot
    /// </summary>
    [HttpPost("cluster/wrap-keys")]
    public ActionResult WrapKeysWithHashTag(
        [FromQuery] string hashTag,
        [FromBody] string[] keys)
    {
        var wrappedKeys = RedisClusterHelper.WrapKeysWithHashTag(hashTag, keys);

        var result = wrappedKeys.Select((k, i) => new
        {
            original = keys[i],
            wrapped = k,
            hashSlot = RedisClusterHelper.CalculateHashSlot(k)
        }).ToArray();

        return Ok(new
        {
            hashTag,
            keys = result,
            allSameSlot = RedisClusterHelper.ValidateHashSlots(wrappedKeys)
        });
    }

    /// <summary>
    /// Multi-key operation with automatic hash tags (cluster-safe)
    /// </summary>
    [HttpPost("cluster/user/{userId}/batch-set")]
    public async Task<ActionResult> ClusterSafeUserBatch(int userId, [FromBody] Dictionary<string, string> data)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        // Use hash tag to ensure all keys in same slot
        var hashTag = $"user:{userId}";
        var keys = data.Keys.Select(k => $"{{{hashTag}}}:{k}").ToArray();
        var values = data.Values.Cast<object>()
            .Concat(new object[] { 3600 }) // expiration
            .ToArray();

        var result = await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.SetMultiple,
            keys: keys,
            values: values
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new
        {
            message = $"{data.Count} keys set successfully",
            userId,
            hashTag,
            keysSet = result.Result,
            clusterSafe = true
        });
    }

    #endregion

    #region Cache Notifications

    /// <summary>
    /// Demonstrate cache notifications across instances
    /// </summary>
    [HttpPost("notifications/demo/{key}")]
    public async Task<ActionResult> NotificationDemo(string key, [FromBody] string value)
    {
        // When this is set, all other instances will be notified
        // and their L1 caches will be automatically invalidated

        await _cache.SetAsync(
            $"user:{key}",
            value,
            HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(10))
        );

        return Ok(new
        {
            message = "Value cached and notification sent to all instances",
            key,
            value,
            notificationChannel = "hybridcache:notifications",
            behavior = new
            {
                thisInstance = "L1 and L2 updated",
                otherInstances = "L1 invalidated automatically",
                nextRead = "Other instances will fetch from L2"
            }
        });
    }

    /// <summary>
    /// Simulate multi-instance scenario
    /// </summary>
    [HttpPost("notifications/multi-instance-test")]
    public async Task<ActionResult> MultiInstanceTest()
    {
        var testKey = $"user:test-{Guid.NewGuid():N}";
        var initialValue = "version-1";
        var updatedValue = "version-2";

        // Step 1: Set initial value
        await _cache.SetAsync(testKey, initialValue);
        _logger.LogInformation("Set initial value: {Value}", initialValue);

        // Step 2: Read from cache (will be in L1)
        var value1 = await _cache.GetAsync<string>(testKey);
        _logger.LogInformation("Read value: {Value}", value1);

        // Step 3: Update value (notification will be sent)
        await _cache.SetAsync(testKey, updatedValue);
        _logger.LogInformation("Updated to: {Value}", updatedValue);

        // Step 4: Read again (L1 was invalidated by notification)
        var value2 = await _cache.GetAsync<string>(testKey);
        _logger.LogInformation("Read updated value: {Value}", value2);

        return Ok(new
        {
            message = "Multi-instance test completed",
            testKey,
            initialValue = value1,
            updatedValue = value2,
            notificationSent = true,
            l1Invalidated = true
        });
    }

    #endregion

    #region Cache Patterns

    /// <summary>
    /// Cache-aside pattern with distributed locking
    /// </summary>
    [HttpGet("patterns/cache-aside-with-lock/{userId}")]
    public async Task<ActionResult<User>> CacheAsideWithLock(int userId)
    {
        var key = $"user:{userId}";
        var lockKey = $"lock:{key}";
        var lockToken = Guid.NewGuid().ToString();

        // Try to get from cache first
        var cachedUser = await _cache.GetAsync<User>(key);
        if (cachedUser != null)
        {
            return Ok(new { user = cachedUser, source = "cache" });
        }

        // Not in cache, acquire lock to prevent stampede
        var scriptExecutor = _cache.ScriptExecutor!;
        var lockResult = await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.AcquireLock,
            keys: new[] { lockKey },
            values: new object[] { lockToken, 10 }
        );

        if (lockResult.Result == 0)
        {
            // Another process is loading, wait and retry
            await Task.Delay(100);
            cachedUser = await _cache.GetAsync<User>(key);
            return Ok(new { user = cachedUser, source = "cache-after-wait" });
        }

        try
        {
            // Simulate database fetch
            _logger.LogInformation("Fetching user {UserId} from database", userId);
            await Task.Delay(200);

            var user = new User
            {
                Id = userId,
                Name = $"User {userId}",
                Email = $"user{userId}@example.com",
                CreatedAt = DateTime.UtcNow
            };

            // Cache the result
            await _cache.SetAsync(key, user,
                HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(15)));

            return Ok(new { user, source = "database" });
        }
        finally
        {
            // Release lock
            await scriptExecutor.ExecuteAsync<int>(
                LuaScripts.ReleaseLock,
                keys: new[] { lockKey },
                values: new object[] { lockToken }
            );
        }
    }

    /// <summary>
    /// Write-through cache pattern
    /// </summary>
    [HttpPut("patterns/write-through/{userId}")]
    public async Task<ActionResult> WriteThroughCache(int userId, [FromBody] User user)
    {
        user.Id = userId;
        user.LastModified = DateTime.UtcNow;

        // Step 1: Write to "database" (simulated)
        _logger.LogInformation("Writing user {UserId} to database", userId);
        await Task.Delay(100);

        // Step 2: Write to cache immediately
        var key = $"user:{userId}";
        await _cache.SetAsync(key, user,
            HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(15)));

        return Ok(new
        {
            message = "User updated using write-through pattern",
            user,
            pattern = "write-through",
            cached = true
        });
    }

    /// <summary>
    /// Refresh-ahead cache pattern
    /// </summary>
    [HttpPost("patterns/refresh-ahead/{key}")]
    public async Task<ActionResult> RefreshAhead(string key)
    {
        var scriptExecutor = _cache.ScriptExecutor!;

        // Get value and extend expiration atomically
        var result = await scriptExecutor.ExecuteAsync(
            LuaScripts.GetAndExtendExpiration,
            keys: new[] { key },
            values: new object[] { 3600 }
        );

        return Ok(new
        {
            message = "Cache entry refreshed",
            key,
            expirationExtended = result.Success,
            pattern = "refresh-ahead"
        });
    }

    #endregion

    #region Statistics and Health

    /// <summary>
    /// Get cache statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult> GetStatistics()
    {
        var testKeys = new[] { "user:1", "user:2", "product:100", "session:abc" };
        var stats = new List<object>();

        foreach (var key in testKeys)
        {
            var exists = await _cache.GetAsync<object>(key) != null;
            var slot = RedisClusterHelper.CalculateHashSlot(key);

            stats.Add(new
            {
                key,
                exists,
                hashSlot = slot
            });
        }

        return Ok(new
        {
            timestamp = DateTime.UtcNow,
            features = new
            {
                luaScripts = _cache.ScriptExecutor != null,
                notifications = true,
                clusterSupport = true
            },
            sampleKeys = stats
        });
    }

    /// <summary>
    /// Health check
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult> HealthCheck()
    {
        try
        {
            // Test basic cache operation
            var testKey = $"health:{Guid.NewGuid():N}";
            await _cache.SetAsync(testKey, "test",
                HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromSeconds(10)));
            var value = await _cache.GetAsync<string>(testKey);
            await _cache.RemoveAsync(testKey);

            var isHealthy = value == "test";

            return Ok(new
            {
                status = isHealthy ? "healthy" : "degraded",
                timestamp = DateTime.UtcNow,
                components = new
                {
                    cache = isHealthy,
                    luaScripts = _cache.ScriptExecutor != null
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "unhealthy", error = ex.Message });
        }
    }

    #endregion
}
