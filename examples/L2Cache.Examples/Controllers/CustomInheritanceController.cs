using L2Cache.Abstractions;
using L2Cache.Examples.Models;
using Microsoft.AspNetCore.Mvc;

namespace L2Cache.Examples.Controllers;

/// <summary>
/// 演示"users"区域的自定义 Loader（CustomUserLoader）回源能力。
/// <para>
/// 控制器只依赖 <see cref="ICacheClient{TKey,TValue}"/> 门面，
/// 数据源逻辑全部封装在 Loader 中（从 DI 解析，支持 Scoped 依赖）。
/// </para>
/// </summary>
[ApiController]
[Route("api/custom-inheritance")]
public class CustomInheritanceController : ControllerBase
{
    private readonly ICacheClient<int, UserDto> _userCache;

    public CustomInheritanceController(ICacheClient<int, UserDto> userCache)
    {
        _userCache = userCache;
    }

    /// <summary>
    /// 获取用户（未命中时由 CustomUserLoader 回源）
    /// </summary>
    [HttpGet("users/{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userCache.GetOrLoadAsync(id, TimeSpan.FromMinutes(10));
        if (user == null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    /// <summary>
    /// 更新用户（演示 PutAsync）
    /// </summary>
    [HttpPut("users/{id}")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, [FromBody] UserDto user)
    {
        if (id != user.Id)
        {
            return BadRequest();
        }

        // 写入缓存
        await _userCache.PutAsync(id, user, TimeSpan.FromMinutes(10));

        return Ok(user);
    }

    /// <summary>
    /// 删除用户缓存（演示 EvictAsync）
    /// </summary>
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _userCache.EvictAsync(id);
        return NoContent();
    }
}
