using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/vouchers")]
// Yêu cầu đăng nhập, không giới hạn role: mọi role đều có "kho voucher của tôi".
// Trước đây dùng [Authorize(Roles = nameof(UserRole.Member))] khiến Admin/Staff/Shipper
// bị 403 khi gọi các endpoint này.
[Authorize]
[TypeFilter(typeof(REWEAR.API.Filters.ObjectIdRouteFilter))]
public class VouchersController : ControllerBase
{
    private readonly IVoucherService _voucherService;
    private readonly IUserVoucherService _userVoucherService;

    public VouchersController(
        IVoucherService voucherService,
        IUserVoucherService userVoucherService)
    {
        _voucherService = voucherService;
        _userVoucherService = userVoucherService;
    }

    /// <summary>
    /// Lấy userId từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ============================================
    // USER VOUCHER COLLECTION
    // ============================================

    /// <summary>
    /// Lấy kho voucher của tôi.
    /// </summary>
    /// <param name="filter">
    /// all = tất cả;
    /// available = còn dùng được;
    /// used = đã dùng;
    /// expired = hết hạn / không dùng được.
    /// </param>
    [HttpGet("my-collection")]
    [ProducesResponseType(typeof(List<UserVoucherResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyCollection([FromQuery] string filter = "all")
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var collection = await _userVoucherService.GetMyCollectionAsync(userId, filter);
        return Ok(new ApiResponse { Success = true, Data = collection });
    }

    /// <summary>
    /// Lấy chi tiết 1 voucher trong kho của tôi.
    /// </summary>
    [HttpGet("my-collection/{id}")]
    [ProducesResponseType(typeof(UserVoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyVoucherById(string id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var voucher = await _userVoucherService.GetByIdAsync(id, userId);
        if (voucher == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy voucher." });

        return Ok(new ApiResponse { Success = true, Data = voucher });
    }

    // ============================================
    // APPLY VOUCHER
    // ============================================

    /// <summary>
    /// Validate + preview khi áp dụng voucher vào đơn hàng.
    /// </summary>
    /// <remarks>
    /// Chỉ trả preview (không lưu DB). Bước tiếp theo là checkout với VoucherCode.
    /// Ví dụ payload:
    /// {
    ///   "code": "TET2026",
    ///   "orderTotal": 500000
    /// }
    /// </remarks>
    [HttpPost("apply")]
    [ProducesResponseType(typeof(ApplyVoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Apply([FromBody] ApplyVoucherRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var result = await _voucherService.ApplyVoucherAsync(userId, request);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden, result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }
}
