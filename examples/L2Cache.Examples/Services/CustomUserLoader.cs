using L2Cache.Abstractions.Policies;
using L2Cache.Examples.Models;

namespace L2Cache.Examples.Services;

/// <summary>
/// 用户缓存回源加载器：演示通过 <see cref="LoaderBase{TKey,TValue}"/> 只实现单条查询，
/// 批量加载由基类默认逐 Key 实现（支持真正批量回源时可覆写 LoadManyAsync）。
/// </summary>
public class CustomUserLoader(ILogger<CustomUserLoader> logger) : LoaderBase<int, UserDto>
{
    /// <summary>模拟数据库单条查询。</summary>
    public override async Task<UserDto?> LoadAsync(int key, CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Fetching user {Key} from simulated database...", key);
        }

        // 模拟数据库查询延迟
        await Task.Delay(50, cancellationToken);

        if (key <= 0)
        {
            return null;
        }

        return new UserDto
        {
            Id = key,
            Username = $"User_{key}",
            Email = $"user{key}@example.com",
            CreatedAt = DateTime.UtcNow
        };
    }
}
