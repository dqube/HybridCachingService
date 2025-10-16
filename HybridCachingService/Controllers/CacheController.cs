using Microsoft.AspNetCore.Mvc;
using HybridCachingService.Models;

namespace HybridCachingService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CacheController : ControllerBase
{
    private readonly HybridCache.IHybridCache _cache;
    private readonly ILogger<CacheController> _logger;

    public CacheController(HybridCache.IHybridCache cache, ILogger<CacheController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get a user from cache
    /// </summary>
    [HttpGet("user/{id}")]
    public async Task<ActionResult<User>> GetUser(int id)
    {
        var key = $"user:{id}";
        var user = await _cache.GetAsync<User>(key);

        if (user == null)
        {
            return NotFound(new { message = $"User {id} not found in cache" });
        }

        return Ok(user);
    }

    /// <summary>
    /// Create or update a user in cache
    /// </summary>
    [HttpPut("user/{id}")]
    public async Task<ActionResult> SetUser(int id, [FromBody] User user)
    {
        user.Id = id;
        user.LastModified = DateTime.UtcNow;

        var key = $"user:{id}";
        await _cache.SetAsync(key, user, HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(15)));

        _logger.LogInformation("User {UserId} cached successfully", id);

        return Ok(new { message = $"User {id} cached successfully", key });
    }

    /// <summary>
    /// Get or create a user (cache-aside pattern)
    /// </summary>
    [HttpPost("user/{id}/get-or-create")]
    public async Task<ActionResult<User>> GetOrCreateUser(int id, [FromBody] User userData)
    {
        var key = $"user:{id}";

        var user = await _cache.GetOrCreateAsync(
            key,
            async ct =>
            {
                // Simulate database fetch
                _logger.LogInformation("User {UserId} not in cache, creating new entry", id);
                userData.Id = id;
                userData.CreatedAt = DateTime.UtcNow;
                await Task.Delay(100, ct); // Simulate DB latency
                return userData;
            },
            HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(15))
        );

        return Ok(new { user, source = user.CreatedAt == DateTime.UtcNow ? "created" : "cache" });
    }

    /// <summary>
    /// Remove a user from cache
    /// </summary>
    [HttpDelete("user/{id}")]
    public async Task<ActionResult> RemoveUser(int id)
    {
        var key = $"user:{id}";
        await _cache.RemoveAsync(key);

        _logger.LogInformation("User {UserId} removed from cache", id);

        return Ok(new { message = $"User {id} removed from cache" });
    }

    /// <summary>
    /// Remove from L1 cache only (for testing)
    /// </summary>
    [HttpDelete("user/{id}/local")]
    public ActionResult RemoveUserLocal(int id)
    {
        var key = $"user:{id}";
        _cache.RemoveLocal(key);

        _logger.LogInformation("User {UserId} removed from L1 cache only", id);

        return Ok(new { message = $"User {id} removed from L1 cache" });
    }

    /// <summary>
    /// Cache a product
    /// </summary>
    [HttpPut("product/{id}")]
    public async Task<ActionResult> SetProduct(int id, [FromBody] Product product)
    {
        product.Id = id;
        var key = $"product:{id}";

        // Use sliding expiration for frequently accessed products
        var options = new HybridCache.HybridCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            LocalCacheExpiration = TimeSpan.FromMinutes(2)
        };

        await _cache.SetAsync(key, product, options);

        return Ok(new { message = $"Product {id} cached with sliding expiration", key });
    }

    /// <summary>
    /// Get a product from cache
    /// </summary>
    [HttpGet("product/{id}")]
    public async Task<ActionResult<Product>> GetProduct(int id)
    {
        var key = $"product:{id}";
        var product = await _cache.GetAsync<Product>(key);

        if (product == null)
        {
            return NotFound(new { message = $"Product {id} not found in cache" });
        }

        return Ok(product);
    }

    /// <summary>
    /// Batch cache multiple products
    /// </summary>
    [HttpPost("products/batch")]
    public async Task<ActionResult> SetProducts([FromBody] List<Product> products)
    {
        var tasks = products.Select(async product =>
        {
            var key = $"product:{product.Id}";
            await _cache.SetAsync(key, product,
                HybridCache.HybridCacheEntryOptions.WithSlidingExpiration(TimeSpan.FromMinutes(10)));
        });

        await Task.WhenAll(tasks);

        return Ok(new { message = $"{products.Count} products cached successfully" });
    }

    /// <summary>
    /// Cache with specific L1/L2 configuration
    /// </summary>
    [HttpPost("session")]
    public async Task<ActionResult> CreateSession([FromBody] Session session)
    {
        var key = $"session:{session.SessionId}";

        // Store in both L1 and L2, but with shorter L1 expiration
        var options = new HybridCache.HybridCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            LocalCacheExpiration = TimeSpan.FromMinutes(5),
            UseL1Cache = true,
            UseL2Cache = true
        };

        await _cache.SetAsync(key, session, options);

        return Ok(new
        {
            message = "Session cached",
            sessionId = session.SessionId,
            l1Expiration = "5 minutes",
            l2Expiration = "1 hour"
        });
    }

    /// <summary>
    /// Get session
    /// </summary>
    [HttpGet("session/{sessionId}")]
    public async Task<ActionResult<Session>> GetSession(string sessionId)
    {
        var key = $"session:{sessionId}";
        var session = await _cache.GetAsync<Session>(key);

        if (session == null)
        {
            return NotFound(new { message = "Session not found or expired" });
        }

        return Ok(session);
    }

    /// <summary>
    /// L1 only cache (fast, not distributed)
    /// </summary>
    [HttpPost("cache/l1-only")]
    public async Task<ActionResult> SetL1Only([FromBody] Dictionary<string, string> data)
    {
        var key = $"temp:{Guid.NewGuid()}";

        var options = new HybridCache.HybridCacheEntryOptions
        {
            UseL1Cache = true,
            UseL2Cache = false,
            LocalCacheExpiration = TimeSpan.FromMinutes(1)
        };

        await _cache.SetAsync(key, data, options);

        return Ok(new { message = "Data cached in L1 only", key });
    }

    /// <summary>
    /// L2 only cache (distributed, not local)
    /// </summary>
    [HttpPost("cache/l2-only")]
    public async Task<ActionResult> SetL2Only([FromBody] Dictionary<string, string> data)
    {
        var key = $"distributed:{Guid.NewGuid()}";

        var options = new HybridCache.HybridCacheEntryOptions
        {
            UseL1Cache = false,
            UseL2Cache = true,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        };

        await _cache.SetAsync(key, data, options);

        return Ok(new { message = "Data cached in L2 only (distributed)", key });
    }
}
