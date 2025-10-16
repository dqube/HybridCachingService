namespace HybridCache.LuaScripting;

/// <summary>
/// Provides common Lua scripts for cache operations.
/// </summary>
public static class LuaScripts
{
    /// <summary>
    /// Atomically gets a value and extends its expiration time (sliding window).
    /// KEYS[1]: cache key
    /// ARGV[1]: expiration time in seconds
    /// Returns: the cached value or nil
    /// </summary>
    public const string GetAndExtendExpiration = @"
        local value = redis.call('GET', KEYS[1])
        if value then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return value
    ";

    /// <summary>
    /// Sets a value only if the key doesn't exist (add operation).
    /// KEYS[1]: cache key
    /// ARGV[1]: value
    /// ARGV[2]: expiration time in seconds
    /// Returns: 1 if set, 0 if key already exists
    /// </summary>
    public const string SetIfNotExists = @"
        local exists = redis.call('EXISTS', KEYS[1])
        if exists == 0 then
            redis.call('SETEX', KEYS[1], ARGV[2], ARGV[1])
            return 1
        end
        return 0
    ";

    /// <summary>
    /// Sets a value only if the key exists (update operation).
    /// KEYS[1]: cache key
    /// ARGV[1]: value
    /// ARGV[2]: expiration time in seconds
    /// Returns: 1 if updated, 0 if key doesn't exist
    /// </summary>
    public const string SetIfExists = @"
        local exists = redis.call('EXISTS', KEYS[1])
        if exists == 1 then
            redis.call('SETEX', KEYS[1], ARGV[2], ARGV[1])
            return 1
        end
        return 0
    ";

    /// <summary>
    /// Compare and swap operation - updates value only if current value matches expected.
    /// KEYS[1]: cache key
    /// ARGV[1]: expected value
    /// ARGV[2]: new value
    /// ARGV[3]: expiration time in seconds
    /// Returns: 1 if swapped, 0 if value didn't match
    /// </summary>
    public const string CompareAndSwap = @"
        local current = redis.call('GET', KEYS[1])
        if current == ARGV[1] then
            redis.call('SETEX', KEYS[1], ARGV[3], ARGV[2])
            return 1
        end
        return 0
    ";

    /// <summary>
    /// Atomically increments a counter and sets expiration if it's a new key.
    /// KEYS[1]: cache key
    /// ARGV[1]: increment amount
    /// ARGV[2]: expiration time in seconds
    /// Returns: the new value after increment
    /// </summary>
    public const string IncrementWithExpiration = @"
        local value = redis.call('INCRBY', KEYS[1], ARGV[1])
        local ttl = redis.call('TTL', KEYS[1])
        if ttl == -1 then
            redis.call('EXPIRE', KEYS[1], ARGV[2])
        end
        return value
    ";

    /// <summary>
    /// Gets multiple keys in a single atomic operation.
    /// KEYS[1..N]: cache keys to retrieve
    /// Returns: array of values (nil for missing keys)
    /// </summary>
    public const string GetMultiple = @"
        local results = {}
        for i, key in ipairs(KEYS) do
            results[i] = redis.call('GET', key)
        end
        return results
    ";

    /// <summary>
    /// Sets multiple keys with the same expiration time atomically.
    /// KEYS[1..N]: cache keys
    /// ARGV[1..N]: values (same count as KEYS)
    /// ARGV[N+1]: expiration time in seconds
    /// Returns: number of keys set
    /// </summary>
    public const string SetMultiple = @"
        local keyCount = #KEYS
        local expiration = ARGV[keyCount + 1]
        local count = 0

        for i = 1, keyCount do
            redis.call('SETEX', KEYS[i], expiration, ARGV[i])
            count = count + 1
        end

        return count
    ";

    /// <summary>
    /// Deletes keys matching a pattern (use with caution in production).
    /// KEYS[1]: pattern to match
    /// ARGV[1]: batch size (default 100)
    /// Returns: number of keys deleted
    /// </summary>
    public const string DeleteByPattern = @"
        local pattern = KEYS[1]
        local batchSize = tonumber(ARGV[1]) or 100
        local cursor = '0'
        local total = 0

        repeat
            local result = redis.call('SCAN', cursor, 'MATCH', pattern, 'COUNT', batchSize)
            cursor = result[1]
            local keys = result[2]

            if #keys > 0 then
                local deleted = redis.call('DEL', unpack(keys))
                total = total + deleted
            end
        until cursor == '0'

        return total
    ";

    /// <summary>
    /// Gets a value and refreshes multiple related keys' expiration.
    /// KEYS[1]: primary key to get
    /// KEYS[2..N]: related keys to refresh
    /// ARGV[1]: expiration time in seconds
    /// Returns: value of the primary key
    /// </summary>
    public const string GetAndRefreshRelated = @"
        local value = redis.call('GET', KEYS[1])
        if value then
            for i = 1, #KEYS do
                redis.call('EXPIRE', KEYS[i], ARGV[1])
            end
        end
        return value
    ";

    /// <summary>
    /// Implements a distributed lock with automatic expiration.
    /// KEYS[1]: lock key
    /// ARGV[1]: lock token (unique identifier)
    /// ARGV[2]: lock timeout in seconds
    /// Returns: 1 if lock acquired, 0 if already locked
    /// </summary>
    public const string AcquireLock = @"
        local exists = redis.call('EXISTS', KEYS[1])
        if exists == 0 then
            redis.call('SETEX', KEYS[1], ARGV[2], ARGV[1])
            return 1
        end
        return 0
    ";

    /// <summary>
    /// Releases a distributed lock only if the token matches.
    /// KEYS[1]: lock key
    /// ARGV[1]: lock token (unique identifier)
    /// Returns: 1 if lock released, 0 if token didn't match
    /// </summary>
    public const string ReleaseLock = @"
        local token = redis.call('GET', KEYS[1])
        if token == ARGV[1] then
            redis.call('DEL', KEYS[1])
            return 1
        end
        return 0
    ";

    /// <summary>
    /// Rate limiting script using sliding window.
    /// KEYS[1]: rate limit key
    /// ARGV[1]: max requests
    /// ARGV[2]: window size in seconds
    /// ARGV[3]: current timestamp
    /// Returns: 1 if allowed, 0 if rate limit exceeded
    /// </summary>
    public const string RateLimitSlidingWindow = @"
        local key = KEYS[1]
        local maxRequests = tonumber(ARGV[1])
        local windowSize = tonumber(ARGV[2])
        local currentTime = tonumber(ARGV[3])
        local windowStart = currentTime - windowSize

        -- Remove old entries
        redis.call('ZREMRANGEBYSCORE', key, 0, windowStart)

        -- Count current requests
        local currentCount = redis.call('ZCARD', key)

        if currentCount < maxRequests then
            -- Add new request
            redis.call('ZADD', key, currentTime, currentTime)
            redis.call('EXPIRE', key, windowSize)
            return 1
        end

        return 0
    ";

    /// <summary>
    /// Atomically appends to a list with a maximum size (FIFO queue).
    /// KEYS[1]: list key
    /// ARGV[1]: value to append
    /// ARGV[2]: maximum list size
    /// ARGV[3]: expiration time in seconds
    /// Returns: new list length
    /// </summary>
    public const string AppendToLimitedList = @"
        local key = KEYS[1]
        local value = ARGV[1]
        local maxSize = tonumber(ARGV[2])
        local expiration = tonumber(ARGV[3])

        redis.call('LPUSH', key, value)
        local len = redis.call('LLEN', key)

        if len > maxSize then
            redis.call('LTRIM', key, 0, maxSize - 1)
            len = maxSize
        end

        redis.call('EXPIRE', key, expiration)
        return len
    ";
}
