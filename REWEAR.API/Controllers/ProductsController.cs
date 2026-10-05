using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Controllers;

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
    /// Lấy danh sách sản phẩm kèm bộ lọc và tìm kiếm
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
    /// Lấy chi tiết một sản phẩm theo Id
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
    /// Tạo sản phẩm mới kèm ảnh
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
    /// Cập nhật thông tin sản phẩm
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
    /// Xóa sản phẩm (soft delete, đặt IsActive = false)
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
    /// Khôi phục sản phẩm đã bị xóa
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
    /// Cập nhật trạng thái đăng bán của sản phẩm
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
