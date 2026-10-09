using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

/// <summary>
/// Các endpoint admin dành riêng cho luồng shipping (reship, hủy vĩnh viễn).
/// Tách khỏi ShippingController để dễ audit quyền: chỉ AdminOnly mới vào được.
/// </summary>
[ApiController]
[Route("api/admin/shipping")]
[Authorize(Policy = "AdminOnly")]
public class AdminShippingController : ControllerBase
{
    private readonly IShippingService _shippingService;
    private readonly IUserRepository _userRepository;

    public AdminShippingController(
        IShippingService shippingService,
        IUserRepository userRepository)
    {
        _shippingService = shippingService;
        _userRepository = userRepository;
    }

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Lấy tên admin từ JWT (snapshot vào timeline).
    /// </summary>
    private async Task<string> GetAdminNameAsync(string adminUserId)
    {
        var admin = await _userRepository.GetByIdAsync(adminUserId);
        return admin?.FullName ?? "Admin";
    }

    /// <summary>
    /// Admin yêu cầu giao lại đơn đang Failed (sau khi shipper trả hàng về kho).
    /// Order Failed → Confirmed; Shipping reset về Pending (giữ AttemptCount).
    /// </summary>
    /// <param name="orderId">Id đơn hàng cần reship.</param>
    /// <param name="request">ShipperId mới (optional) + note.</param>
    [HttpPost("{orderId}/reship")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> Reship(
        string orderId, [FromBody] ReshipRequest request)
    {
        var adminUserId = GetUserId();
        if (string.IsNullOrEmpty(adminUserId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var adminName = await GetAdminNameAsync(adminUserId);

        var result = await _shippingService.ReshipAsync(orderId, request, adminUserId, adminName);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                ApiErrorCode.Validation => BadRequest(result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }

    /// <summary>
    /// Admin hủy vĩnh viễn đơn đang Failed: buyer mất tiền + hàng, sản phẩm vẫn Sold.
    /// Order Failed → Cancelled; Shipping Failed → Returned.
    /// </summary>
    /// <param name="orderId">Id đơn hàng cần hủy vĩnh viễn.</param>
    /// <param name="request">Lý do hủy.</param>
    [HttpPut("{orderId}/cancel-permanent")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> CancelPermanent(
        string orderId, [FromBody] CancelPermanentRequest request)
    {
        var adminUserId = GetUserId();
        if (string.IsNullOrEmpty(adminUserId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var adminName = await GetAdminNameAsync(adminUserId);

        var result = await _shippingService.CancelPermanentAsync(orderId, request, adminUserId, adminName);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                ApiErrorCode.Validation => BadRequest(result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }
}
