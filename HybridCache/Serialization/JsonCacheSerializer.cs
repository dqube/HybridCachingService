using System.Text;
using System.Text.Json;

namespace HybridCache.Serialization;

/// <summary>
/// JSON-based cache serializer using System.Text.Json.
/// </summary>
public class JsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;

    public JsonCacheSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public byte[] Serialize<T>(T value)
    {
        if (value is null)
        {
            return Array.Empty<byte>();
        }

        var json = JsonSerializer.Serialize(value, _options);
        return Encoding.UTF8.GetBytes(json);
    }

    public T? Deserialize<T>(byte[] data)
    {
        if (data is null || data.Length == 0)
        {
            return default;
        }

        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<T>(json, _options);
    }
}
