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
public partial class ShippingService : IShippingService
{
    private const int STANDARD_DAYS_MIN = 3;
    private const int STANDARD_DAYS_MAX = 5;
    private const int EXPRESS_DAYS_MIN = 1;
    private const int EXPRESS_DAYS_MAX = 2;

    private readonly IShippingRepository _shippingRepository;
    private readonly ICommerceUnitOfWork _transactions;
    private readonly IPaymentRepository _payments;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderStatusHistoryRepository _historyRepository;
    private readonly IUserRepository _userRepository;
    private readonly IProductRepository _productRepository;
    private readonly ILifecycleStore _lifecycle;

    public ShippingService(
        IShippingRepository shippingRepository,
        IOrderRepository orderRepository,
        IOrderStatusHistoryRepository historyRepository,
        IUserRepository userRepository,
        IProductRepository productRepository, ICommerceUnitOfWork transactions, IPaymentRepository payments, ILifecycleStore lifecycle)
    {
        _shippingRepository = shippingRepository;
        _transactions = transactions;
        _payments = payments;
        _lifecycle = lifecycle;
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
    public Task<ApiResponse> CreateShippingAsync(string orderId, string? shipperId, ShippingMethod method, string? carrier, string? actorId = null) =>
        _transactions.ExecuteAsync(() => CreateShippingCoreAsync(orderId, shipperId, method, carrier, actorId));

    private async Task<ApiResponse> CreateShippingCoreAsync(
        string orderId,
        string? shipperId,
        ShippingMethod method,
        string? carrier, string? actorId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        var payment = await _payments.GetLatestByOrderIdAsync(orderId);
        if (order.Status != OrderStatus.Confirmed || payment?.Status != PaymentStatus.Paid || payment.RequiresReconciliation || payment.RefundState == "Requested")
            return Fail("Đơn chưa đủ điều kiện giao hàng hoặc đang chờ hoàn tiền.", ApiErrorCode.Conflict);
        if (string.IsNullOrWhiteSpace(shipperId)) return Fail("Cần phân công người giao hàng.", ApiErrorCode.Validation);
        method = order.ShippingMethod;

        // Đã có thông tin vận chuyển thì không tạo lại (tránh nhân đôi).
        var existing = await _shippingRepository.GetByOrderIdAsync(orderId);


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

        if (existing != null)
        {
            if (existing.ShipperId == shipperId) return new ApiResponse { Success = true, Data = BuildShippingResponse(existing), Message = "Đã phân công người giao hàng này." };
            if (existing.Status != ShippingStatus.Pending || existing.PickedUpAt.HasValue) return Fail("Chỉ đổi phân công trước khi đã lấy hàng.", ApiErrorCode.Conflict);
            existing.ShipperId = shipperId; existing.ShipperName = shipperName; existing.AssignedAt = DateTime.UtcNow;
            await _shippingRepository.UpdateAsync(existing);
            await AssignmentHistoryAsync(order, existing, "Reassigned", actorId);
            return new ApiResponse { Success = true, Data = BuildShippingResponse(existing), Message = "Đã cập nhật phân công." };
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
            AttemptCount = 0, AssignedAt = DateTime.UtcNow, RequiresPickupConfirmation = true
        };

        await _shippingRepository.CreateAsync(shipping);
        await AssignmentHistoryAsync(order, shipping, "Assigned", actorId);

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
    public Task<ApiResponse> UpdateShippingAsync(string orderId, UpdateShippingRequest request) => _transactions.ExecuteAsync(() => UpdateShippingCoreAsync(orderId, request));

    private async Task<ApiResponse> UpdateShippingCoreAsync(string orderId, UpdateShippingRequest request)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        var shipping = await _shippingRepository.GetByOrderIdAsync(orderId);
        if (shipping == null)
            return Fail("Đơn hàng chưa có thông tin vận chuyển.", ApiErrorCode.NotFound);

        if (shipping.Status is not (ShippingStatus.Pending or ShippingStatus.PickedUp or ShippingStatus.InTransit)
            || order.Status is not (OrderStatus.Confirmed or OrderStatus.Shipping))
            return Fail("Chỉ sửa thông tin của chuyến giao đang hoạt động.", ApiErrorCode.Conflict);
        var payment = await _payments.GetLatestByOrderIdAsync(orderId);
        if (payment?.Status != PaymentStatus.Paid || payment.RequiresReconciliation || payment.RefundState == "Requested")
            return Fail("Thanh toán đang được xử lý; chưa thể cập nhật chuyến giao.", ApiErrorCode.Conflict);

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

        // Metadata changes never move the shipment. Use the guarded status endpoint.

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



        var shipping = await _shippingRepository.GetByOrderIdAsync(orderId);
        if (shipping == null)
            return null;

        bool internalView = false;
        if (userId != null && order.UserId != userId)
        {
            var actor = await _userRepository.GetByIdAsync(userId);
            if (actor == null || (actor.Role is not (UserRole.Admin or UserRole.Staff) && !(actor.Role == UserRole.Shipper && shipping.ShipperId == userId))) return null;
            internalView = true;
        }
        return await BuildDeliveryAsync(shipping, order, internalView);
    }

    /// <summary>
    /// Cập nhật trạng thái vận chuyển từ Shipper (hoặc Staff/Admin):
    /// đồng bộ Order.Status, ghi timeline, cập nhật AttemptCount / LastFailureReason...
    /// </summary>
    public Task<ApiResponse> UpdateShippingStatusAsync(string orderId, UpdateShippingStatusRequest request, string userId, UserRole userRole) =>
        _transactions.ExecuteAsync(() => UpdateShippingStatusCoreAsync(orderId, request, userId, userRole));

    private async Task<ApiResponse> UpdateShippingStatusCoreAsync(
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

        if (userRole != UserRole.Shipper) return Fail("Bạn không có quyền cập nhật giao hàng.", ApiErrorCode.Forbidden);
        var payment = await _payments.GetLatestByOrderIdAsync(orderId);
        if (payment?.Status != PaymentStatus.Paid || payment.RequiresReconciliation || payment.RefundState == "Requested")
            return Fail("Đơn chưa đủ điều kiện giao hàng.", ApiErrorCode.Conflict);

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

        bool allowed = (shipping.Status, newShippingStatus) switch
        {
            (ShippingStatus.Pending, ShippingStatus.PickedUp) => order.Status == OrderStatus.Confirmed,
            (ShippingStatus.PickedUp, ShippingStatus.InTransit) => order.Status == OrderStatus.Confirmed,
            (ShippingStatus.Pending, ShippingStatus.InTransit) => !shipping.RequiresPickupConfirmation && order.Status == OrderStatus.Confirmed,
            (ShippingStatus.InTransit, ShippingStatus.Delivered or ShippingStatus.Failed) => order.Status == OrderStatus.Shipping,
            _ => false
        };
        if (!allowed) return Fail("Không thể chuyển trạng thái vận chuyển theo bước này.", ApiErrorCode.Conflict);
        if (newShippingStatus == ShippingStatus.InTransit && string.IsNullOrWhiteSpace(request.TrackingNumber ?? shipping.TrackingNumber))
            return Fail("Cần mã vận đơn trước khi giao.", ApiErrorCode.Validation);
        if (newShippingStatus == ShippingStatus.Failed && !string.IsNullOrWhiteSpace(request.FailureCode) && !DeliveryHistoryMapping.FailureReasons.ContainsKey(request.FailureCode)) return Fail("Lý do giao thất bại không hợp lệ.", ApiErrorCode.Validation);
        if (newShippingStatus == ShippingStatus.Failed && (string.IsNullOrWhiteSpace(request.FailureCode) || request.FailureCode == "Other") && string.IsNullOrWhiteSpace(request.Note))
            return Fail("Cần ghi lý do giao thất bại.", ApiErrorCode.Validation);

        // Lấy tên người thao tác để snapshot timeline.
        var actor = await _userRepository.GetByIdAsync(userId);
        if (actor?.Role != UserRole.Shipper) return Fail("Tài khoản giao hàng không còn hợp lệ.", ApiErrorCode.Forbidden);
        var actorName = actor.FullName;
        var actorRole = userRole == UserRole.Shipper
            ? OrderStatusChangedBy.Shipper
            : OrderStatusChangedBy.Staff;

        var fromOrderStatus = order.Status;
        var previousShippingStatus = shipping.Status;

        // Cập nhật shipping fields.
        shipping.Status = newShippingStatus;
        if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            shipping.TrackingNumber = request.TrackingNumber;
            order.TrackingNumber = request.TrackingNumber;
        }
        if (!string.IsNullOrWhiteSpace(request.Note))
            shipping.Note = request.Note;

        if (newShippingStatus == ShippingStatus.PickedUp && shipping.PickedUpAt == null) shipping.PickedUpAt = DateTime.UtcNow;
        if (newShippingStatus == ShippingStatus.InTransit && shipping.ShippedAt == null)
            shipping.ShippedAt = DateTime.UtcNow;
        if (newShippingStatus == ShippingStatus.Delivered)
            shipping.DeliveredAt = DateTime.UtcNow;
        if (newShippingStatus is ShippingStatus.PickedUp or ShippingStatus.InTransit or ShippingStatus.Delivered)
            await MoveCustodyAsync(order, newShippingStatus == ShippingStatus.Delivered ? "Buyer" : "Shipper", userId);

        if (newShippingStatus == ShippingStatus.Failed)
        {
            // Số lần giao thất bại chỉ do server tăng, bỏ qua giá trị do client gửi.
            shipping.AttemptCount += 1;
            shipping.LastFailureCode = string.IsNullOrWhiteSpace(request.FailureCode) ? "Other" : request.FailureCode;
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
            EventType = newShippingStatus == ShippingStatus.Failed ? "DeliveryFailed" : newShippingStatus.ToString(),
            ShipmentId = shipping.Id, AssignedShipperId = shipping.ShipperId,
            FromShippingStatus = previousShippingStatus.ToString(), ToShippingStatus = newShippingStatus.ToString(),
            CustomerMessage = newShippingStatus == ShippingStatus.Failed ? DeliveryHistoryMapping.FailureReasons[shipping.LastFailureCode ?? "Other"] : null,
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
            var payment = await _payments.GetLatestByOrderIdAsync(order.Id);
            if (payment?.Status != PaymentStatus.Paid || payment.RequiresReconciliation || payment.RefundState == "Requested") continue;
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
                BuyerName = order.ShippingRecipientName, RecipientName = order.ShippingRecipientName, ShippingAddress = order.FullShippingAddress, TotalQuantity = order.TotalQuantity,
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
                BuyerId = currentUserRole == UserRole.Shipper ? "" : order.UserId,
                BuyerName = order.ShippingRecipientName,
                BuyerPhone = order.ShippingPhoneNumber,
                ShippingAddress = order.FullShippingAddress,
                TotalAmount = order.TotalAmount,
                Method = shipping.Method.ToString(), PaymentStatus = (await _payments.GetLatestByOrderIdAsync(order.Id))?.Status.ToString() ?? "Pending",
                TotalQuantity = order.TotalQuantity, AssignedAt = shipping.AssignedAt, PickedUpAt = shipping.PickedUpAt,
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
        var items = new List<ShippingResponse>();
        foreach (var shipment in shippings)
        { var order = await _orderRepository.GetByIdAsync(shipment.OrderId); if (order != null) items.Add(await BuildDeliveryAsync(shipment, order, true)); }
        return new ShippingListResponse { Items = items, TotalCount = items.Count, FilterStatus = status };
    }

    /// <summary>
    /// Admin yêu cầu giao lại sau khi shipper trả hàng về kho do thất bại nhiều lần.
    /// </summary>
    public Task<ApiResponse> ReshipAsync(string orderId, ReshipRequest request, string adminUserId, string adminUserName) =>
        _transactions.ExecuteAsync(() => ReshipCoreAsync(orderId, request, adminUserId, adminUserName));

    private async Task<ApiResponse> ReshipCoreAsync(
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

        var payment = await _payments.GetLatestByOrderIdAsync(orderId);
        if (payment?.Status != PaymentStatus.Paid || payment.RequiresReconciliation || payment.RefundState == "Requested")
            return Fail("Thanh toán hoặc hàng hoàn đang được xử lý; không thể giao lại.", ApiErrorCode.Conflict);

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
        shipping.AssignedAt = DateTime.UtcNow; shipping.PickedUpAt = null; shipping.RequiresPickupConfirmation = true;
        shipping.ShippedAt = null;
        shipping.DeliveredAt = null;
        shipping.LastFailedAt = null;
        shipping.LastFailureReason = null;
        shipping.LastFailureCode = null;
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
            EventType = "RedeliveryRequested", ShipmentId = shipping.Id, AssignedShipperId = shipping.ShipperId, FromShippingStatus = "Failed", ToShippingStatus = "Pending",
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
    public Task<ApiResponse> CancelPermanentAsync(string orderId, CancelPermanentRequest request, string adminUserId, string adminUserName) =>
        Task.FromResult(Fail("Cần tiếp nhận hàng hoàn, kiểm định và xử lý hoàn tiền; không được hủy để giữ cả tiền và hàng.", ApiErrorCode.Conflict));

    // ============================================
    // HELPER
    // ============================================

    /// <summary>
    /// Chốt sản phẩm sang Sold khi đơn giao thành công.
    /// Logic giống OrderService.UpdateStatusAsync để giữ single source of truth.
    /// </summary>
    private async Task MoveCustodyAsync(Order order, string target, string actor)
    {
        foreach (var line in order.Items)
        foreach (var item in await _lifecycle.FindAsync<InventoryItem>(i => i.ProductId == line.ProductId && i.State != "Consumed"))
        {
            if (item.Custody == target) continue;
            var from = item.Custody; item.Custody = target;
            item.Location = target == "Shipper" ? "Người giao hàng đang quản lý" : "Đã giao người mua";
            await _lifecycle.SaveAsync(item);
            await _lifecycle.InsertAsync(new InventoryMovement { InventoryId = item.Id, FromState = item.State, ToState = item.State,
                FromCustody = from, ToCustody = target, ActorId = actor, RelatedId = order.Id,
                Reason = target == "Shipper" ? "Bàn giao hàng cho người giao hàng." : "Xác nhận đã giao hàng cho người mua." });
        }
    }

    private async Task CommitDeliveredStockAsync(Order order)
    {
        foreach (var item in order.Items)
        {
            await _productRepository.CommitStockAsync(item.ProductId, item.Quantity, order.Id);
        }
    }

    /// <summary>
    /// Map trạng thái Shipping sang trạng thái Order tương ứng.
    /// </summary>
    private static OrderStatus MapShippingToOrderStatus(ShippingStatus shipping)
    {
        return shipping switch
        {
            ShippingStatus.Pending or ShippingStatus.PickedUp => OrderStatus.Confirmed,
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
            AssignedAt = s.AssignedAt, PickedUpAt = s.PickedUpAt,
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
