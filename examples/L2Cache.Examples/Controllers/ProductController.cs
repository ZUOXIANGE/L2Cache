using L2Cache.Abstractions;
using L2Cache.Examples.Models;
using Microsoft.AspNetCore.Mvc;

namespace L2Cache.Examples.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductController : ControllerBase
{
    // ICacheClient<int, ProductDto> resolves the "products" region
    // (AddCache<int, ProductDto>("products").WithLoader<ProductLoader>())
    private readonly ICacheClient<int, ProductDto> _cache;

    public ProductController(ICacheClient<int, ProductDto> cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Get product by ID.
    /// If not in cache, loads from the ProductLoader (simulated DB) and caches it.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> Get(int id)
    {
        // Cache-Aside 回源由注册的 Loader 完成
        var product = await _cache.GetOrLoadAsync(id, TimeSpan.FromMinutes(10));

        if (product == null) return NotFound($"Product {id} not found");
        return Ok(product);
    }

    /// <summary>
    /// Update product.
    /// 模拟业务侧先更新数据库，再直接回写缓存（Write-Through）。
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductDto product)
    {
        if (id != product.Id) return BadRequest("ID mismatch");

        // 模拟 DB 更新
        await Task.Delay(50);

        // 覆盖写缓存（L1 + L2 同步更新，并广播失效消息）
        await _cache.PutAsync(id, product, TimeSpan.FromMinutes(10));

        return Ok(new { message = "Product updated", id });
    }

    /// <summary>
    /// Delete product cache.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCache(int id)
    {
        var removed = await _cache.EvictAsync(id);
        return Ok(new { message = "Cache evicted", id, removed });
    }

    /// <summary>
    /// Batch get products.
    /// Efficiently fetches multiple items from cache/DB.
    /// </summary>
    [HttpPost("batch")]
    public async Task<ActionResult<Dictionary<int, ProductDto>>> BatchGet([FromBody] List<int> ids)
    {
        var products = await _cache.BatchGetOrLoadAsync(ids, TimeSpan.FromMinutes(10));
        return Ok(products);
    }

    /// <summary>
    /// Reload product from source.
    /// Forces a refresh from DB.
    /// </summary>
    [HttpPost("{id}/reload")]
    public async Task<ActionResult<ProductDto>> Reload(int id)
    {
        var product = await _cache.ReloadAsync(id, TimeSpan.FromMinutes(10));
        if (product == null) return NotFound($"Product {id} not found");
        return Ok(product);
    }
}
