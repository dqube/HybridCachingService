using Microsoft.AspNetCore.Mvc;
using HybridCache.LuaScripting;
using HybridCachingService.Models;

namespace HybridCachingService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class LuaScriptsController : ControllerBase
{
    private readonly HybridCache.IHybridCache _cache;
    private readonly ILogger<LuaScriptsController> _logger;

    public LuaScriptsController(HybridCache.IHybridCache cache, ILogger<LuaScriptsController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Increment counter with automatic expiration
    /// </summary>
    [HttpPost("counter/{key}/increment")]
    public async Task<ActionResult> IncrementCounter(string key, [FromQuery] int amount = 1)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var result = await scriptExecutor.ExecuteAsync<long>(
            LuaScripts.IncrementWithExpiration,
            keys: new[] { $"counter:{key}" },
            values: new object[] { amount, 3600 } // 1 hour expiration
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new { counter = key, value = result.Result, operation = "increment" });
    }

    /// <summary>
    /// Get counter value
    /// </summary>
    [HttpGet("counter/{key}")]
    public async Task<ActionResult> GetCounter(string key)
    {
        var fullKey = $"counter:{key}";
        var value = await _cache.GetAsync<long?>(fullKey);

        return Ok(new { counter = key, value = value ?? 0 });
    }

    /// <summary>
    /// Rate limit check using sliding window
    /// </summary>
    [HttpPost("ratelimit/{userId}/check")]
    public async Task<ActionResult<RateLimitResult>> CheckRateLimit(
        string userId,
        [FromQuery] int maxRequests = 10,
        [FromQuery] int windowSeconds = 60)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var result = await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.RateLimitSlidingWindow,
            keys: new[] { $"ratelimit:{userId}" },
            values: new object[]
            {
                maxRequests,
                windowSeconds,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new RateLimitResult
        {
            IsAllowed = result.Result == 1,
            Limit = maxRequests,
            Window = TimeSpan.FromSeconds(windowSeconds),
            ResetAt = DateTime.UtcNow.AddSeconds(windowSeconds)
        });
    }

    /// <summary>
    /// Acquire distributed lock
    /// </summary>
    [HttpPost("lock/{resourceKey}/acquire")]
    public async Task<ActionResult> AcquireLock(string resourceKey, [FromQuery] int timeoutSeconds = 30)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var lockToken = Guid.NewGuid().ToString();
        var lockKey = $"lock:{resourceKey}";

        var result = await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.AcquireLock,
            keys: new[] { lockKey },
            values: new object[] { lockToken, timeoutSeconds }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        if (result.Result == 0)
        {
            return Conflict(new { message = "Lock already acquired by another process" });
        }

        return Ok(new
        {
            message = "Lock acquired",
            resource = resourceKey,
            lockToken,
            expiresIn = timeoutSeconds
        });
    }

    /// <summary>
    /// Release distributed lock
    /// </summary>
    [HttpPost("lock/{resourceKey}/release")]
    public async Task<ActionResult> ReleaseLock(string resourceKey, [FromQuery] string lockToken)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var lockKey = $"lock:{resourceKey}";

        var result = await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.ReleaseLock,
            keys: new[] { lockKey },
            values: new object[] { lockToken }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        if (result.Result == 0)
        {
            return BadRequest(new { message = "Lock token mismatch or lock doesn't exist" });
        }

        return Ok(new { message = "Lock released", resource = resourceKey });
    }

    /// <summary>
    /// Compare and swap operation
    /// </summary>
    [HttpPost("cas/{key}")]
    public async Task<ActionResult> CompareAndSwap(
        string key,
        [FromQuery] string expectedValue,
        [FromQuery] string newValue)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var result = await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.CompareAndSwap,
            keys: new[] { key },
            values: new object[] { expectedValue, newValue, 3600 }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        if (result.Result == 0)
        {
            return Conflict(new { message = "Current value doesn't match expected value" });
        }

        return Ok(new { message = "Value updated successfully", key, newValue });
    }

    /// <summary>
    /// Set value only if it doesn't exist
    /// </summary>
    [HttpPost("setnx/{key}")]
    public async Task<ActionResult> SetIfNotExists(string key, [FromBody] string value)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var result = await scriptExecutor.ExecuteAsync<int>(
            LuaScripts.SetIfNotExists,
            keys: new[] { key },
            values: new object[] { value, 3600 }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        if (result.Result == 0)
        {
            return Conflict(new { message = "Key already exists" });
        }

        return Ok(new { message = "Value set successfully", key });
    }

    /// <summary>
    /// Get multiple keys in one operation
    /// </summary>
    [HttpPost("mget")]
    public async Task<ActionResult> GetMultiple([FromBody] string[] keys)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var result = await scriptExecutor.ExecuteAsync(
            LuaScripts.GetMultiple,
            keys: keys
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new { keys, values = result.Result });
    }

    /// <summary>
    /// Add activity to limited-size log
    /// </summary>
    [HttpPost("user/{userId}/activity")]
    public async Task<ActionResult> LogActivity(
        string userId,
        [FromBody] string activity,
        [FromQuery] int maxActivities = 50)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var activityData = System.Text.Json.JsonSerializer.Serialize(new
        {
            activity,
            timestamp = DateTime.UtcNow
        });

        var result = await scriptExecutor.ExecuteAsync<long>(
            LuaScripts.AppendToLimitedList,
            keys: new[] { $"activities:{userId}" },
            values: new object[] { activityData, maxActivities, 86400 }
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new
        {
            message = "Activity logged",
            userId,
            listSize = result.Result
        });
    }

    /// <summary>
    /// Custom Lua script execution
    /// </summary>
    [HttpPost("execute")]
    public async Task<ActionResult> ExecuteCustomScript(
        [FromBody] CustomScriptRequest request)
    {
        var scriptExecutor = _cache.ScriptExecutor;
        if (scriptExecutor == null)
        {
            return BadRequest(new { message = "Lua script executor not available" });
        }

        var result = await scriptExecutor.ExecuteAsync(
            request.Script,
            keys: request.Keys,
            values: request.Values
        );

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new { result = result.Result });
    }

    public class CustomScriptRequest
    {
        public string Script { get; set; } = string.Empty;
        public string[]? Keys { get; set; }
        public object[]? Values { get; set; }
    }
}
