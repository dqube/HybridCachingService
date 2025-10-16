namespace HybridCachingService.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class Session
{
    public string SessionId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Data { get; set; } = new();
}

public class CacheStats
{
    public string Key { get; set; } = string.Empty;
    public bool ExistsInL1 { get; set; }
    public bool ExistsInL2 { get; set; }
    public int? HashSlot { get; set; }
    public TimeSpan? Ttl { get; set; }
}

public class RateLimitResult
{
    public bool IsAllowed { get; set; }
    public int RequestCount { get; set; }
    public int Limit { get; set; }
    public TimeSpan Window { get; set; }
    public DateTime ResetAt { get; set; }
}
