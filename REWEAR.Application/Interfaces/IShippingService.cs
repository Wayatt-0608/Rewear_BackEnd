using REWEAR.Application.DTOs;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Service xử lý vận chuyển (Task 9 + Task Shipper).
/// </summary>
public interface IShippingService
{
    /// <summary>
    /// Lấy bảng giá vận chuyển để frontend hiển thị lựa chọn khi checkout.
    /// REWEAR miễn phí vận chuyển nên Fee luôn = 0.
    /// </summary>
    Task<List<ShippingRateResponse>> GetRatesAsync();

    /// <summary>
    /// Tạo thông tin vận chuyển cho đơn. Chỉ được gọi khi đơn đã thu tiền.
    /// Nếu <paramref name="shipperId"/> được truyền thì sẽ snapshot tên shipper
    /// vào Shipping.ShipperName để hiển thị trên lịch sử.
    /// </summary>
    Task<ApiResponse> CreateShippingAsync(
        string orderId,
        string? shipperId,
        ShippingMethod method,
        string? carrier);

    /// <summary>
    /// Cập nhật vận chuyển: bàn giao cho đơn vị vận chuyển hoặc đánh dấu giao xong.
    /// </summary>
    Task<ApiResponse> UpdateShippingAsync(string orderId, UpdateShippingRequest request);

    /// <summary>
    /// Lấy thông tin vận chuyển của đơn. Nếu truyền userId thì kiểm tra sở hữu.
    /// </summary>
    Task<ShippingResponse?> GetByOrderIdAsync(string orderId, string? userId);

    /// <summary>
    /// Cập nhật trạng thái vận chuyển từ Shipper (hoặc Staff/Admin) đồng thời
    /// đồng bộ <c>Order.Status</c>, ghi timeline và cập nhật
    /// <c>AttemptCount / LastFailureReason / LastFailedAt / DeliveredAt</c>.
    /// </summary>
    /// <param name="orderId">Id đơn hàng cần cập nhật.</param>
    /// <param name="request">Mã trạng thái mới + note + trackingNumber + attemptCount.</param>
    /// <param name="userId">Id user thực hiện (lấy từ JWT).</param>
    /// <param name="userRole">Role user thực hiện (để kiểm tra ownership cho Shipper).</param>
    Task<ApiResponse> UpdateShippingStatusAsync(
        string orderId,
        UpdateShippingStatusRequest request,
        string userId,
        UserRole userRole);

    /// <summary>
    /// Lấy danh sách đơn chờ giao (Admin/Staff): order Confirmed chưa được giao cho shipper cụ thể.
    /// </summary>
    Task<ShippingQueueResponse> GetShippingQueueAsync();

    /// <summary>
    /// Lấy danh sách đơn của shipper hiện tại (filter theo status nếu có).
    /// Admin/Staff có thể truyền <paramref name="targetShipperId"/> để xem đơn của shipper khác.
    /// </summary>
    Task<MyOrdersResponse> GetMyOrdersAsync(
        string currentUserId,
        UserRole currentUserRole,
        ShippingStatus? status,
        string? targetShipperId);

    /// <summary>
    /// Lấy toàn bộ shipping (AdminOnly, cho dashboard).
    /// </summary>
    Task<ShippingListResponse> GetAllAsync(ShippingStatus? status);

    /// <summary>
    /// Admin yêu cầu giao lại sau khi shipper trả hàng về kho.
    /// Order Failed → Confirmed; Shipping giữ AttemptCount, reset Status = Pending,
    /// cập nhật ShipperId mới (nếu truyền).
    /// </summary>
    Task<ApiResponse> ReshipAsync(
        string orderId,
        ReshipRequest request,
        string adminUserId,
        string adminUserName);

    /// <summary>
    /// Admin hủy vĩnh viễn đơn đã Failed: Order Failed → Cancelled,
    /// Shipping Failed → Returned, sản phẩm vẫn Sold (buyer mất tiền + hàng).
    /// </summary>
    Task<ApiResponse> CancelPermanentAsync(
        string orderId,
        CancelPermanentRequest request,
        string adminUserId,
        string adminUserName);
}
