using L2Cache.Examples.Models;
using L2Cache.Examples.Services;
using Microsoft.AspNetCore.Mvc;

namespace L2Cache.Examples.Controllers;

[ApiController]
[Route("api/custom-inheritance")]
public class CustomInheritanceController : ControllerBase
{
    private readonly CustomUserCacheService _userCache;
    private readonly ILogger<CustomInheritanceController> _logger;

    public CustomInheritanceController(
        CustomUserCacheService userCache,
        ILogger<CustomInheritanceController> logger)
    {
        _userCache = userCache;
        _logger = logger;
    }

    /// <summary>
    /// 获取用户（通过自定义缓存服务）
    /// </summary>
    [HttpGet("users/{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userCache.GetAsync(id);
        if (user == null)
            return NotFound();

        return Ok(user);
    }

    /// <summary>
    /// 更新用户（演示 PutAsync）
    /// </summary>
    [HttpPut("users/{id}")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, [FromBody] UserDto user)
    {
        if (id != user.Id)
            return BadRequest();

        // 写入缓存
        await _userCache.PutAsync(id, user, TimeSpan.FromMinutes(10));
        
        return Ok(user);
    }

    /// <summary>
    /// 删除用户（演示 EvictAsync）
    /// </summary>
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _userCache.EvictAsync(id);
        return NoContent();
    }
}
