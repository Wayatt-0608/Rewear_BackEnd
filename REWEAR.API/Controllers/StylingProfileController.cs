using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

/// <summary>
/// API hồ sơ sở thích thời trang (Styling Profile) của người dùng.
/// </summary>
/// <remarks>
/// <para>
/// Yêu cầu đăng nhập, không giới hạn role: mọi role đều có "hồ sơ của tôi",
/// đồng bộ với <see cref="WishlistController"/>.
/// </para>
/// <para>
/// userId LUÔN lấy từ JWT (<c>ClaimTypes.NameIdentifier</c>), không bao giờ nhận từ
/// request, nên user chỉ thao tác được trên hồ sơ của chính mình.
/// </para>
/// <para>
/// Đây là hồ sơ sở thích thời trang, KHÔNG phải hồ sơ tài khoản: endpoint này không
/// đọc/ghi entity User (FullName, Avatar, Address) của module Profile.
/// </para>
/// </remarks>
[ApiController]
[Route("api/styling-profile")]
[Authorize]
public class StylingProfileController : ControllerBase
{
    private readonly IStylingProfileService _stylingProfileService;

    public StylingProfileController(IStylingProfileService stylingProfileService)
    {
        _stylingProfileService = stylingProfileService;
    }

    /// <summary>
    /// Lấy userId từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ============================================
    // LẤY HỒ SƠ
    // ============================================

    /// <summary>
    /// Lấy hồ sơ sở thích thời trang của người dùng đang đăng nhập.
    /// </summary>
    /// <remarks>
    /// Nếu chưa có hồ sơ, trả 404 kèm mã lỗi NOT_FOUND để frontend biết cần gọi PUT.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(StylingProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyStylingProfile()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var profile = await _stylingProfileService.GetAsync(userId);
        if (profile == null)
            return NotFound(new ApiResponse
            {
                Success = false,
                Message = "Chưa có hồ sơ sở thích thời trang.",
                ErrorCode = ApiErrorCode.NotFound
            });

        return Ok(new ApiResponse { Success = true, Data = profile });
    }

    // ============================================
    // TẠO / CẬP NHẬT
    // ============================================

    /// <summary>
    /// Tạo mới nếu chưa có hồ sơ, cập nhật nếu đã có.
    /// </summary>
    /// <remarks>
    /// Idempotent theo phía server: gọi PUT nhiều lần không tạo ra nhiều hồ sơ
    /// (mỗi user tối đa 1 hồ sơ, unique index trên userId).
    /// Ngữ nghĩa PUT là ghi đè toàn bộ nội dung hồ sơ.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertMyStylingProfile([FromBody] UpsertStylingProfileRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _stylingProfileService.UpsertAsync(userId, request);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }

    // ============================================
    // XÓA HỒ SƠ
    // ============================================

    /// <summary>
    /// Xóa hồ sơ sở thích thời trang của người dùng đang đăng nhập.
    /// </summary>
    /// <remarks>
    /// Idempotent: chưa có hồ sơ vẫn trả 200.
    /// </remarks>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteMyStylingProfile()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _stylingProfileService.DeleteAsync(userId);
        return Ok(result);
    }
}
