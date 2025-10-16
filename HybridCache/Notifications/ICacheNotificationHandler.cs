namespace HybridCache.Notifications;

/// <summary>
/// Handles cache change notifications.
/// </summary>
public interface ICacheNotificationHandler
{
    /// <summary>
    /// Called when a cache change notification is received.
    /// </summary>
    /// <param name="notification">The cache change notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleNotificationAsync(CacheChangeNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes cache change notifications.
/// </summary>
public interface ICacheNotificationPublisher
{
    /// <summary>
    /// Publishes a cache change notification.
    /// </summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(CacheChangeNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Subscribes to cache change notifications.
/// </summary>
public interface ICacheNotificationSubscriber : IDisposable
{
    /// <summary>
    /// Subscribes to cache change notifications.
    /// </summary>
    /// <param name="handler">The handler to invoke for each notification.</param>
    Task SubscribeAsync(Func<CacheChangeNotification, Task> handler);

    /// <summary>
    /// Unsubscribes from cache change notifications.
    /// </summary>
    Task UnsubscribeAsync();

    /// <summary>
    /// Gets whether currently subscribed.
    /// </summary>
    bool IsSubscribed { get; }
}
