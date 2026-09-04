using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace L2Cache.Examples.Controllers;

/// <summary>
/// 高级场景演示：L2（Redis）运行状态观测。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AdvancedController : ControllerBase
{
    private readonly IConnectionMultiplexer? _redis;

    public AdvancedController(IConnectionMultiplexer? redis = null)
    {
        _redis = redis;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            RedisConnected = _redis?.IsConnected ?? false,
            Timestamp = DateTime.UtcNow
        });
    }
}
