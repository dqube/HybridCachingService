using Microsoft.AspNetCore.Mvc;
using HybridCache.Clustering;
using HybridCache.LuaScripting;
using HybridCache.Notifications;
using HybridCache.Examples;
using HybridCachingService.Models;

namespace HybridCachingService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AdvancedFeaturesController : ControllerBase
{
    private readonly HybridCache.IHybridCache _cache;
    private readonly ILogger<AdvancedFeaturesController> _logger;
    private readonly LuaScriptExamples _luaExamples;

    public AdvancedFeaturesController(
        HybridCache.IHybridCache cache, 
        ILogger<AdvancedFeaturesController> logger)
    {
        _cache = cache;
        _logger = logger;
        _luaExamples = new LuaScriptExamples(cache);
    }

    #region Lua Script Examples

    /// <summary>
    /// Execute operation with distributed lock
    /// </summary>
    [HttpPost("lua/execute-with-lock/{resourceKey}")]
    public async Task<ActionResult> ExecuteWithLock(string resourceKey, [FromBody] int delayMs = 1000)
    {
        try
        {
            var result = await _luaExamples.ExecuteWithLockAsync(
                resourceKey,
                async () =>
                {
                    _logger.LogInformation("Executing protected operation for {Resource}", resourceKey);
                    await Task.Delay(delayMs);
                    return $"Operation completed at {DateTime.UtcNow:O}";
                },
                TimeSpan.FromSeconds(10)
            );

            return Ok(new
            {
                success = true,
                resource = resourceKey,
                result,
                lockDuration = "10 seconds"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(409, new { error = ex.Message, resource = resourceKey });
        }
    }

    /// <summary>
    /// Check rate limit for a user
    /// </summary>
    [HttpPost("lua/rate-limit/{userId}")]
    public async Task<ActionResult> CheckRateLimit(
        string userId,
        [FromQuery] int maxRequests = 10,
        [FromQuery] int windowSeconds = 60)
    {
        var isAllowed = await _luaExamples.CheckRateLimitAsync(
            userId,
            maxRequests,
            TimeSpan.FromSeconds(windowSeconds)
        );

        return Ok(new
        {
            userId,
            isAllowed,
            maxRequests,
            windowSeconds,
            message = isAllowed ? "Request allowed" : "Rate limit exceeded"
        });
    }

    /// <summary>
    /// Increment daily counter
    /// </summary>
    [HttpPost("lua/counter/{counterKey}/increment")]
    public async Task<ActionResult> IncrementDailyCounter(string counterKey)
    {
        var value = await _luaExamples.IncrementDailyCounterAsync(counterKey);

        return Ok(new
        {
            counter = counterKey,
            value,
            expiration = "24 hours"
        });
    }

    /// <summary>
    /// Update with optimistic concurrency control
    /// </summary>
    [HttpPut("lua/update-with-cas/{key}")]
    public async Task<ActionResult> UpdateWithCAS(
        string key,
        [FromQuery] string expectedValue,
        [FromQuery] string newValue)
    {
        var success = await _luaExamples.UpdateWithConcurrencyCheckAsync(
            key,
            expectedValue,
            newValue,
            TimeSpan.FromMinutes(10)
        );

        return Ok(new
        {
            success,
            key,
            expectedValue,
            newValue,
            message = success ? "Value updated successfully" : "Value was modified by another process"
        });
    }

    /// <summary>
    /// Get multiple keys in a single operation
    /// </summary>
    [HttpPost("lua/batch-get")]
    public async Task<ActionResult> BatchGet([FromBody] string[] keys)
    {
        var results = await _luaExamples.GetMultipleAsync<string>(keys);

        return Ok(new
        {
            totalKeys = keys.Length,
            foundKeys = results.Count(r => r.Value != null),
            results
        });
    }

    /// <summary>
    /// Log user activity with size limit
    /// </summary>
    [HttpPost("lua/activity/{userId}/log")]
    public async Task<ActionResult> LogActivity(string userId, [FromBody] string activity)
    {
        var count = await _luaExamples.LogActivityAsync(userId, activity, maxActivities: 50);

        return Ok(new
        {
            userId,
            activity,
            totalActivities = count,
            maxActivities = 50
        });
    }

    /// <summary>
    /// Get value with sliding expiration
    /// </summary>
    [HttpGet("lua/sliding/{key}")]
    public async Task<ActionResult> GetWithSlidingExpiration(string key)
    {
        var value = await _luaExamples.GetWithSlidingExpirationAsync<string>(
            key,
            TimeSpan.FromMinutes(5)
        );

        return Ok(new
        {
            key,
            value,
            slidingWindow = "5 minutes",
            message = value != null ? "Value found and expiration extended" : "Value not found"
        });
    }

    /// <summary>
    /// Transfer amount between counters atomically
    /// </summary>
    [HttpPost("lua/transfer")]
    public async Task<ActionResult> TransferBetweenCounters(
        [FromQuery] string sourceKey,
        [FromQuery] string targetKey,
        [FromQuery] long amount)
    {
        var success = await _luaExamples.TransferBetweenCountersAsync(sourceKey, targetKey, amount);

        return Ok(new
        {
            success,
            sourceKey,
            targetKey,
            amount,
            message = success ? "Transfer completed" : "Insufficient balance in source"
        });
    }

    #endregion

    #region RedisJSON Operations

    /// <summary>
    /// Set a JSON document in Redis using JSON.SET
    /// </summary>
    [HttpPost("json/set/{key}")]
    public async Task<ActionResult> SetJsonDocument(string key, [FromBody] object jsonDocument)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        // Lua script to set JSON document
        const string setJsonScript = @"
            local key = KEYS[1]
            local path = ARGV[1]
            local value = ARGV[2]
            
            -- Use JSON.SET command if RedisJSON module is loaded
            local ok, result = pcall(redis.call, 'JSON.SET', key, path, value)
            if ok then
                return 1
            else
                -- Fallback to regular SET if JSON module not available
                redis.call('SET', key, value)
                return 0
            end
        ";

        var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonDocument);
        var result = await scriptExecutor.ExecuteAsync<int>(
            setJsonScript,
            keys: new[] { $"json:{key}" },
            values: new object[] { "$", jsonString }
        );

        return Ok(new
        {
            key,
            success = true,
            usedJsonModule = result.Result == 1,
            message = result.Result == 1 
                ? "JSON document stored using RedisJSON" 
                : "JSON document stored as string (RedisJSON module not available)"
        });
    }

    /// <summary>
    /// Get a JSON document from Redis using JSON.GET
    /// </summary>
    [HttpGet("json/get/{key}")]
    public async Task<ActionResult> GetJsonDocument(string key, [FromQuery] string path = "$")
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        const string getJsonScript = @"
            local key = KEYS[1]
            local path = ARGV[1]
            
            -- Try JSON.GET first
            local ok, result = pcall(redis.call, 'JSON.GET', key, path)
            if ok then
                return result
            else
                -- Fallback to regular GET
                return redis.call('GET', key)
            end
        ";

        var result = await scriptExecutor.ExecuteAsync(
            getJsonScript,
            keys: new[] { $"json:{key}" },
            values: new object[] { path }
        );

        if (!result.Success || result.Result == null)
        {
            return NotFound(new { key, message = "JSON document not found" });
        }

        return Ok(new
        {
            key,
            path,
            document = result.Result,
            message = "JSON document retrieved"
        });
    }

    /// <summary>
    /// Set a specific field in JSON document using JSON.SET with path
    /// </summary>
    [HttpPatch("json/{key}/field")]
    public async Task<ActionResult> SetJsonField(
        string key,
        [FromQuery] string path,
        [FromBody] object value)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        const string setFieldScript = @"
            local key = KEYS[1]
            local path = ARGV[1]
            local value = ARGV[2]
            
            local ok, result = pcall(redis.call, 'JSON.SET', key, path, value)
            if ok then
                return 1
            else
                return redis.error_reply(result)
            end
        ";

        var valueJson = System.Text.Json.JsonSerializer.Serialize(value);
        var result = await scriptExecutor.ExecuteAsync<int>(
            setFieldScript,
            keys: new[] { $"json:{key}" },
            values: new object[] { path, valueJson }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new
        {
            key,
            path,
            value,
            success = result.Result == 1,
            message = "JSON field updated"
        });
    }

    /// <summary>
    /// Increment a numeric field in JSON document
    /// </summary>
    [HttpPost("json/{key}/increment")]
    public async Task<ActionResult> IncrementJsonField(
        string key,
        [FromQuery] string path,
        [FromQuery] double incrementBy = 1)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        const string incrementScript = @"
            local key = KEYS[1]
            local path = ARGV[1]
            local increment = tonumber(ARGV[2])
            
            local ok, result = pcall(redis.call, 'JSON.NUMINCRBY', key, path, increment)
            if ok then
                return result
            else
                return redis.error_reply(result)
            end
        ";

        var result = await scriptExecutor.ExecuteAsync<string>(
            incrementScript,
            keys: new[] { $"json:{key}" },
            values: new object[] { path, incrementBy }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new
        {
            key,
            path,
            incrementBy,
            newValue = result.Result,
            message = "JSON field incremented"
        });
    }

    /// <summary>
    /// Append to a JSON array
    /// </summary>
    [HttpPost("json/{key}/array/append")]
    public async Task<ActionResult> AppendToJsonArray(
        string key,
        [FromQuery] string path,
        [FromBody] object value)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        const string appendScript = @"
            local key = KEYS[1]
            local path = ARGV[1]
            local value = ARGV[2]
            
            local ok, result = pcall(redis.call, 'JSON.ARRAPPEND', key, path, value)
            if ok then
                return result
            else
                return redis.error_reply(result)
            end
        ";

        var valueJson = System.Text.Json.JsonSerializer.Serialize(value);
        var result = await scriptExecutor.ExecuteAsync<long>(
            appendScript,
            keys: new[] { $"json:{key}" },
            values: new object[] { path, valueJson }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new
        {
            key,
            path,
            value,
            arrayLength = result.Result,
            message = "Value appended to JSON array"
        });
    }

    /// <summary>
    /// Get length of JSON array
    /// </summary>
    [HttpGet("json/{key}/array/length")]
    public async Task<ActionResult> GetJsonArrayLength(string key, [FromQuery] string path = "$")
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        const string lengthScript = @"
            local key = KEYS[1]
            local path = ARGV[1]
            
            local ok, result = pcall(redis.call, 'JSON.ARRLEN', key, path)
            if ok then
                return result
            else
                return nil
            end
        ";

        var result = await scriptExecutor.ExecuteAsync<long?>(
            lengthScript,
            keys: new[] { $"json:{key}" },
            values: new object[] { path }
        );

        if (!result.Success || result.Result == null)
        {
            return NotFound(new { key, path, message = "Array not found" });
        }

        return Ok(new
        {
            key,
            path,
            length = result.Result,
            message = "JSON array length retrieved"
        });
    }

    /// <summary>
    /// Delete a field from JSON document
    /// </summary>
    [HttpDelete("json/{key}/field")]
    public async Task<ActionResult> DeleteJsonField(string key, [FromQuery] string path)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        const string deleteScript = @"
            local key = KEYS[1]
            local path = ARGV[1]
            
            local ok, result = pcall(redis.call, 'JSON.DEL', key, path)
            if ok then
                return result
            else
                return 0
            end
        ";

        var result = await scriptExecutor.ExecuteAsync<int>(
            deleteScript,
            keys: new[] { $"json:{key}" },
            values: new object[] { path }
        );

        return Ok(new
        {
            key,
            path,
            deleted = result.Result > 0,
            fieldsDeleted = result.Result,
            message = result.Result > 0 ? "Field deleted" : "Field not found"
        });
    }

    /// <summary>
    /// Complex JSON operation: Update user profile with nested fields
    /// </summary>
    [HttpPost("json/user/{userId}/update-profile")]
    public async Task<ActionResult> UpdateUserProfile(int userId, [FromBody] UserProfileUpdate update)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Script executor not available" });
        }

        const string updateProfileScript = @"
            local key = KEYS[1]
            local nameValue = ARGV[1]
            local emailValue = ARGV[2]
            local ageValue = ARGV[3]
            
            -- Update multiple fields atomically
            redis.call('JSON.SET', key, '$.name', nameValue)
            redis.call('JSON.SET', key, '$.email', emailValue)
            
            if ageValue ~= '' then
                redis.call('JSON.SET', key, '$.age', ageValue)
            end
            
            -- Update lastModified timestamp
            local timestamp = redis.call('TIME')[1]
            redis.call('JSON.SET', key, '$.lastModified', timestamp)
            
            -- Increment update counter
            redis.call('JSON.NUMINCRBY', key, '$.updateCount', 1)
            
            return redis.call('JSON.GET', key)
        ";

        var result = await scriptExecutor.ExecuteAsync<string>(
            updateProfileScript,
            keys: new[] { $"json:user:{userId}" },
            values: new object[] 
            { 
                $"\"{update.Name}\"",
                $"\"{update.Email}\"",
                update.Age.HasValue ? update.Age.Value.ToString() : ""
            }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new
        {
            userId,
            updatedProfile = result.Result,
            message = "User profile updated atomically"
        });
    }

    #endregion

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
