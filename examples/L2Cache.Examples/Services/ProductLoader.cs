using L2Cache.Abstractions.Policies;
using L2Cache.Examples.Models;

namespace L2Cache.Examples.Services;

/// <summary>
/// 商品缓存回源加载器：演示通过 <see cref="ILoader{TKey,TValue}"/> 对接数据源（数据库 / 远程服务）。
/// <para>
/// 通过 <c>AddCache(...).WithLoader&lt;ProductLoader&gt;()</c> 注册；Loader 从 DI 解析，
/// 可注入 Scoped 依赖（如 DbContext 仓储）。
/// </para>
/// </summary>
public class ProductLoader : ILoader<int, ProductDto>
{
    /// <summary>模拟数据库单条查询。</summary>
    public async Task<ProductDto?> LoadAsync(int key, CancellationToken cancellationToken = default)
    {
        // 模拟 DB 延迟
        await Task.Delay(20, cancellationToken);

        return key <= 0 ? null : BuildProduct(key);
    }

    /// <summary>模拟批量查询（真实场景可翻译为一条 IN 查询）。</summary>
    public async Task<Dictionary<int, ProductDto>> LoadManyAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken = default)
    {
        await Task.Delay(30, cancellationToken);

        var result = new Dictionary<int, ProductDto>(keys.Count);
        foreach (var id in keys)
        {
            if (id > 0)
            {
                result[id] = BuildProduct(id);
            }
        }

        return result;
    }

    private static ProductDto BuildProduct(int id) => new()
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
