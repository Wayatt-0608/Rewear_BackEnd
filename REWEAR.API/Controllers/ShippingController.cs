using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/shipping")]
[Authorize]
public class ShippingController : ControllerBase
{
    private readonly IShippingService _shippingService;
    private readonly IUserRepository _userRepository;

    public ShippingController(
        IShippingService shippingService,
        IUserRepository userRepository)
    {
        _shippingService = shippingService;
        _userRepository = userRepository;
    }

    /// <summary>
    /// Lấy userId và role từ token JWT.
    /// </summary>
    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Lấy role từ claim "role" trong JWT (ASP.NET Core sẽ map ClaimTypes.Role = "role").
    /// </summary>
    private UserRole? GetUserRole()
    {
        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrEmpty(roleClaim)) return null;
        if (Enum.TryParse<UserRole>(roleClaim, out var role)) return role;
        return null;
    }

    // ============================================
    // VẬN CHUYỂN (Task 9 + Task Shipper)
    // ============================================

    /// <summary>
    /// Lấy bảng giá vận chuyển để hiển thị lựa chọn khi checkout.
    /// </summary>
    /// <remarks>REWEAR miễn phí vận chuyển toàn bộ nên phí luôn = 0.</remarks>
    [HttpGet("rates")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> GetRates()
    {
        var rates = await _shippingService.GetRatesAsync();
        return Ok(new ApiResponse { Success = true, Data = rates });
    }

    /// <summary>
    /// Lấy thông tin vận chuyển của một đơn (chỉ xem được đơn của chính mình).
    /// </summary>
    /// <param name="orderId">Id của đơn hàng cần xem thông tin vận chuyển.</param>
    [HttpGet("{orderId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> GetShipping(string orderId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var shipping = await _shippingService.GetByOrderIdAsync(orderId, userId);

        if (shipping == null)
            return NotFound(new ApiResponse
            {
                Success = false,
                Message = "Không tìm thấy thông tin vận chuyển."
            });

        return Ok(new ApiResponse { Success = true, Data = shipping });
    }

    /// <summary>
    /// Tạo thông tin vận chuyển cho đơn (chọn phương thức giao hàng + phân bổ shipper).
    /// </summary>
    /// <remarks>Chỉ được tạo khi đơn đã thanh toán thành công.</remarks>
    /// <param name="orderId">Id của đơn hàng cần tạo vận chuyển.</param>
    /// <param name="shipperId">Id shipper được phân bổ (bắt buộc đối với Admin/Staff).</param>
    /// <param name="method">Phương thức vận chuyển (Standard / Express). Mặc định Standard.</param>
    /// <param name="carrier">Đơn vị vận chuyển (vd: "GHN", "GHTK").</param>
    [HttpPost("{orderId}")]
    [Authorize(Policy = "StaffOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> CreateShipping(
        string orderId,
        [FromQuery] string? shipperId,
        [FromQuery] ShippingMethod method = ShippingMethod.Standard,
        [FromQuery] string? carrier = null)
    {
        var result = await _shippingService.CreateShippingAsync(orderId, shipperId, method, carrier);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                ApiErrorCode.Forbidden => Forbid(),
                ApiErrorCode.Validation => BadRequest(result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }

    /// <summary>
    /// Cập nhật vận chuyển: nhập mã vận đơn (đang chuyển) hoặc ghi chú giao hàng.
    /// </summary>
    /// <param name="orderId">Id của đơn hàng cần cập nhật.</param>
    /// <param name="request">Đơn vị vận chuyển, mã vận đơn, ngày dự kiến giao và ghi chú.</param>
    [HttpPut("{orderId}")]
    [Authorize(Policy = "ShippingOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> UpdateShipping(
        string orderId, [FromBody] UpdateShippingRequest request)
    {
        var result = await _shippingService.UpdateShippingAsync(orderId, request);

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

    /// <summary>
    /// Cập nhật trạng thái vận chuyển (Pending / InTransit / Delivered / Failed).
    /// Đồng bộ Order.Status, ghi timeline, cập nhật AttemptCount.
    /// </summary>
    /// <param name="orderId">Id đơn hàng.</param>
    /// <param name="request">Mã trạng thái mới (theo ShippingStatus) + note + trackingNumber + attemptCount.</param>
    [HttpPut("{orderId}/status")]
    [Authorize(Policy = "ShippingOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> UpdateShippingStatus(
        string orderId, [FromBody] UpdateShippingStatusRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var userRole = GetUserRole() ?? UserRole.Member;

        var result = await _shippingService.UpdateShippingStatusAsync(orderId, request, userId, userRole);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ApiErrorCode.NotFound => NotFound(result),
                ApiErrorCode.Conflict => Conflict(result),
                ApiErrorCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden, result),
                ApiErrorCode.Validation => BadRequest(result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách đơn chờ giao (Admin/Staff): order Confirmed chưa gán shipper cụ thể.
    /// </summary>
    [HttpGet("queue")]
    [Authorize(Policy = "StaffOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> GetShippingQueue()
    {
        var queue = await _shippingService.GetShippingQueueAsync();
        return Ok(new ApiResponse { Success = true, Data = queue });
    }

    /// <summary>
    /// Lấy danh sách đơn của shipper hiện tại (filter theo status).
    /// </summary>
    /// <param name="status">Lọc theo trạng thái (Pending / InTransit / Delivered / Failed). Null = tất cả.</param>
    /// <param name="shipperId">Chỉ Admin/Staff mới được truyền: xem đơn của shipper khác.</param>
    [HttpGet("my-orders")]
    [Authorize(Policy = "ShippingOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> GetMyOrders(
        [FromQuery] ShippingStatus? status,
        [FromQuery] string? shipperId = null)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new ApiResponse { Success = false, Message = "Không xác định được người dùng." });

        var userRole = GetUserRole() ?? UserRole.Member;

        var result = await _shippingService.GetMyOrdersAsync(userId, userRole, status, shipperId);
        return Ok(new ApiResponse { Success = true, Data = result });
    }

    /// <summary>
    /// Lấy toàn bộ shipping (AdminOnly, cho dashboard).
    /// </summary>
    /// <param name="status">Lọc theo trạng thái. Null = tất cả.</param>
    [HttpGet("all")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> GetAll(
        [FromQuery] ShippingStatus? status = null)
    {
        var result = await _shippingService.GetAllAsync(status);
        return Ok(new ApiResponse { Success = true, Data = result });
    }
}
