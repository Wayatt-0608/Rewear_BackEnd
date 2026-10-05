using REWEAR.Application.DTOs;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Order Service (Task 6).
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Tạo đơn hàng từ giỏ hàng hiện tại của người dùng (đây là Task 5 Checkout).
    /// </summary>
    /// <param name="userId">Id người mua (lấy từ JWT).</param>
    /// <param name="request">Thông tin checkout: địa chỉ giao hàng, phương thức thanh toán.</param>
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
    /// Hủy đơn hàng. Chỉ hủy được khi đơn chưa hoàn thành (chưa giao).
    /// Khi hủy, sản phẩm sẽ được trả về trạng thái Available.
    /// </summary>
    Task<ApiResponse> CancelOrderAsync(string userId, string orderId, string? reason);
}