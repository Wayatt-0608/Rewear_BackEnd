using Microsoft.AspNetCore.Mvc;
using REWEAR.API.Models;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ICloudinaryService _cloudinaryService;

    // Image validation constants
    private const int MinImages = 1;
    private const int MaxImages = 5;
    private const string ImageFolder = "products";

    public ProductsController(IProductService productService, ICloudinaryService cloudinaryService)
    {
        _productService = productService;
        _cloudinaryService = cloudinaryService;
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
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create([FromForm] CreateProductFormRequest request)
    {
        // ===== IMAGE VALIDATION =====
        if (request.Images == null || request.Images.Count == 0)
        {
            return BadRequest(new { message = "Cần ít nhất 1 hình ảnh sản phẩm." });
        }

        if (request.Images.Count > MaxImages)
        {
            return BadRequest(new { message = $"Tối đa {MaxImages} hình ảnh cho mỗi sản phẩm." });
        }

        // Validate each image is not empty
        foreach (var image in request.Images)
        {
            if (image == null || image.Length == 0)
            {
                return BadRequest(new { message = "Có hình ảnh không hợp lệ hoặc trống." });
            }
        }

        // ===== UPLOAD IMAGES TO CLOUDINARY =====
        var uploadedImages = new List<(string Url, string PublicId)>();

        try
        {
            for (int i = 0; i < request.Images.Count; i++)
            {
                var image = request.Images[i];
                using var stream = image.OpenReadStream();

                var publicId = $"product_{Guid.NewGuid():N}";
                var uploadRequest = new ImageUploadRequest
                {
                    FileStream = stream,
                    FileName = image.FileName,
                    Folder = ImageFolder,
                    PublicId = publicId
                };

                var result = await _cloudinaryService.UploadImageAsync(uploadRequest);

                if (!result.Success)
                {
                    // Rollback: delete all previously uploaded images
                    await RollbackUploadedImages(uploadedImages);
                    return BadRequest(new { message = $"Upload hình ảnh thất bại: {result.ErrorMessage}" });
                }

                uploadedImages.Add((result.Url!, result.PublicId!));
            }
        }
        catch (Exception ex)
        {
            // Rollback on unexpected error
            await RollbackUploadedImages(uploadedImages);
            return BadRequest(new { message = $"Lỗi khi upload hình ảnh: {ex.Message}" });
        }

        // ===== CREATE PRODUCT =====
        try
        {
            var createRequest = new CreateProductRequest
            {
                Title = request.Title,
                Description = request.Description,
                BrandId = request.BrandId,
                CategoryId = request.CategoryId,
                Price = request.Price,
                Condition = request.Condition,
                Size = request.Size,
                Color = request.Color,
                ImageUrls = uploadedImages.Select(x => x.Url).ToList()
            };

            var product = await _productService.CreateAsync(createRequest);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }
        catch (ArgumentException ex)
        {
            // Rollback: delete all uploaded images when product creation fails
            await RollbackUploadedImages(uploadedImages);
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Rollback: delete all uploaded images when product creation fails
            await RollbackUploadedImages(uploadedImages);
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            // Rollback on unexpected error during product creation
            await RollbackUploadedImages(uploadedImages);
            return BadRequest(new { message = $"Lỗi khi tạo sản phẩm: {ex.Message}" });
        }
    }

    /// <summary>
    /// Helper method to rollback uploaded images on failure.
    /// </summary>
    private async Task RollbackUploadedImages(List<(string Url, string PublicId)> uploadedImages)
    {
        foreach (var image in uploadedImages)
        {
            try
            {
                await _cloudinaryService.DeleteImageAsync(image.PublicId);
            }
            catch
            {
                // Log warning but don't fail - cleanup failure shouldn't mask original error
                // In production, use proper logging: _logger.LogWarning("Failed to rollback image {PublicId}", image.PublicId);
            }
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
