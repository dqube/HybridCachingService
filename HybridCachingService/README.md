# HybridCaching Service - Demo API

A comprehensive demo API showcasing all features of the HybridCache library including L1/L2 caching, Lua scripts, cache notifications, and Redis cluster support.

## Quick Start

### 1. Prerequisites

- .NET 8.0 SDK
- Redis server running locally or connection string in appsettings.json

### 2. Run Redis (Docker)

```bash
docker run -d --name redis -p 6379:6379 redis:7-alpine
```

### 3. Run the API

```bash
dotnet run
```

### 4. Access Swagger UI

Navigate to: `https://localhost:5001/swagger`

## API Endpoints

### Basic Cache Operations (`/api/cache`)

#### User Management
- `GET /api/cache/user/{id}` - Get user from cache
- `PUT /api/cache/user/{id}` - Cache a user with absolute expiration
- `POST /api/cache/user/{id}/get-or-create` - Cache-aside pattern
- `DELETE /api/cache/user/{id}` - Remove user from cache
- `DELETE /api/cache/user/{id}/local` - Remove from L1 only

#### Product Management
- `PUT /api/cache/product/{id}` - Cache product with sliding expiration
- `GET /api/cache/product/{id}` - Get product from cache
- `POST /api/cache/products/batch` - Batch cache multiple products

#### Session Management
- `POST /api/cache/session` - Create session with different L1/L2 expiration
- `GET /api/cache/session/{sessionId}` - Get session

#### Advanced Caching
- `POST /api/cache/cache/l1-only` - Store in L1 cache only (fast, not distributed)
- `POST /api/cache/cache/l2-only` - Store in L2 cache only (distributed)

### Lua Scripts (`/api/luascripts`)

#### Atomic Counter
- `POST /api/luascripts/counter/{key}/increment` - Increment counter with auto-expiration
- `GET /api/luascripts/counter/{key}` - Get counter value

#### Rate Limiting
- `POST /api/luascripts/ratelimit/{userId}/check` - Check rate limit (sliding window)

#### Distributed Locking
- `POST /api/luascripts/lock/{resourceKey}/acquire` - Acquire distributed lock
- `POST /api/luascripts/lock/{resourceKey}/release` - Release lock with token validation

#### Compare and Swap
- `POST /api/luascripts/cas/{key}` - Atomic compare-and-swap
- `POST /api/luascripts/setnx/{key}` - Set if not exists

#### Batch Operations
- `POST /api/luascripts/mget` - Get multiple keys atomically
- `POST /api/luascripts/user/{userId}/activity` - Log activity (limited-size FIFO queue)

#### Custom Scripts
- `POST /api/luascripts/execute` - Execute custom Lua script

### Advanced Features (`/api/advancedfeatures`)

#### Redis Cluster Support
- `GET /api/advancedfeatures/cluster/hash-slot/{key}` - Calculate hash slot for key
- `POST /api/advancedfeatures/cluster/validate-keys` - Validate keys for cluster mode
- `POST /api/advancedfeatures/cluster/wrap-keys` - Wrap keys with hash tag
- `POST /api/advancedfeatures/cluster/user/{userId}/batch-set` - Cluster-safe batch operation

#### Cache Notifications
- `POST /api/advancedfeatures/notifications/demo/{key}` - Send notification to all instances
- `POST /api/advancedfeatures/notifications/multi-instance-test` - Test multi-instance sync

#### Cache Patterns
- `GET /api/advancedfeatures/patterns/cache-aside-with-lock/{userId}` - Cache-aside with stampede prevention
- `PUT /api/advancedfeatures/patterns/write-through/{userId}` - Write-through cache pattern
- `POST /api/advancedfeatures/patterns/refresh-ahead/{key}` - Refresh-ahead pattern

#### Monitoring
- `GET /api/advancedfeatures/stats` - Get cache statistics
- `GET /api/advancedfeatures/health` - Health check

## Usage Examples

### Example 1: Basic Caching

```bash
# Create a user
curl -X PUT "https://localhost:5001/api/cache/user/123" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "John Doe",
    "email": "john@example.com"
  }'

# Get user from cache
curl -X GET "https://localhost:5001/api/cache/user/123"

# Remove user
curl -X DELETE "https://localhost:5001/api/cache/user/123"
```

### Example 2: Rate Limiting

```bash
# Check rate limit (10 requests per 60 seconds)
curl -X POST "https://localhost:5001/api/luascripts/ratelimit/user123/check?maxRequests=10&windowSeconds=60"

# Response:
{
  "isAllowed": true,
  "limit": 10,
  "window": "00:01:00",
  "resetAt": "2025-01-15T10:31:00Z"
}
```

### Example 3: Distributed Locking

```bash
# Acquire lock
curl -X POST "https://localhost:5001/api/luascripts/lock/critical-resource/acquire?timeoutSeconds=30"

# Response:
{
  "message": "Lock acquired",
  "resource": "critical-resource",
  "lockToken": "abc123...",
  "expiresIn": 30
}

# Release lock
curl -X POST "https://localhost:5001/api/luascripts/lock/critical-resource/release?lockToken=abc123..."
```

### Example 4: Cluster Hash Slots

```bash
# Calculate hash slot
curl -X GET "https://localhost:5001/api/advancedfeatures/cluster/hash-slot/user:123"

# Response:
{
  "key": "user:123",
  "hashSlot": 5461,
  "hashTag": null,
  "range": "0-16383"
}

# Validate multiple keys
curl -X POST "https://localhost:5001/api/advancedfeatures/cluster/validate-keys" \
  -H "Content-Type: application/json" \
  -d '["user:123", "user:456"]'

# Response:
{
  "isValid": false,
  "message": "Keys map to different hash slots...",
  "keys": [...]
}
```

### Example 5: Cluster-Safe Batch Operations

```bash
# Batch set with hash tags (cluster-safe)
curl -X POST "https://localhost:5001/api/advancedfeatures/cluster/user/123/batch-set" \
  -H "Content-Type: application/json" \
  -d '{
    "profile": "...",
    "settings": "...",
    "preferences": "..."
  }'

# All keys use hash tag {user:123} → same slot → works in cluster mode
```

### Example 6: Cache Notifications

```bash
# Instance 1: Set value (notification sent)
curl -X POST "https://localhost:5001/api/advancedfeatures/notifications/demo/testkey" \
  -H "Content-Type: application/json" \
  -d '"test-value"'

# Instance 2: L1 cache automatically invalidated
# Next read will fetch from L2
```

### Example 7: Atomic Counter

```bash
# Increment counter
curl -X POST "https://localhost:5001/api/luascripts/counter/page-views/increment?amount=1"

# Response:
{
  "counter": "page-views",
  "value": 42,
  "operation": "increment"
}

# Get counter
curl -X GET "https://localhost:5001/api/luascripts/counter/page-views"
```

### Example 8: Activity Logging (FIFO)

```bash
# Log user activity (max 50 items)
curl -X POST "https://localhost:5001/api/luascripts/user/123/activity?maxActivities=50" \
  -H "Content-Type: application/json" \
  -d '"User logged in"'

# Response:
{
  "message": "Activity logged",
  "userId": "123",
  "listSize": 15
}
```

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "HybridCache": "Information"
    }
  }
}
```

### Redis Connection Strings

- **Single instance**: `localhost:6379`
- **With password**: `localhost:6379,password=yourpassword`
- **Cluster**: `node1:6379,node2:6379,node3:6379`

## Features Demonstrated

### 1. Two-Tier Caching
- ✅ L1 (in-memory) cache
- ✅ L2 (distributed Redis) cache
- ✅ Automatic fallback
- ✅ Different expiration per tier

### 2. Lua Script Operations
- ✅ Atomic counters
- ✅ Rate limiting (sliding window)
- ✅ Distributed locking with token validation
- ✅ Compare-and-swap (CAS)
- ✅ Conditional sets (SETNX, SETEX)
- ✅ Batch operations (MGET, MSET)
- ✅ FIFO queues with size limits
- ✅ Custom script execution

### 3. Cache Notifications
- ✅ Automatic L1 invalidation across instances
- ✅ Redis pub/sub for real-time sync
- ✅ Pattern-based filtering
- ✅ Self-notification prevention

### 4. Redis Cluster Support
- ✅ Hash slot calculation (CRC16)
- ✅ Hash tag validation
- ✅ Multi-key operation safety
- ✅ Cluster-aware Lua scripts

### 5. Cache Patterns
- ✅ Cache-aside with stampede prevention
- ✅ Write-through caching
- ✅ Refresh-ahead
- ✅ Read-through (GetOrCreate)

## Testing

### Unit Testing Example

```csharp
[Fact]
public async Task RateLimit_ShouldWork()
{
    // Arrange
    var userId = "test-user";

    // Act
    var results = new List<bool>();
    for (int i = 0; i < 15; i++)
    {
        var response = await client.PostAsync(
            $"/api/luascripts/ratelimit/{userId}/check?maxRequests=10&windowSeconds=60",
            null
        );
        var result = await response.Content.ReadFromJsonAsync<RateLimitResult>();
        results.Add(result.IsAllowed);
    }

    // Assert
    Assert.Equal(10, results.Count(x => x == true));  // First 10 allowed
    Assert.Equal(5, results.Count(x => x == false)); // Next 5 blocked
}
```

### Load Testing with k6

```javascript
import http from 'k6/http';
import { check } from 'k6';

export default function () {
  // Test cache read performance
  let res = http.get('https://localhost:5001/api/cache/user/123');
  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 10ms': (r) => r.timings.duration < 10, // L1 hit!
  });
}
```

## Monitoring

### Health Check

```bash
curl https://localhost:5001/api/advancedfeatures/health
```

### Statistics

```bash
curl https://localhost:5001/api/advancedfeatures/stats
```

## Architecture

```
┌─────────────────────────────────────────────┐
│         HybridCaching Service API            │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐ │
│  │  Cache   │  │   Lua    │  │ Advanced │ │
│  │Controller│  │ Scripts  │  │ Features │ │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘ │
└───────┼─────────────┼─────────────┼────────┘
        │             │             │
        └─────────────┴─────────────┘
                      │
           ┌──────────▼──────────┐
           │   HybridCache       │
           │   Library           │
           └──────────┬──────────┘
                      │
        ┌─────────────┼─────────────┐
        │             │             │
   ┌────▼────┐   ┌────▼────┐  ┌────▼────┐
   │L1 Cache │   │L2 Cache │  │Pub/Sub  │
   │(Memory) │   │(Redis)  │  │(Redis)  │
   └─────────┘   └─────────┘  └─────────┘
```

## Performance

### L1 Cache Hit
- **Latency**: ~1-10 microseconds
- **Throughput**: >1M ops/sec

### L2 Cache Hit
- **Latency**: ~1-10 milliseconds
- **Throughput**: ~100K ops/sec

### Lua Script Execution
- **Latency**: ~1-5 milliseconds
- **Advantage**: Atomic + single network round-trip

## Troubleshooting

### Redis Connection Failed

**Error**: "Unable to connect to Redis"

**Solution**:
```bash
# Check Redis is running
docker ps | grep redis

# Check connection
redis-cli ping
```

### Lua Scripts Not Working

**Error**: "Lua script executor not available"

**Solution**: Ensure Redis connection is established and `AddHybridCacheWithRedis` is called in Program.cs

### Notifications Not Received

**Error**: L1 cache not invalidating across instances

**Solution**: Ensure `AddCacheNotifications` is called and Redis pub/sub is working:
```bash
redis-cli
> SUBSCRIBE hybridcache:notifications
```

## Documentation

- [HybridCache Library](../HybridCache/README.md)
- [Lua Scripts Guide](../HybridCache/LuaScripting/README_LUA.md)
- [Notifications Guide](../HybridCache/Notifications/README_NOTIFICATIONS.md)
- [Cluster Support](../HybridCache/Clustering/README_CLUSTER.md)

## License

MIT License
