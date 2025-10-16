namespace HybridCache.Notifications;

/// <summary>
/// Represents a cache change notification event.
/// </summary>
public class CacheChangeNotification
{
    /// <summary>
    /// Gets or sets the type of cache operation.
    /// </summary>
    public CacheOperation Operation { get; set; }

    /// <summary>
    /// Gets or sets the cache key that changed.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the change occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the source instance that triggered the change.
    /// Useful for avoiding self-notification loops.
    /// </summary>
    public string? SourceInstance { get; set; }

    /// <summary>
    /// Gets or sets optional metadata about the change.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the expiration time if applicable.
    /// </summary>
    public TimeSpan? Expiration { get; set; }
}

/// <summary>
/// Types of cache operations that can trigger notifications.
/// </summary>
public enum CacheOperation
{
    /// <summary>
    /// A value was added or updated in the cache.
    /// </summary>
    Set,

    /// <summary>
    /// A value was removed from the cache.
    /// </summary>
    Remove,

    /// <summary>
    /// A value expired from the cache.
    /// </summary>
    Expire,

    /// <summary>
    /// Multiple keys were removed (bulk operation).
    /// </summary>
    BulkRemove,

    /// <summary>
    /// Cache was cleared.
    /// </summary>
    Clear,

    /// <summary>
    /// A value was refreshed (expiration extended).
    /// </summary>
    Refresh
}

/// <summary>
/// Options for configuring cache change notifications.
/// </summary>
public class CacheNotificationOptions
{
    /// <summary>
    /// Gets or sets whether notifications are enabled.
    /// Default is false.
    /// </summary>
    public bool EnableNotifications { get; set; } = false;

    /// <summary>
    /// Gets or sets the Redis channel name for publishing notifications.
    /// Default is "cache:notifications".
    /// </summary>
    public string NotificationChannel { get; set; } = "cache:notifications";

    /// <summary>
    /// Gets or sets the instance identifier for this cache instance.
    /// If not set, a unique GUID will be generated.
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically invalidate L1 cache on notifications.
    /// Default is true.
    /// </summary>
    public bool AutoInvalidateL1OnNotification { get; set; } = true;

    /// <summary>
    /// Gets or sets which operations should trigger notifications.
    /// Default is all operations.
    /// </summary>
    public CacheOperation[] NotifyOnOperations { get; set; } = new[]
    {
        CacheOperation.Set,
        CacheOperation.Remove,
        CacheOperation.Expire,
        CacheOperation.BulkRemove,
        CacheOperation.Clear
    };

    /// <summary>
    /// Gets or sets whether to ignore notifications from the same instance.
    /// Default is true to avoid self-invalidation loops.
    /// </summary>
    public bool IgnoreSelfNotifications { get; set; } = true;

    /// <summary>
    /// Gets or sets key patterns to include for notifications.
    /// If null or empty, all keys are included.
    /// Supports wildcards: "user:*", "session:*"
    /// </summary>
    public string[]? IncludeKeyPatterns { get; set; }

    /// <summary>
    /// Gets or sets key patterns to exclude from notifications.
    /// Takes precedence over IncludeKeyPatterns.
    /// </summary>
    public string[]? ExcludeKeyPatterns { get; set; }
}
