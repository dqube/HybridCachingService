using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridCache.Notifications;

/// <summary>
/// Redis-based implementation of cache notification pub/sub.
/// </summary>
public class RedisCacheNotificationService : ICacheNotificationPublisher, ICacheNotificationSubscriber
{
    private readonly IConnectionMultiplexer _redis;
    private readonly CacheNotificationOptions _options;
    private readonly ILogger<RedisCacheNotificationService>? _logger;
    private readonly string _instanceId;
    private ISubscriber? _subscriber;
    private Func<CacheChangeNotification, Task>? _handler;
    private bool _isSubscribed;

    public RedisCacheNotificationService(
        IConnectionMultiplexer redis,
        IOptions<CacheNotificationOptions> options,
        ILogger<RedisCacheNotificationService>? logger = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _instanceId = _options.InstanceId ?? Guid.NewGuid().ToString();
    }

    public bool IsSubscribed => _isSubscribed;

    public async Task PublishAsync(CacheChangeNotification notification, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableNotifications)
            return;

        try
        {
            // Check if operation should trigger notification
            if (!_options.NotifyOnOperations.Contains(notification.Operation))
                return;

            // Check key patterns
            if (!ShouldNotifyForKey(notification.Key))
                return;

            // Set source instance
            notification.SourceInstance = _instanceId;

            // Serialize notification
            var message = JsonSerializer.Serialize(notification);

            // Publish to Redis channel
            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(
                new RedisChannel(_options.NotificationChannel, RedisChannel.PatternMode.Literal),
                message);

            _logger?.LogDebug(
                "Published cache notification: Operation={Operation}, Key={Key}, Instance={Instance}",
                notification.Operation,
                notification.Key,
                _instanceId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to publish cache notification for key: {Key}", notification.Key);
            // Don't throw - notification failures shouldn't break cache operations
        }
    }

    public async Task SubscribeAsync(Func<CacheChangeNotification, Task> handler)
    {
        if (_isSubscribed)
            return;

        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _subscriber = _redis.GetSubscriber();

        await _subscriber.SubscribeAsync(
            new RedisChannel(_options.NotificationChannel, RedisChannel.PatternMode.Literal),
            async (channel, message) => await OnMessageReceivedAsync(message));

        _isSubscribed = true;

        _logger?.LogInformation(
            "Subscribed to cache notifications on channel: {Channel}, Instance: {Instance}",
            _options.NotificationChannel,
            _instanceId);
    }

    public async Task UnsubscribeAsync()
    {
        if (!_isSubscribed || _subscriber == null)
            return;

        await _subscriber.UnsubscribeAsync(
            new RedisChannel(_options.NotificationChannel, RedisChannel.PatternMode.Literal));

        _isSubscribed = false;
        _handler = null;

        _logger?.LogInformation(
            "Unsubscribed from cache notifications on channel: {Channel}",
            _options.NotificationChannel);
    }

    private async Task OnMessageReceivedAsync(RedisValue message)
    {
        try
        {
            if (message.IsNullOrEmpty || _handler == null)
                return;

            var notification = JsonSerializer.Deserialize<CacheChangeNotification>(message.ToString());
            if (notification == null)
                return;

            // Ignore self-notifications if configured
            if (_options.IgnoreSelfNotifications && notification.SourceInstance == _instanceId)
            {
                _logger?.LogDebug("Ignoring self-notification for key: {Key}", notification.Key);
                return;
            }

            // Check key patterns
            if (!ShouldNotifyForKey(notification.Key))
                return;

            _logger?.LogDebug(
                "Received cache notification: Operation={Operation}, Key={Key}, Source={Source}",
                notification.Operation,
                notification.Key,
                notification.SourceInstance);

            await _handler(notification);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling cache notification");
        }
    }

    private bool ShouldNotifyForKey(string key)
    {
        // Check exclude patterns first
        if (_options.ExcludeKeyPatterns != null && _options.ExcludeKeyPatterns.Length > 0)
        {
            if (_options.ExcludeKeyPatterns.Any(pattern => MatchesPattern(key, pattern)))
                return false;
        }

        // Check include patterns
        if (_options.IncludeKeyPatterns != null && _options.IncludeKeyPatterns.Length > 0)
        {
            return _options.IncludeKeyPatterns.Any(pattern => MatchesPattern(key, pattern));
        }

        // If no patterns specified, include all
        return true;
    }

    private bool MatchesPattern(string key, string pattern)
    {
        // Simple wildcard matching (* matches any characters)
        if (pattern == "*")
            return true;

        if (!pattern.Contains('*'))
            return key == pattern;

        // Convert wildcard pattern to regex
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(key, regexPattern);
    }

    public void Dispose()
    {
        if (_isSubscribed)
        {
            UnsubscribeAsync().GetAwaiter().GetResult();
        }
    }
}
