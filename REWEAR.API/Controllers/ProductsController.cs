using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly IPaymentService _paymentService;
    private readonly ILogger<ProductsController> _logger;

    // Image validation constants
    private const int MinImages = 1;
    private const int MaxImages = 5;
    private const string ImageFolder = "products";

    public ProductsController(
        IProductService productService,
        ICloudinaryService cloudinaryService,
        IPaymentService paymentService,
        ILogger<ProductsController> logger)
    {
        _productService = productService;
        _cloudinaryService = cloudinaryService;
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách sản phẩm kèm bộ lọc và tìm kiếm
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ProductQueryParameters query)
    {
        try
        {
            // On-demand expiration: đóng phiên thanh toán hết hạn ngay khi user
            // mở trang danh sách sản phẩm. Không phụ thuộc BackgroundService
            // (Render free tier có thể không chạy background).
            // Bỏ qua lỗi để không ảnh trải nghiệm user.
            _ = SafeExpireStaleAsync();

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
        // On-demand expiration tương tự GetAll.
        _ = SafeExpireStaleAsync();

        var product = await _productService.GetByIdAsync(id);

        if (product == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        return Ok(product);
    }

    /// <summary>
    /// Fire-and-forget cleanup phiên thanh toán hết hạn. Không chờ kết quả,
    /// không ném lỗi - đây là tối ưu phụ, không ảnh hưởng response chính.
    /// </summary>
    private async Task SafeExpireStaleAsync()
    {
        try
        {
            await _paymentService.ExpireStalePaymentsAsync(50);
        }
        catch
        {
            // Nuốt lỗi: cleanup fail không được làm sập API.
        }
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
    /// Cập nhật thông tin sản phẩm với upload ảnh
    /// </summary>
    [HttpPut("{id}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Update(string id, [FromForm] UpdateProductFormRequest request)
    {
        // ===== VALIDATE PRODUCT EXISTS =====
        var existingProduct = await _productService.GetByIdAsync(id);
        if (existingProduct == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        // ===== IMAGE VALIDATION =====
        List<(string Url, string PublicId)> uploadedImages = new();
        List<string> newImageUrls = new();
        List<string>? keepImageUrls = request.KeepImageUrls;
        bool clearAllImages = request.ClearAllImages;

        // Validate new images if provided
        if (request.Images != null && request.Images.Count > 0)
        {
            // Validate image count
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

            // ===== UPLOAD NEW IMAGES =====
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
                    newImageUrls.Add(result.Url!);
                }
            }
            catch (Exception ex)
            {
                // Rollback on unexpected error
                await RollbackUploadedImages(uploadedImages);
                return BadRequest(new { message = $"Lỗi khi upload hình ảnh: {ex.Message}" });
            }
        }

        // ===== UPDATE PRODUCT =====
        try
        {
            // Build UpdateProductRequest from form data
            var updateRequest = new UpdateProductRequest
            {
                Title = request.Title,
                Description = request.Description,
                BrandId = request.BrandId,
                CategoryId = request.CategoryId,
                Price = request.Price,
                Condition = request.Condition,
                Size = request.Size,
                Color = request.Color,
                StockQuantity = request.StockQuantity
            };

            // Call service with image management
            var updatedProduct = await _productService.UpdateWithImagesAsync(
                id,
                updateRequest,
                newImageUrls,
                keepImageUrls,
                clearAllImages);

            if (updatedProduct == null)
            {
                // Rollback uploaded images
                await RollbackUploadedImages(uploadedImages);
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            // ===== CLEANUP OLD IMAGES (after successful update) =====
            // Get list of old images that are no longer used
            var unusedImages = await _productService.GetUnusedImagesToDeleteAsync(
                id,
                updatedProduct.ImageUrls);

            // Delete unused images - await to ensure completion before response
            // Errors are logged but don't rollback the successful update
            await DeleteUnusedImagesSafeAsync(unusedImages);

            return Ok(updatedProduct);
        }
        catch (ArgumentException ex)
        {
            // Rollback: delete all uploaded new images when product update fails
            await RollbackUploadedImages(uploadedImages);
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Rollback: delete all uploaded new images when product update fails
            await RollbackUploadedImages(uploadedImages);
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            // Rollback on unexpected error
            await RollbackUploadedImages(uploadedImages);
            return BadRequest(new { message = $"Lỗi khi cập nhật sản phẩm: {ex.Message}" });
        }
    }

    /// <summary>
    /// Helper method to safely delete unused images after successful update.
    /// </summary>
    private async Task DeleteUnusedImagesSafeAsync(List<string> unusedImageUrls)
    {
        foreach (var imageUrl in unusedImageUrls)
        {
            try
            {
                // Extract public ID from URL for deletion
                var publicId = ExtractPublicIdFromUrl(imageUrl);
                if (!string.IsNullOrEmpty(publicId))
                {
                    await _cloudinaryService.DeleteImageAsync(publicId);
                    _logger.LogInformation("Deleted unused image: {PublicId}", publicId);
                }
            }
            catch (Exception ex)
            {
                // Log warning but don't fail - cleanup failure shouldn't mask successful update
                _logger.LogWarning(ex, "Failed to delete unused image: {ImageUrl}", imageUrl);
            }
        }
    }

    /// <summary>
    /// Extract public ID from Cloudinary URL for deletion.
    /// </summary>
    private static string? ExtractPublicIdFromUrl(string url)
    {
        // Cloudinary URL format: https://res.cloudinary.com/{cloud}/image/upload/v{version}/{folder}/{public_id}.{ext}
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath; // /image/upload/v123456789/rewear/products/abc123.jpg
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Find "upload" segment, then skip "v{version}", then take the rest as public ID
            int uploadIndex = Array.IndexOf(segments, "upload");
            if (uploadIndex >= 0 && uploadIndex + 2 < segments.Length)
            {
                // Skip "upload" and version segment (v123456789)
                var publicIdParts = segments.Skip(uploadIndex + 2);
                return string.Join("/", publicIdParts);
            }
        }
        catch
        {
            // Ignore parsing errors
        }
        return null;
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
