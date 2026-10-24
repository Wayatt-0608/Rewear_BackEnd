using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/admin/vouchers")]
[Authorize(Policy = Permission.VoucherManage)]
[TypeFilter(typeof(REWEAR.API.Filters.ObjectIdRouteFilter))]
public class AdminVouchersController : ControllerBase
{
    private readonly IVoucherService _voucherService;

    public AdminVouchersController(IVoucherService voucherService)
    {
        _voucherService = voucherService;
    }

    /// <summary>
    /// Lấy adminId từ token JWT.
    /// </summary>
    private string? GetAdminId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ============================================
    // CRUD
    // ============================================

    /// <summary>
    /// Tạo voucher mới.
    /// </summary>
    /// <remarks>
    /// Ví dụ payload:
    /// {
    ///   "code": "TET2026",
    ///   "name": "Tết 2026 - Giảm 15% đơn từ 400k",
    ///   "type": "Percentage",
    ///   "value": 15,
    ///   "minOrderAmount": 400000,
    ///   "minDiscount": 40000,
    ///   "maxDiscount": 100000,
    ///   "expiryDate": "2026-02-05T23:59:59Z",
    ///   "usageLimit": 1000,
    ///   "usageLimitPerUser": 1
    /// }
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(VoucherResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateVoucherRequest request)
    {
        var adminId = GetAdminId();
        if (string.IsNullOrEmpty(adminId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được admin." });

        try
        {
            var voucher = await _voucherService.CreateAsync(request, adminId);
            return CreatedAtAction(nameof(GetById), new { id = voucher.Id }, voucher);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiResponse { Success = false, Message = ex.Message, ErrorCode = ApiErrorCode.Validation });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiResponse { Success = false, Message = ex.Message, ErrorCode = ApiErrorCode.Conflict });
        }
    }

    /// <summary>
    /// Lấy danh sách voucher (lọc theo isActive).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<VoucherResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool? isActive)
    {
        var vouchers = await _voucherService.GetAllAsync(isActive);
        return Ok(new ApiResponse { Success = true, Data = vouchers });
    }

    /// <summary>
    /// Lấy chi tiết 1 voucher.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(VoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id)
    {
        var voucher = await _voucherService.GetByIdAsync(id);
        if (voucher == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy voucher." });

        return Ok(new ApiResponse { Success = true, Data = voucher });
    }

    /// <summary>
    /// Cập nhật voucher (không cho sửa Code và UsedCount).
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(VoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateVoucherRequest request)
    {
        try
        {
            var voucher = await _voucherService.UpdateAsync(id, request);
            if (voucher == null)
                return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy voucher." });

            return Ok(new ApiResponse { Success = true, Message = "Đã cập nhật voucher.", Data = voucher });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiResponse { Success = false, Message = ex.Message, ErrorCode = ApiErrorCode.Validation });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiResponse { Success = false, Message = ex.Message, ErrorCode = ApiErrorCode.Conflict });
        }
    }

    /// <summary>
    /// Soft delete: tắt voucher. Voucher vẫn còn trong DB nhưng user không dùng được.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id)
    {
        var ok = await _voucherService.DeleteAsync(id);
        if (!ok)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy voucher." });

        return Ok(new ApiResponse { Success = true, Message = "Đã tắt voucher." });
    }

    // ============================================
    // GRANT
    // ============================================

    /// <summary>
    /// Phát voucher cho TẤT CẢ Member hiện tại trong hệ thống.
    /// </summary>
    /// <remarks>
    /// Bỏ qua Admin/Staff/Shipper - chỉ phát cho Member.
    /// User đã nhận voucher trước đó sẽ bị bỏ qua (idempotent).
    /// </remarks>
    [HttpPost("{id}/grant-all")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GrantToAllMembers(string id)
    {
        var adminId = GetAdminId();
        if (string.IsNullOrEmpty(adminId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được admin." });

        try
        {
            var result = await _voucherService.GrantToAllMembersAsync(id, adminId);
            return Ok(new ApiResponse
            {
                Success = true,
                Message = result.Message,
                Data = new
                {
                    grantedCount = result.GrantedCount,
                    skippedCount = result.SkippedCount
                }
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiResponse { Success = false, Message = ex.Message, ErrorCode = ApiErrorCode.Validation });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiResponse { Success = false, Message = ex.Message, ErrorCode = ApiErrorCode.Conflict });
        }
    }

    /// <summary>
    /// Phát voucher cho 1 user cụ thể (phải là Member).
    /// </summary>
    [HttpPost("{id}/grant")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GrantToUser(string id, [FromBody] GrantVoucherToUserRequest request)
    {
        var adminId = GetAdminId();
        if (string.IsNullOrEmpty(adminId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được admin." });

        try
        {
            var granted = await _voucherService.GrantToUserAsync(id, request.UserId, adminId);

            if (granted)
            {
                return Ok(new ApiResponse { Success = true, Message = "Đã phát voucher cho user." });
            }

            return Ok(new ApiResponse { Success = true, Message = "User đã có voucher này từ trước." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiResponse { Success = false, Message = ex.Message, ErrorCode = ApiErrorCode.Validation });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiResponse { Success = false, Message = ex.Message, ErrorCode = ApiErrorCode.Conflict });
        }
    }

    // ============================================
    // STATS
    // ============================================

    /// <summary>
    /// Thống kê sử dụng voucher: tổng phát, đã dùng, còn lại.
    /// </summary>
    [HttpGet("{id}/stats")]
    [ProducesResponseType(typeof(VoucherStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStats(string id)
    {
        var stats = await _voucherService.GetStatsAsync(id);
        if (stats == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy voucher." });

        return Ok(new ApiResponse { Success = true, Data = stats });
    }
}
