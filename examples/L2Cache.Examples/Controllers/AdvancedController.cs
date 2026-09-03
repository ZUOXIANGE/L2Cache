using L2Cache.Abstractions.Telemetry;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace L2Cache.Examples.Controllers;

/// <summary>
/// 高级场景演示：缓存统计与运行状态观测。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AdvancedController : ControllerBase
{
    private readonly ITelemetryProvider _telemetry;
    private readonly IConnectionMultiplexer? _redis;

    public AdvancedController(ITelemetryProvider telemetry, IConnectionMultiplexer? redis = null)
    {
        _telemetry = telemetry;
        _redis = redis;
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
