using L2Cache.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace L2Cache.Examples.Controllers;

/// <summary>
/// Demonstrates basic usage of L2Cache directly via ICacheClient interface.
/// No custom service implementation is required for simple key-value storage.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("Basics")]
public class BasicsController : ControllerBase
{
    // Injecting ICacheClient<string, string> resolves the client registered
    // via AddL2Cache(...).AddCache<string, string>("basics")
    private readonly ICacheClient<string, string> _cache;

    public BasicsController(ICacheClient<string, string> cache)
    {
        _cache = cache;
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        var value = await _cache.GetAsync(key);
        if (value == null) return NotFound(new { key, message = "Key not found in cache" });
        return Ok(new { key, value, source = "Cache" });
    }

    [HttpPost("{key}")]
    public async Task<IActionResult> Set(string key, [FromBody] string value, [FromQuery] int ttlSeconds = 60)
    {
        await _cache.PutAsync(key, value, TimeSpan.FromSeconds(ttlSeconds));
        return Ok(new { message = "Cached successfully", key, value, ttlSeconds });
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Remove(string key)
    {
        var removed = await _cache.EvictAsync(key);
        return Ok(new { message = "Removed", key, removed });
    }

    [HttpGet("{key}/exists")]
    public async Task<IActionResult> Exists(string key)
    {
        var exists = await _cache.ExistsAsync(key);
        return Ok(new { key, exists });
    }
}
