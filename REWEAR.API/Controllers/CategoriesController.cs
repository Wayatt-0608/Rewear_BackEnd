using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>
    /// Lấy danh sách danh mục (lọc theo isActive)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool? isActive)
    {
        var categories = await _categoryService.GetAllAsync(isActive);
        return Ok(categories);
    }

    /// <summary>
    /// Lấy chi tiết một danh mục
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var category = await _categoryService.GetByIdAsync(id);
        
        if (category == null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        return Ok(category);
    }

    /// <summary>
    /// Tạo danh mục mới
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        try
        {
            var category = await _categoryService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
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
    /// Cập nhật thông tin danh mục
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateCategoryRequest request)
    {
        try
        {
            var category = await _categoryService.UpdateAsync(id, request);
            
            if (category == null)
            {
                return NotFound(new { message = "Không tìm thấy danh mục." });
            }

            return Ok(category);
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
    /// Xóa danh mục (soft delete)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _categoryService.DeleteAsync(id);
        
        if (!result)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        return Ok(new { message = "Xóa danh mục thành công." });
    }

    /// <summary>
    /// Khôi phục danh mục đã bị xóa
    /// </summary>
    [HttpPatch("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var category = await _categoryService.RestoreAsync(id);
        
        if (category == null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        return Ok(category);
    }
}
