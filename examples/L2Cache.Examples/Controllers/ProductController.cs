using L2Cache.Examples.Models;
using L2Cache.Examples.Services;
using Microsoft.AspNetCore.Mvc;

namespace L2Cache.Examples.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductController : ControllerBase
{
    private readonly ProductCacheService _productCache;
    private readonly ILogger<ProductController> _logger;

    public ProductController(ProductCacheService productCache, ILogger<ProductController> logger)
    {
        _productCache = productCache;
        _logger = logger;
    }

    /// <summary>
    /// Get product by ID.
    /// If not in cache, loads from simulated DB and caches it.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> Get(int id)
    {
        // Cache Aside pattern handled by the service
        var product = await _productCache.GetOrLoadAsync(id, TimeSpan.FromMinutes(10));
        
        if (product == null) return NotFound($"Product {id} not found");
        return Ok(product);
    }

    /// <summary>
    /// Update product.
    /// Updates DB and invalidates/updates cache.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductDto product)
    {
        if (id != product.Id) return BadRequest("ID mismatch");

        // Service handles DB update and cache invalidation/update
        await _productCache.UpdateAsync(id, product);
        
        return Ok(new { message = "Product updated", id });
    }

    /// <summary>
    /// Delete product cache.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCache(int id)
    {
        var removed = await _productCache.EvictAsync(id);
        return Ok(new { message = "Cache evicted", id, removed });
    }

    /// <summary>
    /// Batch get products.
    /// Efficiently fetches multiple items from cache/DB.
    /// </summary>
    [HttpPost("batch")]
    public async Task<ActionResult<Dictionary<int, ProductDto>>> BatchGet([FromBody] List<int> ids)
    {
        var products = await _productCache.BatchGetOrLoadAsync(ids, TimeSpan.FromMinutes(10));
        return Ok(products);
    }
    
    /// <summary>
    /// Reload product from source.
    /// Forces a refresh from DB.
    /// </summary>
    [HttpPost("{id}/reload")]
    public async Task<ActionResult<ProductDto>> Reload(int id)
    {
        var product = await _productCache.ReloadAsync(id, TimeSpan.FromMinutes(10));
        return Ok(product);
    }

}
