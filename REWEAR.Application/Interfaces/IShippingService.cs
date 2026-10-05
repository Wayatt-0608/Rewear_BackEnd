using REWEAR.Application.DTOs;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Service xử lý vận chuyển (Task 9).
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
    /// </summary>
    Task<ApiResponse> CreateShippingAsync(string orderId, ShippingMethod method);

    /// <summary>
    /// Cập nhật vận chuyển: bàn giao cho đơn vị vận chuyển hoặc đánh dấu giao xong.
    /// </summary>
    Task<ApiResponse> UpdateShippingAsync(string orderId, UpdateShippingRequest request);

    /// <summary>
    /// Lấy thông tin vận chuyển của đơn. Nếu truyền userId thì kiểm tra sở hữu.
    /// </summary>
    Task<ShippingResponse?> GetByOrderIdAsync(string orderId, string? userId);
}