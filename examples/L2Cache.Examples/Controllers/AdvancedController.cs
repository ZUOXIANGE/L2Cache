using L2Cache.Examples.Services;
using L2Cache.Serializers.Json;
using L2Cache.Serializers.MemoryPack;
using Microsoft.AspNetCore.Mvc;
using L2Cache.Abstractions.Telemetry;
using StackExchange.Redis;

namespace L2Cache.Examples.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdvancedController : ControllerBase
{
    private readonly ProductCacheService _productCache;
    private readonly ITelemetryProvider _telemetry;
    private readonly ILogger<AdvancedController> _logger;
    private readonly IConnectionMultiplexer? _redis;

    public AdvancedController(
        ProductCacheService productCache, 
        ITelemetryProvider telemetry,
        ILogger<AdvancedController> logger,
        IConnectionMultiplexer? redis = null)
    {
        _productCache = productCache;
        _telemetry = telemetry;
        _logger = logger;
        _redis = redis;
    }

    /// <summary>
    /// Switch serializer at runtime.
    /// Demonstrates flexibility of serialization strategy.
    /// </summary>
    [HttpPost("serializer/{type}")]
    public IActionResult SwitchSerializer(string type)
    {
        switch (type.ToLower())
        {
            case "json":
                _productCache.SetSerializer(new JsonCacheSerializer());
                return Ok("Switched to JSON serializer");
            case "memorypack":
                // In a real app, you might want to clear cache when switching serializers
                // as binary formats are often incompatible.
                _productCache.SetSerializer(new MemoryPackCacheSerializer());
                return Ok("Switched to MemoryPack serializer");
            default:
                return BadRequest("Supported types: json, memorypack");
        }
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        // Simple health/stats check
        var isRedisConnected = _redis?.IsConnected ?? false;
        var stats = _telemetry.GetCacheStatistics("products");
        
        return Ok(new 
        { 
            RedisConnected = isRedisConnected,
            Timestamp = DateTime.UtcNow,
            CacheStats = stats
        });
    }
}
