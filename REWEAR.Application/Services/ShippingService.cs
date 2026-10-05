using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý vận chuyển (Task 9).
/// </summary>
/// <remarks>
/// REWEAR miễn phí vận chuyển toàn bộ nên phí ship luôn = 0; phương thức vận chuyển
/// chỉ khác nhau ở thời gian giao. Vận chuyển chỉ được tạo sau khi đơn đã thu tiền.
/// </remarks>
public class ShippingService : IShippingService
{
    private const int STANDARD_DAYS_MIN = 3;
    private const int STANDARD_DAYS_MAX = 5;
    private const int EXPRESS_DAYS_MIN = 1;
    private const int EXPRESS_DAYS_MAX = 2;

    private readonly IShippingRepository _shippingRepository;
    private readonly IOrderRepository _orderRepository;

    public ShippingService(
        IShippingRepository shippingRepository,
        IOrderRepository orderRepository)
    {
        _shippingRepository = shippingRepository;
        _orderRepository = orderRepository;
    }

    /// <summary>
    /// Lấy bảng giá vận chuyển để frontend hiển thị lựa chọn khi checkout.
    /// </summary>
    public Task<List<ShippingRateResponse>> GetRatesAsync()
    {
        var rates = new List<ShippingRateResponse>
        {
            new ShippingRateResponse
            {
                Method = ShippingMethod.Standard,
                Label = "Giao hàng tiêu chuẩn",
                Fee = 0m,
                EstimatedDays = $"{STANDARD_DAYS_MIN}-{STANDARD_DAYS_MAX} ngày"
            },
            new ShippingRateResponse
            {
                Method = ShippingMethod.Express,
                Label = "Giao hàng nhanh",
                Fee = 0m,
                EstimatedDays = $"{EXPRESS_DAYS_MIN}-{EXPRESS_DAYS_MAX} ngày"
            }
        };

        return Task.FromResult(rates);
    }

    /// <summary>
    /// Tạo thông tin vận chuyển cho đơn. Chỉ được gọi khi đơn đã thu tiền.
    /// </summary>
    public async Task<ApiResponse> CreateShippingAsync(string orderId, ShippingMethod method)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        // Bắt buộc đã thu tiền mới được giao: không giao hàng khi khách chưa trả.
        if (order.Status is OrderStatus.AwaitingPayment or OrderStatus.PaymentExpired
            or OrderStatus.Cancelled)
        {
            return Fail(
                "Chỉ tạo vận chuyển cho đơn đã thanh toán thành công.",
                ApiErrorCode.Conflict);
        }

        // Đã có thông tin vận chuyển thì không tạo lại (tránh nhân đôi).
        var existing = await _shippingRepository.GetByOrderIdAsync(orderId);
        if (existing != null)
            return Fail("Đơn hàng đã có thông tin vận chuyển.", ApiErrorCode.Conflict);

        var shipping = new Shipping
        {
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            Method = method,
            Fee = 0m,   // REWEAR miễn phí vận chuyển
            Status = ShippingStatus.Pending,
            EstimatedDeliveryDate = CalculateEstimatedDelivery(method)
        };

        await _shippingRepository.CreateAsync(shipping);

        // Mirror ngày dự kiến giao lên Order để khách xem được ngay trên tracking.
        if (!order.EstimatedDeliveryDate.HasValue)
        {
            order.EstimatedDeliveryDate = shipping.EstimatedDeliveryDate;
            await _orderRepository.UpdateAsync(order);
        }

        return new ApiResponse
        {
            Success = true,
            Message = "Đã tạo thông tin vận chuyển.",
            Data = BuildShippingResponse(shipping)
        };
    }

    /// <summary>
    /// Cập nhật vận chuyển: bàn giao cho đơn vị vận chuyển hoặc đánh dấu giao xong.
    /// </summary>
    public async Task<ApiResponse> UpdateShippingAsync(string orderId, UpdateShippingRequest request)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        var shipping = await _shippingRepository.GetByOrderIdAsync(orderId);
        if (shipping == null)
            return Fail("Đơn hàng chưa có thông tin vận chuyển.", ApiErrorCode.NotFound);

        // Đã giao xong rồi thì không sửa nữa (sản phẩm đã chốt Sold).
        if (shipping.Status == ShippingStatus.Delivered)
            return Fail("Đơn hàng đã giao xong, không thể cập nhật vận chuyển.", ApiErrorCode.Conflict);

        if (!string.IsNullOrWhiteSpace(request.Carrier))
            shipping.Carrier = request.Carrier;

        if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            shipping.TrackingNumber = request.TrackingNumber;

            // Mirror mã vận đơn sang Order để khách xem ở trang tracking (Task 7).
            order.TrackingNumber = request.TrackingNumber;
        }

        if (request.EstimatedDeliveryDate.HasValue)
        {
            shipping.EstimatedDeliveryDate = request.EstimatedDeliveryDate;
            order.EstimatedDeliveryDate = request.EstimatedDeliveryDate;
        }

        if (!string.IsNullOrWhiteSpace(request.Note))
            shipping.Note = request.Note;

        // Có mã vận đơn mà chưa gửi hàng -> đánh dấu đã bàn giao (đang chuyển).
        if (shipping.Status == ShippingStatus.Pending
            && !string.IsNullOrWhiteSpace(shipping.TrackingNumber))
        {
            shipping.Status = ShippingStatus.InTransit;
            shipping.ShippedAt = DateTime.UtcNow;
        }

        shipping.UpdatedAt = DateTime.UtcNow;
        await _shippingRepository.UpdateAsync(shipping);
        await _orderRepository.UpdateAsync(order);

        return new ApiResponse
        {
            Success = true,
            Message = $"Đã cập nhật vận chuyển (trạng thái: {shipping.Status}).",
            Data = BuildShippingResponse(shipping)
        };
    }

    /// <summary>
    /// Lấy thông tin vận chuyển của đơn. Nếu có userId thì kiểm tra sở hữu.
    /// </summary>
    public async Task<ShippingResponse?> GetByOrderIdAsync(string orderId, string? userId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return null;

        if (userId != null && order.UserId != userId)
            return null;

        var shipping = await _shippingRepository.GetByOrderIdAsync(orderId);
        if (shipping == null)
            return null;

        return BuildShippingResponse(shipping);
    }

    // ============================================
    // HELPER
    // ============================================

    /// <summary>
    /// Tính ngày dự kiến giao dựa trên phương thức vận chuyển.
    /// </summary>
    private static DateTime CalculateEstimatedDelivery(ShippingMethod method)
    {
        int days = method == ShippingMethod.Express
            ? EXPRESS_DAYS_MIN
            : STANDARD_DAYS_MIN;

        return DateTime.UtcNow.AddDays(days);
    }

    /// <summary>
    /// Chuyển entity Shipping sang DTO trả về cho frontend.
    /// </summary>
    private static ShippingResponse BuildShippingResponse(Shipping s)
    {
        return new ShippingResponse
        {
            Id = s.Id,
            OrderId = s.OrderId,
            OrderCode = s.OrderCode,
            Method = s.Method.ToString(),
            Fee = s.Fee,
            Status = s.Status.ToString(),
            Carrier = s.Carrier,
            TrackingNumber = s.TrackingNumber,
            EstimatedDeliveryDate = s.EstimatedDeliveryDate,
            ShippedAt = s.ShippedAt,
            DeliveredAt = s.DeliveredAt
        };
    }

    private static ApiResponse Fail(string message, string errorCode = ApiErrorCode.Validation)
        => new ApiResponse { Success = false, Message = message, ErrorCode = errorCode };
}