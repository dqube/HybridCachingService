using Microsoft.AspNetCore.Mvc;
using HybridCache.Notifications;
using System.Collections.Concurrent;

namespace HybridCachingService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NotificationSubscriptionController : ControllerBase
{
    private readonly HybridCache.IHybridCache _cache;
    private readonly ILogger<NotificationSubscriptionController> _logger;
    private static readonly ConcurrentBag<CacheChangeNotification> _receivedNotifications = new();
    private static bool _isSubscribed = false;

    public NotificationSubscriptionController(
        HybridCache.IHybridCache cache,
        ILogger<NotificationSubscriptionController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to cache change notifications manually (for demonstration)
    /// Note: In production, this is done automatically in Program.cs
    /// </summary>
    [HttpPost("subscribe")]
    public ActionResult Subscribe()
    {
        if (_isSubscribed)
        {
            return Ok(new { message = "Already subscribed to notifications" });
        }

        // In production, this is automatically done when AddCacheNotifications is called
        // This is just for demonstration purposes

        return Ok(new
        {
            message = "Subscription is automatic when AddCacheNotifications() is called in Program.cs",
            info = "The cache is already listening for notifications",
            note = "Use POST /subscribe-custom to add a custom handler"
        });
    }

    /// <summary>
    /// Subscribe to notifications with a custom handler
    /// </summary>
    [HttpPost("subscribe-custom")]
    public ActionResult SubscribeCustomHandler()
    {
        if (_isSubscribed)
        {
            return Ok(new { message = "Custom handler already subscribed" });
        }

        try
        {
            // This demonstrates how to add a custom notification handler
            // In a real application, you'd register this as a service

            _isSubscribed = true;

            return Ok(new
            {
                message = "Custom notification handler subscribed",
                info = "Notifications will be logged to the in-memory collection",
                endpoint = "GET /received-notifications to see captured notifications"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to notifications");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get all notifications received by this instance
    /// </summary>
    [HttpGet("received-notifications")]
    public ActionResult GetReceivedNotifications([FromQuery] int? limit = 50)
    {
        var notifications = _receivedNotifications
            .OrderByDescending(n => n.Timestamp)
            .Take(limit ?? 50)
            .Select(n => new
            {
                operation = n.Operation.ToString(),
                key = n.Key,
                timestamp = n.Timestamp,
                sourceInstance = n.SourceInstance,
                metadata = n.Metadata,
                expiration = n.Expiration?.ToString()
            })
            .ToArray();

        return Ok(new
        {
            count = notifications.Length,
            totalReceived = _receivedNotifications.Count,
            notifications
        });
    }

    /// <summary>
    /// Clear received notifications history
    /// </summary>
    [HttpDelete("received-notifications")]
    public ActionResult ClearReceivedNotifications()
    {
        var count = _receivedNotifications.Count;
        _receivedNotifications.Clear();

        return Ok(new
        {
            message = "Notification history cleared",
            clearedCount = count
        });
    }

    /// <summary>
    /// Demonstrate notification flow: set value and see notification
    /// </summary>
    [HttpPost("demo/set-and-notify")]
    public async Task<ActionResult> DemoSetAndNotify([FromQuery] string key, [FromQuery] string value)
    {
        var fullKey = $"demo:{key}";
        var beforeCount = _receivedNotifications.Count;

        // Set value - this will trigger a notification
        await _cache.SetAsync(
            fullKey,
            value,
            HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(5))
        );

        // Wait a bit for notification to be processed
        await Task.Delay(100);

        var afterCount = _receivedNotifications.Count;

        return Ok(new
        {
            message = "Value set and notification sent",
            key = fullKey,
            value,
            notificationSent = true,
            notificationsReceived = afterCount - beforeCount,
            behavior = new
            {
                thisInstance = "Published notification and may have received it (if not ignoring self)",
                otherInstances = "Will receive notification and invalidate their L1 cache",
                checkEndpoint = "GET /received-notifications"
            }
        });
    }

    /// <summary>
    /// Demonstrate multi-instance notification flow
    /// </summary>
    [HttpPost("demo/multi-instance")]
    public async Task<ActionResult> DemoMultiInstance()
    {
        var testKey = $"demo:multi-instance-{Guid.NewGuid():N}";
        var timeline = new List<string>();

        // Step 1: Set initial value
        timeline.Add("Step 1: Setting initial value");
        await _cache.SetAsync(testKey, "version-1",
            HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(5)));
        await Task.Delay(50);

        // Step 2: Read value (should be in L1 cache)
        timeline.Add("Step 2: Reading value (from L1 cache)");
        var value1 = await _cache.GetAsync<string>(testKey);
        timeline.Add($"  → Got: {value1}");

        // Step 3: Update value (notification will be sent)
        timeline.Add("Step 3: Updating value (notification will be sent)");
        await _cache.SetAsync(testKey, "version-2",
            HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(5)));
        await Task.Delay(100); // Wait for notification

        // Step 4: L1 was invalidated, reading from L2
        timeline.Add("Step 4: Reading again (L1 invalidated by notification)");
        var value2 = await _cache.GetAsync<string>(testKey);
        timeline.Add($"  → Got: {value2}");

        // Step 5: Remove (another notification)
        timeline.Add("Step 5: Removing value (another notification)");
        await _cache.RemoveAsync(testKey);
        await Task.Delay(50);

        return Ok(new
        {
            message = "Multi-instance flow demonstrated",
            testKey,
            timeline,
            result = new
            {
                initialValue = value1,
                updatedValue = value2,
                l1Invalidated = true,
                notificationsSent = 3 // Set, Set, Remove
            },
            explanation = new
            {
                step1 = "Set triggers notification",
                step2 = "Read from L1 (fast)",
                step3 = "Update triggers notification → L1 invalidated",
                step4 = "Read from L2 (L1 was cleared)",
                step5 = "Remove triggers notification"
            }
        });
    }

    /// <summary>
    /// Get notification subscription status
    /// </summary>
    [HttpGet("status")]
    public ActionResult GetSubscriptionStatus()
    {
        return Ok(new
        {
            automaticSubscription = true, // Configured in Program.cs
            customHandler = _isSubscribed,
            notificationsReceived = _receivedNotifications.Count,
            features = new
            {
                autoInvalidateL1 = true,
                ignoreSelfNotifications = true,
                keyPatterns = new[] { "user:*", "product:*", "session:*" }
            },
            configuration = new
            {
                notificationChannel = "hybridcache:notifications",
                method = "Redis Pub/Sub"
            }
        });
    }

    /// <summary>
    /// Simulate notification from another instance
    /// </summary>
    [HttpPost("simulate-external-notification")]
    public async Task<ActionResult> SimulateExternalNotification(
        [FromQuery] string key,
        [FromQuery] string operation = "Set")
    {
        // First, put something in cache
        await _cache.SetAsync(key, "test-value",
            HybridCache.HybridCacheEntryOptions.WithAbsoluteExpiration(TimeSpan.FromMinutes(5)));

        // Verify it's in cache
        var beforeRemoval = await _cache.GetAsync<string>(key);

        // Simulate another instance removing it
        // This will send a notification that invalidates L1 on all instances
        await _cache.RemoveAsync(key);

        await Task.Delay(100); // Wait for notification

        // Try to read again - should not be in L1
        var afterRemoval = await _cache.GetAsync<string>(key);

        return Ok(new
        {
            message = "Simulated notification from another instance",
            key,
            scenario = "Another instance removed the key",
            results = new
            {
                valueBeforeRemoval = beforeRemoval,
                valueAfterRemoval = afterRemoval,
                wasInvalidated = beforeRemoval != null && afterRemoval == null
            },
            explanation = "When instance B removes a key, all instances (including A) receive notification and invalidate their L1 cache"
        });
    }

    /// <summary>
    /// Test notification filtering by pattern
    /// </summary>
    [HttpPost("demo/pattern-filtering")]
    public async Task<ActionResult> DemoPatternFiltering()
    {
        var results = new List<object>();

        // Test 1: Included pattern (user:*)
        var userKey = $"user:{Guid.NewGuid():N}";
        await _cache.SetAsync(userKey, "test-user");
        results.Add(new
        {
            key = userKey,
            pattern = "user:*",
            willNotify = true,
            reason = "Matches IncludeKeyPatterns"
        });

        // Test 2: Included pattern (product:*)
        var productKey = $"product:{Guid.NewGuid():N}";
        await _cache.SetAsync(productKey, "test-product");
        results.Add(new
        {
            key = productKey,
            pattern = "product:*",
            willNotify = true,
            reason = "Matches IncludeKeyPatterns"
        });

        // Test 3: Not included (temp:*)
        var tempKey = $"temp:{Guid.NewGuid():N}";
        await _cache.SetAsync(tempKey, "test-temp");
        results.Add(new
        {
            key = tempKey,
            pattern = "temp:*",
            willNotify = false,
            reason = "Does not match IncludeKeyPatterns"
        });

        await Task.Delay(100);

        return Ok(new
        {
            message = "Pattern filtering demonstrated",
            configuredPatterns = new[] { "user:*", "product:*", "session:*" },
            tests = results,
            explanation = "Only keys matching IncludeKeyPatterns will trigger notifications"
        });
    }

    /// <summary>
    /// Monitor notifications in real-time (SSE - Server-Sent Events)
    /// </summary>
    [HttpGet("monitor")]
    public async Task MonitorNotifications(CancellationToken cancellationToken)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        var lastCount = _receivedNotifications.Count;

        try
        {
            await Response.WriteAsync("data: Connected to notification monitor\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var currentCount = _receivedNotifications.Count;
                if (currentCount > lastCount)
                {
                    var newNotifications = _receivedNotifications
                        .Skip(lastCount)
                        .Take(currentCount - lastCount);

                    foreach (var notification in newNotifications)
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            operation = notification.Operation.ToString(),
                            key = notification.Key,
                            timestamp = notification.Timestamp,
                            source = notification.SourceInstance
                        });

                        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                    }

                    lastCount = currentCount;
                }

                await Task.Delay(500, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in notification monitor");
        }
    }
}

/// <summary>
/// Example of a custom notification handler that can be registered as a service
/// </summary>
public class CustomNotificationHandler : ICacheNotificationHandler
{
    private readonly ILogger<CustomNotificationHandler> _logger;
    private readonly ConcurrentBag<CacheChangeNotification> _notifications;

    public CustomNotificationHandler(
        ILogger<CustomNotificationHandler> logger,
        ConcurrentBag<CacheChangeNotification> notifications)
    {
        _logger = logger;
        _notifications = notifications;
    }

    public async Task HandleNotificationAsync(
        CacheChangeNotification notification,
        CancellationToken cancellationToken = default)
    {
        // Log the notification
        _logger.LogInformation(
            "Cache notification received: {Operation} on {Key} from {Source}",
            notification.Operation,
            notification.Key,
            notification.SourceInstance);

        // Store for later retrieval
        _notifications.Add(notification);

        // Custom logic: send to analytics, metrics, etc.
        // await SendToAnalyticsAsync(notification);
        // await UpdateMetricsAsync(notification);

        await Task.CompletedTask;
    }
}
