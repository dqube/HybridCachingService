using StackExchange.Redis;

namespace HybridCache.Clustering;

/// <summary>
/// Helper methods for Redis cluster operations.
/// </summary>
public static class RedisClusterHelper
{
    /// <summary>
    /// Calculates the Redis hash slot for a given key.
    /// Redis uses CRC16 of the key modulo 16384.
    /// </summary>
    /// <param name="key">The key to calculate the hash slot for.</param>
    /// <returns>The hash slot (0-16383).</returns>
    public static int CalculateHashSlot(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        // Extract hash tag if present (e.g., {user}:123 -> user)
        var hashKey = ExtractHashTag(key);

        return CalculateCrc16(hashKey) % 16384;
    }

    /// <summary>
    /// Calculates the Redis hash slot for a RedisKey.
    /// </summary>
    public static int CalculateHashSlot(RedisKey key)
    {
        return CalculateHashSlot(key.ToString());
    }

    /// <summary>
    /// Extracts the hash tag from a key if present.
    /// Hash tags are denoted by curly braces: {tag}
    /// </summary>
    /// <param name="key">The key to extract the hash tag from.</param>
    /// <returns>The hash tag if present, otherwise the entire key.</returns>
    public static string ExtractHashTag(string key)
    {
        var start = key.IndexOf('{');
        if (start < 0)
            return key;

        var end = key.IndexOf('}', start + 1);
        if (end <= start + 1)
            return key;

        return key.Substring(start + 1, end - start - 1);
    }

    /// <summary>
    /// Validates that all keys map to the same hash slot.
    /// This is required for multi-key operations in Redis cluster mode.
    /// </summary>
    /// <param name="keys">The keys to validate.</param>
    /// <returns>True if all keys map to the same slot, false otherwise.</returns>
    public static bool ValidateHashSlots(params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            return true;

        if (keys.Length == 1)
            return true;

        var firstSlot = CalculateHashSlot(keys[0]);
        for (int i = 1; i < keys.Length; i++)
        {
            if (CalculateHashSlot(keys[i]) != firstSlot)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Wraps a key with a hash tag to ensure it routes to a specific slot.
    /// </summary>
    /// <param name="key">The key to wrap.</param>
    /// <param name="hashTag">The hash tag to use.</param>
    /// <returns>The key with hash tag: {hashTag}:key</returns>
    public static string WrapWithHashTag(string key, string hashTag)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (string.IsNullOrEmpty(hashTag))
            return key;

        // Check if key already has a hash tag
        if (key.Contains('{') && key.Contains('}'))
            return key;

        return $"{{{hashTag}}}:{key}";
    }

    /// <summary>
    /// Wraps multiple keys with the same hash tag to ensure they route to the same slot.
    /// </summary>
    /// <param name="hashTag">The hash tag to use for all keys.</param>
    /// <param name="keys">The keys to wrap.</param>
    /// <returns>Keys wrapped with the same hash tag.</returns>
    public static string[] WrapKeysWithHashTag(string hashTag, params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            return Array.Empty<string>();

        return keys.Select(k => WrapWithHashTag(k, hashTag)).ToArray();
    }

    /// <summary>
    /// Gets a common hash tag from multiple keys if they all share one.
    /// </summary>
    /// <param name="keys">The keys to analyze.</param>
    /// <returns>The common hash tag, or null if none exists.</returns>
    public static string? GetCommonHashTag(params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            return null;

        var firstTag = ExtractHashTag(keys[0]);
        if (firstTag == keys[0]) // No hash tag in first key
            return null;

        for (int i = 1; i < keys.Length; i++)
        {
            var tag = ExtractHashTag(keys[i]);
            if (tag != firstTag)
                return null;
        }

        return firstTag;
    }

    /// <summary>
    /// CRC16 implementation used by Redis for hash slot calculation.
    /// </summary>
    private static ushort CalculateCrc16(string key)
    {
        const ushort polynomial = 0x1021;
        ushort crc = 0;

        var bytes = System.Text.Encoding.UTF8.GetBytes(key);

        foreach (var b in bytes)
        {
            crc ^= (ushort)(b << 8);

            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ polynomial);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }

        return crc;
    }

    /// <summary>
    /// Checks if a connection is running in cluster mode.
    /// </summary>
    public static bool IsClusterMode(IConnectionMultiplexer redis)
    {
        if (redis == null)
            throw new ArgumentNullException(nameof(redis));

        try
        {
            var endpoints = redis.GetEndPoints();
            if (endpoints.Length == 0)
                return false;

            var server = redis.GetServer(endpoints[0]);
            return server.ServerType == ServerType.Cluster;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets cluster information from Redis.
    /// </summary>
    public static async Task<ClusterInfo?> GetClusterInfoAsync(IConnectionMultiplexer redis)
    {
        if (redis == null)
            throw new ArgumentNullException(nameof(redis));

        try
        {
            var endpoints = redis.GetEndPoints();
            if (endpoints.Length == 0)
                return null;

            var server = redis.GetServer(endpoints[0]);
            if (server.ServerType != ServerType.Cluster)
                return null;

            var clusterNodes = await server.ExecuteAsync("CLUSTER", "NODES");
            var clusterInfo = await server.ExecuteAsync("CLUSTER", "INFO");

            return new ClusterInfo
            {
                IsClusterEnabled = true,
                NodeCount = endpoints.Length,
                ClusterState = ParseClusterState(clusterInfo.ToString()),
                Nodes = endpoints.Select(e => e?.ToString() ?? string.Empty).ToArray()
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ParseClusterState(string? clusterInfo)
    {
        if (string.IsNullOrEmpty(clusterInfo))
            return "unknown";

        foreach (var line in clusterInfo.Split('\n'))
        {
            if (line.StartsWith("cluster_state:"))
            {
                return line.Split(':')[1].Trim();
            }
        }

        return "unknown";
    }
}

/// <summary>
/// Information about a Redis cluster.
/// </summary>
public class ClusterInfo
{
    /// <summary>
    /// Gets or sets whether the cluster is enabled.
    /// </summary>
    public bool IsClusterEnabled { get; set; }

    /// <summary>
    /// Gets or sets the number of nodes in the cluster.
    /// </summary>
    public int NodeCount { get; set; }

    /// <summary>
    /// Gets or sets the cluster state (ok, fail, etc.).
    /// </summary>
    public string ClusterState { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets the list of cluster nodes.
    /// </summary>
    public string[] Nodes { get; set; } = Array.Empty<string>();
}
