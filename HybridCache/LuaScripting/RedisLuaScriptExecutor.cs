using StackExchange.Redis;

namespace HybridCache.LuaScripting;

/// <summary>
/// Redis implementation of Lua script executor using StackExchange.Redis.
/// </summary>
public class RedisLuaScriptExecutor : ILuaScriptExecutor
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string? _keyPrefix;

    public RedisLuaScriptExecutor(IConnectionMultiplexer redis, string? keyPrefix = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _keyPrefix = keyPrefix;
    }

    public async Task<LuaScriptResult> ExecuteAsync(
        string script,
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var redisKeys = PrepareKeys(keys);
            var redisValues = PrepareValues(values);

            var result = await db.ScriptEvaluateAsync(
                script,
                redisKeys,
                redisValues);

            return LuaScriptResult.CreateSuccess(ConvertRedisResult(result));
        }
        catch (Exception ex)
        {
            return LuaScriptResult.CreateFailure(ex.Message);
        }
    }

    public async Task<LuaScriptResult<T>> ExecuteAsync<T>(
        string script,
        string[]? keys = null,
        object[]? values = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var redisKeys = PrepareKeys(keys);
            var redisValues = PrepareValues(values);

            var result = await db.ScriptEvaluateAsync(
                script,
                redisKeys,
                redisValues);

            var convertedResult = ConvertRedisResult<T>(result);
            return LuaScriptResult<T>.CreateSuccess(convertedResult);
        }
        catch (Exception ex)
        {
            return LuaScriptResult<T>.CreateFailure(ex.Message);
        }
    }

    public Task<IPreparedLuaScript> PrepareAsync(string script)
    {
        IPreparedLuaScript prepared = new PreparedRedisLuaScript(script, this);
        return Task.FromResult(prepared);
    }

    private RedisKey[] PrepareKeys(string[]? keys)
    {
        if (keys is null || keys.Length == 0)
            return Array.Empty<RedisKey>();

        var redisKeys = new RedisKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            redisKeys[i] = string.IsNullOrEmpty(_keyPrefix)
                ? keys[i]
                : $"{_keyPrefix}:{keys[i]}";
        }
        return redisKeys;
    }

    private RedisValue[] PrepareValues(object[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<RedisValue>();

        var redisValues = new RedisValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            redisValues[i] = ConvertToRedisValue(values[i]);
        }
        return redisValues;
    }

    private RedisValue ConvertToRedisValue(object? value)
    {
        return value switch
        {
            null => RedisValue.Null,
            string s => s,
            int i => i,
            long l => l,
            double d => d,
            bool b => b,
            byte[] bytes => bytes,
            _ => System.Text.Json.JsonSerializer.Serialize(value)
        };
    }

    private object? ConvertRedisResult(RedisResult result)
    {
        if (result.IsNull)
            return null;

        return result.Resp2Type switch
        {
            ResultType.Integer => (long)result,
            ResultType.SimpleString or ResultType.BulkString => (string?)result,
            ResultType.Array => ((RedisResult[])result!).Select(ConvertRedisResult).ToArray(),
            _ => result.ToString()
        };
    }

    private T ConvertRedisResult<T>(RedisResult result)
    {
        if (result.IsNull)
            return default!;

        var type = typeof(T);

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            if (result.IsNull)
                return default!;
            type = underlyingType;
        }

        // Direct type conversions
        if (type == typeof(string))
            return (T)(object)(string)result!;

        if (type == typeof(int))
            return (T)(object)(int)result;

        if (type == typeof(long))
            return (T)(object)(long)result;

        if (type == typeof(bool))
            return (T)(object)((int)result == 1);

        if (type == typeof(double))
            return (T)(object)(double)result;

        if (type == typeof(byte[]))
            return (T)(object)(byte[])result!;

        // Array types
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var redisArray = (RedisResult[])result!;
            var array = Array.CreateInstance(elementType, redisArray.Length);

            for (int i = 0; i < redisArray.Length; i++)
            {
                array.SetValue(ConvertRedisResult(redisArray[i]), i);
            }

            return (T)(object)array;
        }

        // Try to deserialize as JSON for complex types
        try
        {
            var json = (string?)result;
            if (!string.IsNullOrEmpty(json))
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
            }
        }
        catch
        {
            // Fall through to default conversion
        }

        // Default conversion attempt
        return (T)Convert.ChangeType(result, type)!;
    }

    private class PreparedRedisLuaScript : IPreparedLuaScript
    {
        private readonly RedisLuaScriptExecutor _executor;

        public PreparedRedisLuaScript(string script, RedisLuaScriptExecutor executor)
        {
            Script = script;
            _executor = executor;
        }

        public string Script { get; }

        public Task<LuaScriptResult> ExecuteAsync(
            string[]? keys = null,
            object[]? values = null,
            CancellationToken cancellationToken = default)
        {
            return _executor.ExecuteAsync(Script, keys, values, cancellationToken);
        }

        public Task<LuaScriptResult<T>> ExecuteAsync<T>(
            string[]? keys = null,
            object[]? values = null,
            CancellationToken cancellationToken = default)
        {
            return _executor.ExecuteAsync<T>(Script, keys, values, cancellationToken);
        }
    }
}
