using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

/// <summary>
/// Controller xử lý Brand (Quản lý thương hiệu).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BrandsController : ControllerBase
{
    private readonly IBrandService _brandService;

    public BrandsController(IBrandService brandService)
    {
        _brandService = brandService;
    }

    /// <summary>
    /// Lấy danh sách tất cả thương hiệu.
    /// GET /api/brands
    /// GET /api/brands?isActive=true
    /// GET /api/brands?isActive=false
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool? isActive)
    {
        var brands = await _brandService.GetAllAsync(isActive);
        return Ok(brands);
    }

    /// <summary>
    /// Lấy thông tin thương hiệu theo Id.
    /// GET /api/brands/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var brand = await _brandService.GetByIdAsync(id);
        
        if (brand == null)
        {
            return NotFound(new { message = "Không tìm thấy thương hiệu." });
        }

        return Ok(brand);
    }

    /// <summary>
    /// Tạo thương hiệu mới.
    /// POST /api/brands
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBrandRequest request)
    {
        try
        {
            var brand = await _brandService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = brand.Id }, brand);
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
    /// Cập nhật thông tin thương hiệu.
    /// PUT /api/brands/{id}
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateBrandRequest request)
    {
        try
        {
            var brand = await _brandService.UpdateAsync(id, request);
            
            if (brand == null)
            {
                return NotFound(new { message = "Không tìm thấy thương hiệu." });
            }

            return Ok(brand);
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
    /// Xóa thương hiệu (soft delete - đặt IsActive = false).
    /// DELETE /api/brands/{id}
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _brandService.DeleteAsync(id);
        
        if (!result)
        {
            return NotFound(new { message = "Không tìm thấy thương hiệu." });
        }

        return Ok(new { message = "Xóa thương hiệu thành công." });
    }

    /// <summary>
    /// Khôi phục thương hiệu đã bị soft delete (đặt IsActive = true).
    /// PATCH /api/brands/{id}/restore
    /// TODO: Thêm [Authorize] sau khi có Role system.
    /// </summary>
    [HttpPatch("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var brand = await _brandService.RestoreAsync(id);
        
        if (brand == null)
        {
            return NotFound(new { message = "Không tìm thấy thương hiệu." });
        }

        return Ok(brand);
    }
}
