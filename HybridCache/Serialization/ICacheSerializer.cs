namespace HybridCache.Serialization;

/// <summary>
/// Defines a serializer for cache values.
/// </summary>
public interface ICacheSerializer
{
    /// <summary>
    /// Serializes a value to bytes.
    /// </summary>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes bytes to a value.
    /// </summary>
    T? Deserialize<T>(byte[] data);
}
