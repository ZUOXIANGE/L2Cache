using L2Cache.Serializers.Json;
using L2Cache.Abstractions.Serialization;
using L2Cache.Examples.Models;
using Microsoft.Extensions.Options;
using L2Cache.Configuration;

namespace L2Cache.Examples.Services;

/// <summary>
/// Cache service for Product entities.
/// Demonstrates inheriting from L2CacheService directly to reduce boilerplate.
/// </summary>
public class ProductCacheService : L2CacheService<int, ProductDto>
{
    private readonly ILogger<ProductCacheService> _logger;
    private ICacheSerializer _customSerializer;

    public ProductCacheService(
        IServiceProvider serviceProvider,
        IOptions<L2CacheOptions> options,
        ILogger<L2CacheService<int, ProductDto>> baseLogger,
        ILogger<ProductCacheService> logger)
        : base(serviceProvider, options, baseLogger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Initialize with default JSON serializer
        _customSerializer = new JsonCacheSerializer();
    }

    public void SetSerializer(ICacheSerializer serializer)
    {
        _customSerializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger.LogInformation("Switched serializer to: {SerializerName}", serializer.Name);
    }

    // Override to use our custom swappable serializer
    protected override ICacheSerializer GetCacheSerializer() => _customSerializer;

    public override string GetCacheName() => "products";

    // Build key like "products:1001"
    public override string BuildCacheKey(int id) => id.ToString();

    // Custom expiration policy
    protected override TimeSpan GetLocalCacheExpiry(TimeSpan? redisExpiry = null)
    {
        // Local cache expires faster than remote
        if (!redisExpiry.HasValue) return TimeSpan.FromMinutes(5);
        return TimeSpan.FromTicks(redisExpiry.Value.Ticks / 2);
    }

    // Simulate Database Query
    protected override async Task<ProductDto?> QueryDataAsync(int id)
    {
        // Simulate DB latency
        await Task.Delay(20);
        
        if (id <= 0) return null;

        return new ProductDto
        {
            Id = id,
            Name = $"Product {id}",
            Sku = $"SKU-{id:D6}",
            Description = $"Description for product {id}",
            Price = 99.99m + id,
            Stock = 100,
            CreateTime = DateTime.Now
        };
    }

    // Simulate Batch Database Query
    protected override async Task<Dictionary<int, ProductDto>> QueryDataListAsync(List<int> ids)
    {
        await Task.Delay(30);
        var result = new Dictionary<int, ProductDto>();
        foreach (var id in ids)
        {
            if (id <= 0) continue;
            result[id] = new ProductDto
            {
                Id = id,
                Name = $"Product {id}",
                Sku = $"SKU-{id:D6}",
                Description = $"Description for product {id}",
                Price = 99.99m + id,
                Stock = 100,
                CreateTime = DateTime.Now
            };
        }
        return result;
    }

    // Simulate Database Update
    protected override async Task UpdateDataAsync(int id, ProductDto data)
    {
        await Task.Delay(50);
        _logger.LogInformation("Updated product in DB: {Id}", id);
    }

    /// <summary>
    /// Example hook: Log when data is set to Redis
    /// </summary>
    protected override void OnRedisCacheSet(int key, ProductDto value, TimeSpan? expiry)
    {
        _logger.LogInformation("Product {Id} was set to Redis cache. Expiry: {Expiry}", key, expiry);
        
        // Example: We could trigger a message queue event here, or update a secondary index
        // _messageQueue.Publish(new ProductUpdatedEvent(key));
    }
}
