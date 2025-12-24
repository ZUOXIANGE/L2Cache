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
        var history = _healthChecker.GetHealthHistory();
        return Ok(history);
    }

    /// <summary>
    /// 模拟系统故障（演示目的）
    /// <para>添加一个总是失败的健康检查项</para>
    /// </summary>
    [HttpPost("health/simulate-failure")]
    public IActionResult SimulateFailure()
    {
        _healthChecker.AddHealthCheck("simulated_failure", ct => 
            Task.FromResult(new HealthCheckItemResult(HealthStatus.Unhealthy, "Simulated critical failure for testing")));
            
        return Ok("Simulated failure injected. System status should now be Unhealthy.");
    }

    /// <summary>
    /// 清除模拟故障
    /// </summary>
    [HttpPost("health/clear-simulation")]
    public IActionResult ClearSimulation()
    {
        var removed = _healthChecker.RemoveHealthCheck("simulated_failure");
        return Ok(removed ? "Simulated failure removed." : "No simulated failure found.");
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
