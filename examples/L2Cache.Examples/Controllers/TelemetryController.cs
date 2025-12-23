using L2Cache.Abstractions.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace L2Cache.Examples.Controllers;

[ApiController]
[Route("api/telemetry")]
[Tags("Telemetry & Health")]
public class TelemetryController : ControllerBase
{
    private readonly IHealthChecker _healthChecker;
    private readonly ITelemetryProvider _telemetry;

    public TelemetryController(IHealthChecker healthChecker, ITelemetryProvider telemetry)
    {
        _healthChecker = healthChecker;
        _telemetry = telemetry;
    }

    /// <summary>
    /// 获取当前健康状态
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        // 主动触发一次检查
        var result = await _healthChecker.CheckHealthAsync();
        
        return result.Status == HealthStatus.Healthy 
            ? Ok(result) 
            : StatusCode(503, result);
    }

    /// <summary>
    /// 获取历史健康状态
    /// </summary>
    [HttpGet("health/history")]
    public IActionResult GetHealthHistory()
    {
        // 注意：这里假设 IHealthChecker 有公开历史记录的方法，如果没有，可能需要依赖注入具体的实现类或扩展接口
        // DefaultHealthChecker 确实有 _healthHistory 但没有公开接口方法。
        // 我们先只返回当前状态。
        // 实际上，DefaultHealthChecker 没有公开获取历史的方法，我们可能需要扩展它或者只是展示 CheckHealthAsync 的结果。
        // 这里暂时先只做 CheckHealthAsync。
        return Ok("History API not implemented in interface");
    }

    /// <summary>
    /// 获取当前缓存指标
    /// </summary>
    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        var metrics = _telemetry.GetAllCacheStatistics();
        return Ok(metrics);
    }
}
