using REWEAR.Application.DTOs;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Order Service (Task 6 + Task 7).
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Tạo đơn hàng từ giỏ hàng hiện tại của người dùng (Task 5 Checkout).
    /// Đơn được tạo ở trạng thái AwaitingPayment, sản phẩm được giữ chỗ,
    /// và kèm theo phiên thanh toán PayOS để người dùng hoàn tất mua hàng.
    /// </summary>
    /// <param name="userId">Id người mua (lấy từ JWT).</param>
    /// <param name="request">Thông tin checkout: địa chỉ giao hàng, phương thức vận chuyển.</param>
    Task<ApiResponse> CheckoutAsync(string userId, CheckoutRequest request);

    /// <summary>
    /// Lấy danh sách đơn hàng của người dùng (có phân trang và lọc theo trạng thái).
    /// </summary>
    Task<PagedOrderResponse> GetOrdersByUserAsync(string userId, OrderQueryParameters query);

    /// <summary>
    /// Lấy chi tiết một đơn hàng. Người dùng chỉ xem được đơn của chính mình.
    /// </summary>
    Task<OrderResponse?> GetOrderDetailAsync(string userId, string orderId);

    /// <summary>
    /// Lấy chi tiết đơn hàng bằng mã đơn hiển thị (vd: RW-20261005-A1B2C3).
    /// </summary>
    Task<OrderResponse?> GetOrderDetailByCodeAsync(string userId, string orderCode);

    /// <summary>
    /// Hủy đơn hàng. Chỉ hủy được khi đơn chưa thu tiền và chưa giao.
    /// Sản phẩm được trả về trạng thái Available.
    /// Nếu đơn đã thanh toán, việc hủy và hoàn tiền do <see cref="IPaymentService"/> xử lý.
    /// </summary>
    Task<ApiResponse> CancelOrderAsync(string userId, string orderId, string? reason);

    /// <summary>
    /// Lấy thông tin theo dõi đơn: mã vận đơn, ngày dự kiến giao và timeline trạng thái (Task 7).
    /// </summary>
    Task<OrderTrackingResponse?> GetTrackingAsync(string userId, string orderId);

    /// <summary>
    /// Cập nhật trạng thái đơn (Admin/Shipper) và ghi vào timeline (Task 7).
    /// Tuân thủ state machine: AwaitingPayment → Confirmed → Shipping → Delivered.
    /// </summary>
    Task<ApiResponse> UpdateStatusAsync(
        string orderId, OrderStatus newStatus, string? note,
        string? trackingNumber, DateTime? estimatedDeliveryDate,
        OrderStatusChangedBy changedBy, string? changedByUserId);
}