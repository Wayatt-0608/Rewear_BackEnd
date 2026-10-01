using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Controllers;

/// <summary>
/// Controller xử lý Product (Quản lý sản phẩm secondhand/upcycled).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    /// <summary>
    /// Lấy danh sách tất cả sản phẩm với filter/search.
    /// GET /api/products
    /// GET /api/products?isActive=true
    /// GET /api/products?brandId={id}
    /// GET /api/products?categoryId={id}
    /// GET /api/products?condition=Good
    /// GET /api/products?status=Available
    /// GET /api/products?minPrice=200000&maxPrice=500000
    /// GET /api/products?search=levis
    /// Combinations allowed.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ProductQueryParameters query)
    {
        try
        {
            var products = await _productService.GetAllAsync(query);
            return Ok(products);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy thông tin sản phẩm theo Id.
    /// GET /api/products/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var product = await _productService.GetByIdAsync(id);
        
        if (product == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        return Ok(product);
    }

    /// <summary>
    /// Tạo sản phẩm mới.
    /// POST /api/products
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        try
        {
            var product = await _productService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật thông tin sản phẩm.
    /// PUT /api/products/{id}
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateProductRequest request)
    {
        try
        {
            var product = await _productService.UpdateAsync(id, request);
            
            if (product == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            return Ok(product);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Xóa sản phẩm (soft delete - đặt IsActive = false).
    /// DELETE /api/products/{id}
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _productService.DeleteAsync(id);
        
        if (!result)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        return Ok(new { message = "Xóa sản phẩm thành công." });
    }

    /// <summary>
    /// Khôi phục sản phẩm đã bị soft delete (đặt IsActive = true).
    /// PATCH /api/products/{id}/restore
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpPatch("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        try
        {
            var product = await _productService.RestoreAsync(id);
            
            if (product == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            return Ok(product);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật trạng thái thương mại của sản phẩm.
    /// PATCH /api/products/{id}/status
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateProductStatusRequest request)
    {
        try
        {
            var product = await _productService.UpdateStatusAsync(id, request.Status);
            
            if (product == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            return Ok(product);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
