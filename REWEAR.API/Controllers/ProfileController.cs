using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;
    private readonly ICloudinaryService _cloudinaryService;

    public ProfileController(IProfileService profileService, ICloudinaryService cloudinaryService)
    {
        _profileService = profileService;
        _cloudinaryService = cloudinaryService;
    }

    /// <summary>
    /// Lấy userId từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ============================================
    // USER PROFILE APIs
    // ============================================

    /// <summary>
    /// Lấy thông tin cá nhân của người dùng hiện tại
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var profile = await _profileService.GetProfileAsync(userId);
        if (profile == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy người dùng." });

        return Ok(new ApiResponse { Success = true, Data = profile });
    }

    /// <summary>
    /// Cập nhật thông tin cá nhân
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _profileService.UpdateProfileAsync(userId, request);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Cập nhật ảnh đại diện bằng cách upload file
    /// </summary>
    [HttpPut("avatar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateAvatar(IFormFile file)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        if (file == null || file.Length == 0)
            return BadRequest(new ApiResponse { Success = false, Message = "Vui lòng chọn file ảnh." });

        // Upload lên Cloudinary (folder: rewear/avatars, tên file: user_{userId})
        using var stream = file.OpenReadStream();
        var uploadRequest = new ImageUploadRequest
        {
            FileStream = stream,
            FileName = file.FileName,
            Folder = "avatars",
            PublicId = $"user_{userId}"
        };

        var uploadResult = await _cloudinaryService.UploadImageAsync(uploadRequest);

        if (!uploadResult.Success)
            return BadRequest(new ApiResponse
            {
                Success = false,
                Message = uploadResult.ErrorMessage ?? "Upload thất bại."
            });

        // Lưu URL vào DB
        var updateResult = await _profileService.UpdateAvatarAsync(userId, uploadResult.Url!);
        if (!updateResult.Success)
            return BadRequest(updateResult);

        return Ok(new ApiResponse
        {
            Success = true,
            Message = "Cập nhật avatar thành công.",
            Data = new
            {
                AvatarUrl = uploadResult.Url,
                PublicId = uploadResult.PublicId
            }
        });
    }

    // ============================================
    // ADDRESS APIs
    // ============================================

    /// <summary>
    /// Lấy danh sách địa chỉ của người dùng hiện tại
    /// </summary>
    [HttpGet("addresses")]
    public async Task<IActionResult> GetAddresses()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var addresses = await _profileService.GetAddressesAsync(userId);
        return Ok(new ApiResponse { Success = true, Data = addresses });
    }

    /// <summary>
    /// Lấy chi tiết một địa chỉ
    /// </summary>
    [HttpGet("addresses/{id}")]
    public async Task<IActionResult> GetAddressById(string id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var address = await _profileService.GetAddressByIdAsync(id, userId);
        if (address == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy địa chỉ." });

        return Ok(new ApiResponse { Success = true, Data = address });
    }

    /// <summary>
    /// Thêm địa chỉ mới
    /// </summary>
    [HttpPost("addresses")]
    public async Task<IActionResult> CreateAddress([FromBody] AddressRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _profileService.CreateAddressAsync(userId, request);
        if (!result.Success)
            return BadRequest(result);

        return CreatedAtAction(nameof(GetAddressById), new { id = ((AddressResponse?)result.Data)?.Id }, result);
    }

    /// <summary>
    /// Cập nhật địa chỉ
    /// </summary>
    [HttpPut("addresses/{id}")]
    public async Task<IActionResult> UpdateAddress(string id, [FromBody] AddressRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _profileService.UpdateAddressAsync(id, userId, request);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Xóa địa chỉ
    /// </summary>
    [HttpDelete("addresses/{id}")]
    public async Task<IActionResult> DeleteAddress(string id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _profileService.DeleteAddressAsync(id, userId);
        if (!result.Success)
            return NotFound(result);

        return Ok(result);
    }

    /// <summary>
    /// Đặt địa chỉ mặc định
    /// </summary>
    [HttpPut("addresses/{id}/default")]
    public async Task<IActionResult> SetDefaultAddress(string id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _profileService.SetDefaultAddressAsync(id, userId);
        if (!result.Success)
            return NotFound(result);

        return Ok(result);
    }
}
