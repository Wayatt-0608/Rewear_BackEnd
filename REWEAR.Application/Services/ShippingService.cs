using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý vận chuyển (Task 9 + Task Shipper).
/// </summary>
/// <remarks>
/// REWEAR miễn phí vận chuyển toàn bộ nên phí ship luôn = 0; phương thức vận chuyển
/// chỉ khác nhau ở thời gian giao. Vận chuyển chỉ được tạo sau khi đơn đã thu tiền.
/// Service này cũng chịu trách nhiệm đồng bộ Shipping.Status với Order.Status
/// và cập nhật các thông tin shipper (AttemptCount, LastFailureReason...).
/// </remarks>
public class ShippingService : IShippingService
{
    private const int STANDARD_DAYS_MIN = 3;
    private const int STANDARD_DAYS_MAX = 5;
    private const int EXPRESS_DAYS_MIN = 1;
    private const int EXPRESS_DAYS_MAX = 2;

    private readonly IShippingRepository _shippingRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderStatusHistoryRepository _historyRepository;
    private readonly IUserRepository _userRepository;
    private readonly IProductRepository _productRepository;

    public ShippingService(
        IShippingRepository shippingRepository,
        IOrderRepository orderRepository,
        IOrderStatusHistoryRepository historyRepository,
        IUserRepository userRepository,
        IProductRepository productRepository)
    {
        _shippingRepository = shippingRepository;
        _orderRepository = orderRepository;
        _historyRepository = historyRepository;
        _userRepository = userRepository;
        _productRepository = productRepository;
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
    public async Task<ApiResponse> CreateShippingAsync(
        string orderId,
        string? shipperId,
        ShippingMethod method,
        string? carrier)
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

        // Snapshot tên shipper để hiển thị trên lịch sử.
        string? shipperName = null;
        if (!string.IsNullOrWhiteSpace(shipperId))
        {
            var shipper = await _userRepository.GetByIdAsync(shipperId);
            if (shipper == null)
                return Fail("Không tìm thấy shipper.", ApiErrorCode.NotFound);
            if (shipper.Role != UserRole.Shipper)
                return Fail("Người dùng được chọn không phải shipper.", ApiErrorCode.Validation);
            shipperName = shipper.FullName;
        }

        var shipping = new Shipping
        {
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            Method = method,
            Fee = 0m,   // REWEAR miễn phí vận chuyển
            Status = ShippingStatus.Pending,
            EstimatedDeliveryDate = CalculateEstimatedDelivery(method),
            Carrier = carrier,
            ShipperId = shipperId,
            ShipperName = shipperName,
            AttemptCount = 0
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

    /// <summary>
    /// Cập nhật trạng thái vận chuyển từ Shipper (hoặc Staff/Admin):
    /// đồng bộ Order.Status, ghi timeline, cập nhật AttemptCount / LastFailureReason...
    /// </summary>
    public async Task<ApiResponse> UpdateShippingStatusAsync(
        string orderId,
        UpdateShippingStatusRequest request,
        string userId,
        UserRole userRole)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        var shipping = await _shippingRepository.GetByOrderIdAsync(orderId);
        if (shipping == null)
            return Fail("Đơn hàng chưa có thông tin vận chuyển.", ApiErrorCode.NotFound);

        // Shipper chỉ được cập nhật đơn của chính mình.
        if (userRole == UserRole.Shipper
            && !string.Equals(shipping.ShipperId, userId, StringComparison.Ordinal))
        {
            return Fail("Bạn không phải shipper phụ trách đơn này.", ApiErrorCode.Forbidden);
        }

        // Parse + validate trạng thái đầu vào.
        if (!Enum.IsDefined(typeof(ShippingStatus), request.Status))
            return Fail("Trạng thái vận chuyển không hợp lệ.", ApiErrorCode.Validation);

        var newShippingStatus = (ShippingStatus)request.Status;
        var newOrderStatus = MapShippingToOrderStatus(newShippingStatus);

        // Nếu trạng thái không đổi thì trả về thành công idempotent.
        if (shipping.Status == newShippingStatus && order.Status == newOrderStatus)
        {
            return new ApiResponse
            {
                Success = true,
                Message = "Trạng thái không thay đổi.",
                Data = BuildShippingResponse(shipping)
            };
        }

        // Lấy tên người thao tác để snapshot timeline.
        var actor = await _userRepository.GetByIdAsync(userId);
        var actorName = actor?.FullName;
        var actorRole = userRole == UserRole.Shipper
            ? OrderStatusChangedBy.Shipper
            : OrderStatusChangedBy.Staff;

        var fromOrderStatus = order.Status;

        // Cập nhật shipping fields.
        shipping.Status = newShippingStatus;
        if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            shipping.TrackingNumber = request.TrackingNumber;
            order.TrackingNumber = request.TrackingNumber;
        }
        if (!string.IsNullOrWhiteSpace(request.Note))
            shipping.Note = request.Note;

        if (newShippingStatus == ShippingStatus.InTransit && shipping.ShippedAt == null)
            shipping.ShippedAt = DateTime.UtcNow;
        if (newShippingStatus == ShippingStatus.Delivered)
            shipping.DeliveredAt = DateTime.UtcNow;

        if (newShippingStatus == ShippingStatus.Failed)
        {
            // Server tự tăng AttemptCount; nếu client truyền AttemptCount thì dùng
            // giá trị lớn hơn giữa client và server+1 (chống rollback gian lận).
            shipping.AttemptCount += 1;
            if (request.AttemptCount.HasValue && request.AttemptCount.Value > shipping.AttemptCount)
                shipping.AttemptCount = request.AttemptCount.Value;
            shipping.LastFailureReason = string.IsNullOrWhiteSpace(request.Note)
                ? "Shipper báo giao thất bại."
                : request.Note;
            shipping.LastFailedAt = DateTime.UtcNow;
        }

        // Cập nhật order status đồng bộ.
        order.Status = newOrderStatus;
        order.UpdatedAt = DateTime.UtcNow;

        // Chốt sản phẩm sang Sold khi giao thành công.
        if (newOrderStatus == OrderStatus.Delivered)
        {
            await CommitDeliveredStockAsync(order);
        }

        await _shippingRepository.UpdateAsync(shipping);
        await _orderRepository.UpdateAsync(order);

        // Ghi timeline (append-only).
        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = fromOrderStatus,
            ToStatus = newOrderStatus,
            Note = request.Note,
            ChangedBy = actorRole,
            ChangedByUserId = userId,
            ChangedByName = actorName
        });

        return new ApiResponse
        {
            Success = true,
            Message = $"Đã cập nhật trạng thái vận chuyển thành \"{newShippingStatus}\".",
            Data = BuildShippingResponse(shipping)
        };
    }

    /// <summary>
    /// Lấy danh sách đơn chờ giao (Admin/Staff):
    /// order Confirmed mà chưa có shipping HOẶC shipping chưa gán shipper cụ thể.
    /// </summary>
    public async Task<ShippingQueueResponse> GetShippingQueueAsync()
    {
        var response = new ShippingQueueResponse();
        var confirmedOrders = await _orderRepository.GetByStatusAsync(OrderStatus.Confirmed);

        foreach (var order in confirmedOrders)
        {
            var shipping = await _shippingRepository.GetByOrderIdAsync(order.Id);
            var buyer = await _userRepository.GetByIdAsync(order.UserId);

            // Chỉ hiện đơn chưa có shipping HOẶC shipping chưa gán shipper.
            if (shipping != null && !string.IsNullOrEmpty(shipping.ShipperId))
                continue;

            response.Items.Add(new ShippingQueueItem
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                BuyerId = order.UserId,
                BuyerName = buyer?.FullName ?? string.Empty,
                TotalAmount = order.TotalAmount,
                CreatedAt = order.CreatedAt,
                HasShipping = shipping != null,
                CurrentShipperId = shipping?.ShipperId,
                CurrentShipperName = shipping?.ShipperName,
                OrderStatus = order.Status.ToString()
            });
        }

        response.TotalCount = response.Items.Count;
        return response;
    }

    /// <summary>
    /// Lấy danh sách đơn của shipper hiện tại (filter theo status nếu có).
    /// Admin/Staff có thể truyền <paramref name="targetShipperId"/> để xem đơn của shipper khác.
    /// </summary>
    public async Task<MyOrdersResponse> GetMyOrdersAsync(
        string currentUserId,
        UserRole currentUserRole,
        ShippingStatus? status,
        string? targetShipperId)
    {
        // Shipper chỉ được xem đơn của chính mình, ignore targetShipperId.
        // Admin/Staff có thể xem của shipper bất kỳ.
        string effectiveShipperId = currentUserRole == UserRole.Shipper
            ? currentUserId
            : (targetShipperId ?? currentUserId);

        if (currentUserRole == UserRole.Shipper
            && !string.Equals(effectiveShipperId, currentUserId, StringComparison.Ordinal))
        {
            effectiveShipperId = currentUserId;
        }

        var shippings = await _shippingRepository.GetByShipperIdAsync(effectiveShipperId, status);

        var response = new MyOrdersResponse
        {
            ShipperId = effectiveShipperId,
            FilterStatus = status
        };

        var shipper = await _userRepository.GetByIdAsync(effectiveShipperId);
        response.ShipperName = shipper?.FullName;

        foreach (var shipping in shippings)
        {
            var order = await _orderRepository.GetByIdAsync(shipping.OrderId);
            if (order == null) continue;
            var buyer = await _userRepository.GetByIdAsync(order.UserId);

            response.Items.Add(new MyOrderItem
            {
                ShippingId = shipping.Id,
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                BuyerId = order.UserId,
                BuyerName = buyer?.FullName ?? string.Empty,
                BuyerPhone = order.ShippingPhoneNumber,
                ShippingAddress = order.FullShippingAddress,
                TotalAmount = order.TotalAmount,
                ShippingStatus = shipping.Status.ToString(),
                OrderStatus = order.Status.ToString(),
                TrackingNumber = shipping.TrackingNumber,
                AttemptCount = shipping.AttemptCount,
                LastFailureReason = shipping.LastFailureReason,
                LastFailedAt = shipping.LastFailedAt,
                CreatedAt = order.CreatedAt,
                ShippedAt = shipping.ShippedAt,
                DeliveredAt = shipping.DeliveredAt,
                EstimatedDeliveryDate = shipping.EstimatedDeliveryDate
            });
        }

        response.TotalCount = response.Items.Count;
        return response;
    }

    /// <summary>
    /// Lấy toàn bộ shipping (AdminOnly, cho dashboard).
    /// </summary>
    public async Task<ShippingListResponse> GetAllAsync(ShippingStatus? status)
    {
        var shippings = await _shippingRepository.GetAllAsync(status);
        return new ShippingListResponse
        {
            Items = shippings.Select(BuildShippingResponse).ToList(),
            TotalCount = shippings.Count,
            FilterStatus = status
        };
    }

    /// <summary>
    /// Admin yêu cầu giao lại sau khi shipper trả hàng về kho do thất bại nhiều lần.
    /// </summary>
    public async Task<ApiResponse> ReshipAsync(
        string orderId,
        ReshipRequest request,
        string adminUserId,
        string adminUserName)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        var shipping = await _shippingRepository.GetByOrderIdAsync(orderId);
        if (shipping == null)
            return Fail("Đơn hàng chưa có thông tin vận chuyển.", ApiErrorCode.NotFound);

        if (order.Status != OrderStatus.Failed || shipping.Status != ShippingStatus.Failed)
            return Fail(
                "Chỉ reship được đơn đang ở trạng thái giao thất bại.",
                ApiErrorCode.Conflict);

        // Cập nhật shipper nếu có yêu cầu đổi.
        if (!string.IsNullOrWhiteSpace(request.ShipperId)
            && !string.Equals(request.ShipperId, shipping.ShipperId, StringComparison.Ordinal))
        {
            var newShipper = await _userRepository.GetByIdAsync(request.ShipperId);
            if (newShipper == null)
                return Fail("Không tìm thấy shipper mới.", ApiErrorCode.NotFound);
            if (newShipper.Role != UserRole.Shipper)
                return Fail("Người dùng được chọn không phải shipper.", ApiErrorCode.Validation);
            shipping.ShipperId = newShipper.Id;
            shipping.ShipperName = newShipper.FullName;
        }

        // Reset trạng thái shipping, GIỮ NGUYÊN AttemptCount (lưu vết).
        shipping.Status = ShippingStatus.Pending;
        shipping.ShippedAt = null;
        shipping.DeliveredAt = null;
        shipping.LastFailedAt = null;
        shipping.LastFailureReason = null;
        shipping.UpdatedAt = DateTime.UtcNow;

        // Order: Failed -> Confirmed.
        order.Status = OrderStatus.Confirmed;
        order.UpdatedAt = DateTime.UtcNow;

        await _shippingRepository.UpdateAsync(shipping);
        await _orderRepository.UpdateAsync(order);

        // Ghi timeline.
        var note = string.IsNullOrWhiteSpace(request.Note)
            ? $"Admin {adminUserName} đã yêu cầu giao lại."
            : $"Admin {adminUserName} đã yêu cầu giao lại. Lý do: {request.Note}";

        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.Failed,
            ToStatus = OrderStatus.Confirmed,
            Note = note,
            ChangedBy = OrderStatusChangedBy.Staff,
            ChangedByUserId = adminUserId,
            ChangedByName = adminUserName
        });

        return new ApiResponse
        {
            Success = true,
            Message = "Đã yêu cầu giao lại đơn hàng.",
            Data = BuildShippingResponse(shipping)
        };
    }

    /// <summary>
    /// Admin hủy vĩnh viễn đơn đã Failed: buyer mất tiền + hàng, sản phẩm vẫn Sold.
    /// </summary>
    public async Task<ApiResponse> CancelPermanentAsync(
        string orderId,
        CancelPermanentRequest request,
        string adminUserId,
        string adminUserName)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
            return Fail("Vui lòng nhập lý do hủy.", ApiErrorCode.Validation);

        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        var shipping = await _shippingRepository.GetByOrderIdAsync(orderId);
        if (shipping == null)
            return Fail("Đơn hàng chưa có thông tin vận chuyển.", ApiErrorCode.NotFound);

        if (order.Status != OrderStatus.Failed || shipping.Status != ShippingStatus.Failed)
            return Fail(
                "Chỉ hủy vĩnh viễn đơn đang ở trạng thái giao thất bại.",
                ApiErrorCode.Conflict);

        // Order: Failed -> Cancelled (lưu lý do).
        order.Status = OrderStatus.Cancelled;
        order.CancelReason = $"Hủy vĩnh viễn: {request.Note}";
        order.UpdatedAt = DateTime.UtcNow;

        // Shipping: Failed -> Returned (sản phẩm vẫn Sold, không refund stock).
        shipping.Status = ShippingStatus.Returned;
        shipping.UpdatedAt = DateTime.UtcNow;

        await _shippingRepository.UpdateAsync(shipping);
        await _orderRepository.UpdateAsync(order);

        // Ghi timeline.
        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.Failed,
            ToStatus = OrderStatus.Cancelled,
            Note = $"Admin {adminUserName} đã hủy vĩnh viễn - {request.Note}",
            ChangedBy = OrderStatusChangedBy.Staff,
            ChangedByUserId = adminUserId,
            ChangedByName = adminUserName
        });

        return new ApiResponse
        {
            Success = true,
            Message = "Đã hủy vĩnh viễn đơn hàng.",
            Data = BuildShippingResponse(shipping)
        };
    }

    // ============================================
    // HELPER
    // ============================================

    /// <summary>
    /// Chốt sản phẩm sang Sold khi đơn giao thành công.
    /// Logic giống OrderService.UpdateStatusAsync để giữ single source of truth.
    /// </summary>
    private async Task CommitDeliveredStockAsync(Order order)
    {
        foreach (var item in order.Items)
        {
            await _productRepository.CommitStockAsync(item.ProductId, item.Quantity);
        }
    }

    /// <summary>
    /// Map trạng thái Shipping sang trạng thái Order tương ứng.
    /// </summary>
    private static OrderStatus MapShippingToOrderStatus(ShippingStatus shipping)
    {
        return shipping switch
        {
            ShippingStatus.Pending => OrderStatus.Confirmed,
            ShippingStatus.InTransit => OrderStatus.Shipping,
            ShippingStatus.Delivered => OrderStatus.Delivered,
            ShippingStatus.Failed => OrderStatus.Failed,
            ShippingStatus.Returned => OrderStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(shipping), shipping, null)
        };
    }

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
            DeliveredAt = s.DeliveredAt,
            ShipperId = s.ShipperId,
            ShipperName = s.ShipperName,
            AttemptCount = s.AttemptCount,
            LastFailureReason = s.LastFailureReason,
            LastFailedAt = s.LastFailedAt
        };
    }

    /// <summary>
    /// Tạo ApiResponse thất bại.
    /// </summary>
    private static ApiResponse Fail(string message, string errorCode = ApiErrorCode.Validation)
        => new ApiResponse { Success = false, Message = message, ErrorCode = errorCode };
}
