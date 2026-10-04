using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.API.Models;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Controllers;

/// <summary>
/// Controller xử lý Sourcing Request (Thu mua đồ cũ).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SourcingController : ControllerBase
{
    private readonly ISourcingService _sourcingService;
    private readonly ICloudinaryService _cloudinaryService;

    public SourcingController(ISourcingService sourcingService, ICloudinaryService cloudinaryService)
    {
        _sourcingService = sourcingService;
        _cloudinaryService = cloudinaryService;
    }

    /// <summary>
    /// Customer: Tạo sourcing request mới (multipart/form-data).
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [Authorize]
    public async Task<IActionResult> Create([FromForm] CreateSourcingFormRequest formRequest)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Không xác định được người dùng." });

            // ===== IMAGE VALIDATION =====
            if (formRequest.Images == null || formRequest.Images.Count == 0)
                return BadRequest(new { message = "Phải upload ít nhất 1 hình ảnh." });

            if (formRequest.Images.Count > 5)
                return BadRequest(new { message = "Tối đa 5 hình ảnh." });

            // ===== UPLOAD IMAGES TO CLOUDINARY =====
            var uploadedUrls = new List<string>();
            var uploadedPublicIds = new List<string>();

            try
            {
                foreach (var image in formRequest.Images)
                {
                    if (image == null || image.Length == 0)
                    {
                        await RollbackUploadedImages(uploadedPublicIds);
                        return BadRequest(new { message = "Có hình ảnh không hợp lệ hoặc trống." });
                    }

                    var publicId = $"sourcing_{Guid.NewGuid():N}";
                    using var stream = image.OpenReadStream();

                    var uploadRequest = new ImageUploadRequest
                    {
                        FileStream = stream,
                        FileName = image.FileName,
                        Folder = "sourcing",
                        PublicId = publicId
                    };

                    var result = await _cloudinaryService.UploadImageAsync(uploadRequest);

                    if (!result.Success)
                    {
                        await RollbackUploadedImages(uploadedPublicIds);
                        return BadRequest(new { message = $"Upload ảnh thất bại: {result.ErrorMessage}" });
                    }

                    uploadedUrls.Add(result.Url!);
                    uploadedPublicIds.Add(result.PublicId!);
                }
            }
            catch (Exception ex)
            {
                await RollbackUploadedImages(uploadedPublicIds);
                return BadRequest(new { message = $"Lỗi khi upload ảnh: {ex.Message}" });
            }

            // ===== CREATE SOURCING REQUEST =====
            try
            {
                var request = new CreateSourcingRequest
                {
                    Title = formRequest.Title,
                    Description = formRequest.Description,
                    BrandId = formRequest.BrandId,
                    CategoryId = formRequest.CategoryId,
                    DeclaredCondition = formRequest.DeclaredCondition,
                    Size = formRequest.Size,
                    Color = formRequest.Color,
                    CustomerExpectedPrice = formRequest.CustomerExpectedPrice,
                    ImageUrls = uploadedUrls,
                    ImagePublicIds = uploadedPublicIds
                };

                var result = await _sourcingService.CreateAsync(userId, request);
                return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
            }
            catch (ArgumentException ex)
            {
                await RollbackUploadedImages(uploadedPublicIds);
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                await RollbackUploadedImages(uploadedPublicIds);
                return Conflict(new { message = ex.Message });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Lỗi không xác định: {ex.Message}" });
        }
    }

    /// <summary>
    /// Helper method to rollback uploaded images on failure.
    /// </summary>
    private async Task RollbackUploadedImages(List<string> publicIds)
    {
        foreach (var publicId in publicIds)
        {
            try
            {
                await _cloudinaryService.DeleteImageAsync(publicId);
            }
            catch
            {
                // Log warning but don't fail - cleanup failure shouldn't mask original error
            }
        }
    }

    /// <summary>
    /// Customer: Lấy tất cả sourcing request của mình.
    /// </summary>
    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMy()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = "Không xác định được người dùng." });

        var result = await _sourcingService.GetByUserIdAsync(userId);
        return Ok(result);
    }

    /// <summary>
    /// Customer: Lấy chi tiết sourcing request của mình.
    /// Staff/Admin: Lấy chi tiết bất kỳ sourcing request nào.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetById(string id)
    {
        var result = await _sourcingService.GetByIdAsync(id);
        if (result == null)
            return NotFound(new { message = "Không tìm thấy sourcing request." });

        // Ownership check: customer chỉ được xem request của mình
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (role != "Admin" && role != "Staff" && result.UserId != userId)
            return Forbid();

        return Ok(result);
    }

    /// <summary>
    /// Staff/Admin: Lấy tất cả sourcing requests với filter status.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "StaffOrAdmin")]
    public async Task<IActionResult> GetAll([FromQuery] SourcingStatus? status)
    {
        var result = await _sourcingService.GetAllAsync(status);
        return Ok(result);
    }

    /// <summary>
    /// Staff/Admin: Bắt đầu review (Pending → UnderReview).
    /// </summary>
    [HttpPatch("{id}/review")]
    [Authorize(Policy = "StaffOrAdmin")]
    public async Task<IActionResult> StartReview(string id)
    {
        try
        {
            var result = await _sourcingService.StartReviewAsync(id);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy sourcing request." });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Staff/Admin: Định giá (UnderReview → Priced).
    /// </summary>
    [HttpPatch("{id}/offer")]
    [Authorize(Policy = "StaffOrAdmin")]
    public async Task<IActionResult> OfferPrice(string id, [FromBody] OfferSourcingRequest request)
    {
        try
        {
            var result = await _sourcingService.OfferPriceAsync(id, request);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy sourcing request." });

            return Ok(result);
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
    /// Staff/Admin: Từ chối request (Pending/UnderReview → Rejected).
    /// </summary>
    [HttpPatch("{id}/reject")]
    [Authorize(Policy = "StaffOrAdmin")]
    public async Task<IActionResult> Reject(string id, [FromBody] RejectSourcingRequest request)
    {
        try
        {
            var result = await _sourcingService.RejectAsync(id, request);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy sourcing request." });

            return Ok(result);
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
    /// Customer: Đồng ý giá (Priced → Accepted).
    /// </summary>
    [HttpPatch("{id}/accept")]
    [Authorize]
    public async Task<IActionResult> AcceptOffer(string id)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Không xác định được người dùng." });

            var result = await _sourcingService.AcceptOfferAsync(id, userId);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy sourcing request." });

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Customer: Từ chối giá (Priced → Declined).
    /// </summary>
    [HttpPatch("{id}/decline")]
    [Authorize]
    public async Task<IActionResult> DeclineOffer(string id)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Không xác định được người dùng." });

            var result = await _sourcingService.DeclineOfferAsync(id, userId);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy sourcing request." });

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Staff/Admin: Đánh dấu đã nhận hàng (Accepted → Received).
    /// </summary>
    [HttpPatch("{id}/received")]
    [Authorize(Policy = "StaffOrAdmin")]
    public async Task<IActionResult> MarkReceived(string id)
    {
        try
        {
            var result = await _sourcingService.MarkReceivedAsync(id);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy sourcing request." });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Staff/Admin: Kiểm định (Received → Approved).
    /// </summary>
    [HttpPatch("{id}/inspect")]
    [Authorize(Policy = "StaffOrAdmin")]
    public async Task<IActionResult> Inspect(string id, [FromBody] InspectSourcingRequest request)
    {
        try
        {
            var result = await _sourcingService.InspectAsync(id, request);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy sourcing request." });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Staff/Admin: Convert sang Product (Approved → Completed).
    /// </summary>
    [HttpPost("{id}/convert-to-product")]
    [Authorize(Policy = "StaffOrAdmin")]
    public async Task<IActionResult> ConvertToProduct(string id, [FromBody] ConvertToProductRequest request)
    {
        try
        {
            var result = await _sourcingService.ConvertToProductAsync(id, request);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy sourcing request." });

            return Ok(result);
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
}
