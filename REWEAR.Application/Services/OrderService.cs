using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý đơn hàng, checkout và theo dõi trạng thái (Task 5 + Task 6 + Task 7).
/// </summary>
public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICartRepository _cartRepository;
    private readonly IProductRepository _productRepository;
    private readonly IAddressRepository _addressRepository;
    private readonly IOrderStatusHistoryRepository _historyRepository;

    public OrderService(
        IOrderRepository orderRepository,
        ICartRepository cartRepository,
        IProductRepository productRepository,
        IAddressRepository addressRepository,
        IOrderStatusHistoryRepository historyRepository)
    {
        _orderRepository = orderRepository;
        _cartRepository = cartRepository;
        _productRepository = productRepository;
        _addressRepository = addressRepository;
        _historyRepository = historyRepository;
    }

    // ============================================
    // CHECKOUT (Task 5)
    // ============================================

    /// <summary>
    /// Đặt hàng: validate nhóm món đã tick → chốt tồn kho → tạo đơn ở trạng thái
    /// AwaitingPayment → ghi mốc timeline đầu tiên.
    /// </summary>
    /// <remarks>
    /// Việc tạo phiên thanh toán PayOS do <see cref="IPaymentService"/> đảm nhiệm
    /// ngay sau khi đơn được tạo, để tách bạch trách nhiệm và dễ rollback.
    /// </remarks>
    public async Task<ApiResponse> CheckoutAsync(string userId, CheckoutRequest request)
    {
        // ===== 1. VALIDATE ĐỊA CHỈ GIAO HÀNG =====
        if (string.IsNullOrWhiteSpace(request.ShippingAddressId))
            return Fail("Vui lòng chọn địa chỉ giao hàng.", ApiErrorCode.Validation);

        // Chỉ lấy địa chỉ thuộc về chính user này -> chặn đặt hàng bằng địa chỉ người khác.
        var address = await _addressRepository.GetByIdAsync(request.ShippingAddressId);
        if (address == null || address.UserId != userId)
            return Fail("Địa chỉ giao hàng không tồn tại.", ApiErrorCode.NotFound);

        // ===== 2. VALIDATE GIỎ HÀNG (CHỈ NHÓM ĐÃ TICK) =====
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null || cart.Items.Count == 0)
            return Fail("Giỏ hàng đang trống.", ApiErrorCode.Validation);

        // Chỉ các món ĐÃ TICK mới được đưa vào đơn (mô hình giống Shopee):
        // user thêm nhiều món để "để đó" rồi chỉ tick mua một phần.
        var selectedItems = cart.Items.Where(i => i.IsSelected).ToList();

        if (selectedItems.Count == 0)
            return Fail("Chưa chọn sản phẩm nào để thanh toán.", ApiErrorCode.Validation);

        var validation = await ValidateCartAsync(selectedItems);
        if (!validation.IsValid)
        {
            return new ApiResponse
            {
                Success = false,
                Message = "Giỏ hàng có vấn đề, không thể đặt hàng.",
                ErrorCode = ApiErrorCode.Validation,
                Data = new CheckoutErrorResponse
                {
                    Message = "Giỏ hàng có vấn đề, không thể đặt hàng.",
                    Errors = validation.Errors,
                    Warnings = validation.Warnings,
                    ErrorCode = "CART_INVALID"
                }
            };
        }

        // ===== 3. CHỐT TỒN KHO (INVENTORY CHECK) =====
        // Dùng atomic update từng sản phẩm. Nếu 1 sản phẩm bị người khác mua trước
        // thì phải trả lại các sản phẩm đã chốt trước đó (rollback).
        var reservedProductIds = new List<string>();

        foreach (var item in selectedItems)
        {
            long reserved = await _productRepository.TryReserveStockAsync(item.ProductId, item.Quantity);

            if (reserved == 0)
            {
                // Rollback: trả lại những gì đã chốt trong lượt này.
                await RollbackReservedStockAsync(selectedItems, reservedProductIds);

                return new ApiResponse
                {
                    Success = false,
                    Message = "Sản phẩm vừa được người khác mua. Vui lòng kiểm tra lại giỏ hàng.",
                    ErrorCode = ApiErrorCode.Conflict,
                    Data = new CheckoutErrorResponse
                    {
                        Message = "Sản phẩm vừa được người khác mua. Vui lòng kiểm tra lại giỏ hàng.",
                        ProductId = item.ProductId,
                        ErrorCode = "PRODUCT_UNAVAILABLE"
                    }
                };
            }

            reservedProductIds.Add(item.ProductId);
        }

        // ===== 4. TẠO ĐƠN HÀNG =====
        Order order;

        try
        {
            order = await BuildOrder(userId, selectedItems, address, request);
            await _orderRepository.CreateAsync(order);
        }
        catch
        {
            // Tạo đơn lỗi (vd: trùng orderCode) -> trả lại tồn kho, không mất sản phẩm.
            await RollbackReservedStockAsync(selectedItems, reservedProductIds);
            throw;
        }

        // ===== 5. GHI MỐC TIMELINE ĐẦU TIÊN (Task 7) =====
        // Đơn bắt đầu ở AwaitingPayment: sản phẩm đang giữ chỗ chờ khách trả tiền.
        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = null,
            ToStatus = OrderStatus.AwaitingPayment,
            Note = "Đơn hàng đã được tạo, đang chờ thanh toán.",
            ChangedBy = OrderStatusChangedBy.Customer
        });

        // ===== 6. XÓA KHỎI GIỎ CHỈ NHÓM ĐÃ MUA =====
        // Giữ nguyên các món chưa tick (user mua sau) và giữ nguyên trạng thái tick
        // của chúng. Chỉ xóa đúng nhóm vừa tạo đơn.
        var purchasedItemIds = selectedItems.Select(i => i.Id).ToHashSet();
        cart.Items.RemoveAll(i => purchasedItemIds.Contains(i.Id));
        cart.UpdatedAt = DateTime.UtcNow;
        cart.ExpiresAt = DateTime.UtcNow.AddDays(Cart.EXPIRATION_DAYS);
        await _cartRepository.UpdateAsync(cart);

        // ===== 7. TRẢ KẾT QUẢ =====
        var response = await BuildOrderResponseAsync(order);

        return new ApiResponse
        {
            Success = true,
            Message = $"Đặt hàng thành công. Mã đơn: {order.OrderCode}. Vui lòng hoàn tất thanh toán.",
            Data = response
        };
    }

    /// <summary>
    /// Kiểm tra từng mục trong nhóm ĐÃ TICK trước khi đặt hàng:
    /// sản phẩm còn tồn tại, còn khả dụng, còn đủ tồn kho, và có bị đổi giá không.
    /// </summary>
    private async Task<CartValidationResult> ValidateCartAsync(List<CartItem> itemsToBuy)
    {
        var result = new CartValidationResult();
        decimal total = 0m;

        foreach (var item in itemsToBuy)
        {
            var product = await _productRepository.GetByIdAsync(item.ProductId);

            // Sản phẩm đã bị xóa khỏi catalog
            if (product == null)
            {
                result.Errors.Add($"Sản phẩm \"{item.ProductId}\" không còn tồn tại trong hệ thống.");
                continue;
            }

            // Sản phẩm bị gỡ khỏi cửa hàng
            if (!product.IsActive)
            {
                result.Errors.Add($"Sản phẩm \"{product.Title}\" đã bị gỡ khỏi cửa hàng.");
                continue;
            }

            // Sản phẩm đã bán / đang được giữ
            if (product.Status != ProductStatus.Available)
            {
                result.Errors.Add($"Sản phẩm \"{product.Title}\" không còn khả dụng ({(product.Status == ProductStatus.Sold ? "đã bán" : "đang được giữ")}).");
                continue;
            }

            // Không đủ tồn kho
            if (product.StockQuantity < item.Quantity)
            {
                result.Errors.Add($"Sản phẩm \"{product.Title}\" chỉ còn {product.StockQuantity} món, bạn đang mua {item.Quantity} món.");
                continue;
            }

            // Cảnh báo giá đã thay đổi (không chặn, dùng giá mới)
            if (product.Price != item.PriceSnapshot)
            {
                result.Warnings.Add(
                    $"Giá \"{product.Title}\" đã thay đổi từ {item.PriceSnapshot:N0} đ lên {product.Price:N0} đ. "
                    + "Đơn hàng sẽ dùng giá mới nhất.");
            }

            // Tính tổng theo GIÁ HIỆN TẠI, không phải giá snapshot cũ trong giỏ.
            total += product.Price * item.Quantity;
        }

        result.TotalAmount = total;
        result.IsValid = result.Errors.Count == 0;

        return result;
    }

    /// <summary>
    /// Trả lại tồn kho cho các sản phẩm đã chốt nhưng đơn hàng chưa tạo được.
    /// </summary>
    private async Task RollbackReservedStockAsync(List<CartItem> cartItems, List<string> reservedProductIds)
    {
        foreach (var productId in reservedProductIds)
        {
            var item = cartItems.FirstOrDefault(i => i.ProductId == productId);
            int quantity = item?.Quantity ?? 1;

            await _productRepository.ReleaseStockAsync(productId, quantity);
        }
    }

    /// <summary>
    /// Dựng đơn hàng từ giỏ + địa chỉ, snapshot toàn bộ thông tin cần thiết.
    /// </summary>
    private async Task<Order> BuildOrder(
        string userId,
        List<CartItem> itemsToBuy,
        Address address,
        CheckoutRequest request)
    {
        var order = new Order
        {
            OrderCode = Order.GenerateOrderCode(),
            UserId = userId,

            // ===== SNAPSHOT ĐỊA CHỈ =====
            AddressId = address.Id,
            ShippingRecipientName = address.RecipientName,
            ShippingPhoneNumber = address.PhoneNumber,
            ShippingProvince = address.Province,
            ShippingDistrict = address.District,
            ShippingWard = address.Ward,
            ShippingStreetAddress = address.StreetAddress,
            ShippingNote = string.IsNullOrWhiteSpace(request.Note)
                ? address.Note
                : request.Note,

            // REWEAR chỉ có một phương thức thanh toán: trả tiền trước qua PayOS.
            PaymentMethod = PaymentMethod.PayOs,
            Status = OrderStatus.AwaitingPayment
        };

        decimal subTotal = 0m;

        foreach (var item in itemsToBuy)
        {
            var product = await _productRepository.GetByIdAsync(item.ProductId);
            if (product == null) continue;

            // Giá lúc checkout (giá hiện tại), đơn hàng là bản ghi tài chính nên
            // phải giữ nguyên giá đã chốt dù sản phẩm sau này có đổi giá.
            decimal unitPrice = product.Price;

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                UnitPrice = unitPrice,
                ProductTitle = product.Title,
                ProductSlug = product.Slug,
                ProductImageUrl = product.ImageUrls.FirstOrDefault(),
                Size = product.Size,
                Color = product.Color,
                Condition = product.Condition.ToString()
            });

            subTotal += unitPrice * item.Quantity;
        }

        // REWEAR miễn phí vận chuyển toàn bộ: ShippingFee = 0.
        // Voucher (Task 10) sẽ chèn DiscountAmount vào giữa SubTotal và TotalAmount.
        order.SubTotal = subTotal;
        order.DiscountAmount = 0m;
        order.ShippingFee = 0m;
        order.TotalAmount = subTotal - order.DiscountAmount + order.ShippingFee;

        return order;
    }

    // ============================================
    // ORDER MANAGEMENT (Task 6)
    // ============================================

    /// <summary>
    /// Lấy danh sách đơn hàng của người dùng, có phân trang.
    /// </summary>
    public async Task<PagedOrderResponse> GetOrdersByUserAsync(string userId, OrderQueryParameters query)
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize switch
        {
            < 1 => 20,
            > 100 => 100,   // chặn client xin pageSize quá lớn
            _ => query.PageSize
        };

        var (orders, totalCount) = await _orderRepository.GetPagedByUserIdAsync(
            userId, query.Status, page, pageSize);

        var responses = new List<OrderResponse>();
        foreach (var order in orders)
        {
            responses.Add(await BuildOrderResponseAsync(order));
        }

        return new PagedOrderResponse
        {
            Orders = responses,
            TotalCount = (int)totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Lấy chi tiết đơn hàng, có kiểm tra sở hữu (chỉ xem được đơn của chính mình).
    /// </summary>
    public async Task<OrderResponse?> GetOrderDetailAsync(string userId, string orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);

        // Trả về null nếu không tồn tại HOẶC không phải của user này
        // (không phân biệt 2 case để tránh lộ thông tin đơn hàng của người khác).
        if (order == null || order.UserId != userId)
            return null;

        return await BuildOrderResponseAsync(order);
    }

    /// <summary>
    /// Lấy chi tiết đơn hàng bằng mã hiển thị, có kiểm tra sở hữu.
    /// </summary>
    public async Task<OrderResponse?> GetOrderDetailByCodeAsync(string userId, string orderCode)
    {
        var order = await _orderRepository.GetByOrderCodeAsync(orderCode);

        if (order == null || order.UserId != userId)
            return null;

        return await BuildOrderResponseAsync(order);
    }

    /// <summary>
    /// Hủy đơn hàng và trả lại tồn kho cho các sản phẩm.
    /// </summary>
    /// <remarks>
    /// Chỉ hủy được đơn CHƯA thanh toán. Nếu đơn đã thu tiền thì phải hoàn tiền qua
    /// PayOS trước, việc đó thuộc <see cref="IPaymentService.RefundAsync"/> vì cần gọi
    /// API bên ngoài và có thể thất bại giữa chừng.
    /// </remarks>
    public async Task<ApiResponse> CancelOrderAsync(string userId, string orderId, string? reason)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null || order.UserId != userId)
            return FailNotFound("Không tìm thấy đơn hàng.");

        // Đã thu tiền rồi -> không tự hủy, phải đi qua luồng hoàn tiền.
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            return new ApiResponse
            {
                Success = false,
                Message = order.Status switch
                {
                    OrderStatus.Confirmed or OrderStatus.Shipping or OrderStatus.Delivered
                        => "Đơn hàng đã thanh toán và đang được xử lý. Vui lòng liên hệ để được hoàn tiền.",
                    _ => "Đơn hàng không còn ở trạng thái chờ thanh toán."
                },
                ErrorCode = ApiErrorCode.Conflict
            };
        }

        // Trả lại tồn kho cho từng sản phẩm trong đơn.
        foreach (var item in order.Items)
        {
            await _productRepository.ReleaseStockAsync(item.ProductId, item.Quantity);
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelReason = string.IsNullOrWhiteSpace(reason) ? "Người mua hủy đơn." : reason;
        order.UpdatedAt = DateTime.UtcNow;

        await _orderRepository.UpdateAsync(order);

        // Ghi vào timeline (Task 7).
        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.AwaitingPayment,
            ToStatus = OrderStatus.Cancelled,
            Note = order.CancelReason,
            ChangedBy = OrderStatusChangedBy.Customer,
            ChangedByUserId = userId
        });

        var response = await BuildOrderResponseAsync(order);

        return new ApiResponse
        {
            Success = true,
            Message = "Đã hủy đơn hàng.",
            Data = response
        };
    }

    // ============================================
    // ORDER TRACKING (Task 7)
    // ============================================

    /// <summary>
    /// Cập nhật trạng thái đơn và ghi vào timeline.
    /// Chỉ cho phép chuyển trạng thái đi đúng 1 chiều theo sơ đồ bên dưới.
    /// </summary>
    public async Task<ApiResponse> UpdateStatusAsync(
        string orderId, OrderStatus newStatus, string? note,
        string? trackingNumber, DateTime? estimatedDeliveryDate,
        OrderStatusChangedBy changedBy, string? changedByUserId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return FailNotFound("Không tìm thấy đơn hàng.");

        // Đã kết thúc thì không đổi trạng thái được nữa.
        if (order.IsFinalized)
            return Fail("Đơn hàng đã kết thúc, không thể cập nhật trạng thái.", ApiErrorCode.Conflict);

        // Chặn chuyển trạng thái không hợp lệ (vd: Shipping -> Confirmed).
        if (!IsValidTransition(order.Status, newStatus))
        {
            return Fail(
                $"Không thể chuyển đơn từ \"{order.Status}\" sang \"{newStatus}\".",
                ApiErrorCode.Conflict);
        }

        // Chuyển sang Shipping thì bắt buộc có mã vận đơn để khách tra cứu được.
        if (newStatus == OrderStatus.Shipping && string.IsNullOrWhiteSpace(trackingNumber))
            return Fail("Cần nhập mã vận đơn trước khi chuyển sang đang giao.", ApiErrorCode.Validation);

        // Lưu lại trạng thái cũ để ghi timeline.
        var fromStatus = order.Status;

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(trackingNumber))
            order.TrackingNumber = trackingNumber;

        if (estimatedDeliveryDate.HasValue)
            order.EstimatedDeliveryDate = estimatedDeliveryDate;

        // Chốt sản phẩm sang Sold khi giao thành công. Trước đó sản phẩm vẫn giữ ở
        // Reserved để hủy/hoàn tiền còn trả về kho được.
        if (newStatus == OrderStatus.Delivered)
        {
            foreach (var item in order.Items)
            {
                await _productRepository.CommitStockAsync(item.ProductId, item.Quantity);
            }
        }

        await _orderRepository.UpdateAsync(order);

        // Ghi 1 mốc vào timeline (append-only).
        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = fromStatus,
            ToStatus = newStatus,
            Note = note,
            ChangedBy = changedBy,
            ChangedByUserId = changedByUserId
        });

        return new ApiResponse
        {
            Success = true,
            Message = $"Đã cập nhật trạng thái đơn thành \"{newStatus}\".",
            Data = await BuildOrderResponseAsync(order)
        };
    }

    /// <summary>
    /// Sơ đồ chuyển trạng thái hợp lệ (Task 7).
    /// AwaitingPayment → Confirmed → Shipping → Delivered, kèm 2 đường hủy
    /// (khách chủ động hủy / hết hạn thanh toán) chỉ mở khi chưa thu tiền.
    /// </summary>
    private static bool IsValidTransition(OrderStatus from, OrderStatus to)
    {
        return (from, to) switch
        {
            // Chỉ vào Confirmed khi đơn chưa thu tiền; thực tế PaymentService gọi
            // hàm này sau khi đã xác nhận Paid nên không có đường lách.
            (OrderStatus.AwaitingPayment, OrderStatus.Confirmed) => true,
            (OrderStatus.AwaitingPayment, OrderStatus.Cancelled) => true,
            (OrderStatus.AwaitingPayment, OrderStatus.PaymentExpired) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipping) => true,
            (OrderStatus.Shipping, OrderStatus.Delivered) => true,
            _ => false
        };
    }

    /// <summary>
    /// Lấy thông tin theo dõi đơn (mã vận đơn + timeline) cho người mua.
    /// </summary>
    public async Task<OrderTrackingResponse?> GetTrackingAsync(string userId, string orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);

        // Người mua chỉ xem được đơn của chính mình.
        if (order == null || order.UserId != userId)
            return null;

        var histories = await _historyRepository.GetByOrderIdAsync(order.Id);

        // Trạng thái thanh toán suy ra từ trạng thái đơn: chỉ Confirmed trở đi
        // mới chắc chắn đã thu tiền.
        string paymentStatus = order.Status switch
        {
            OrderStatus.AwaitingPayment => "Pending",
            OrderStatus.PaymentExpired => "Expired",
            OrderStatus.Cancelled => "Pending",
            _ => "Paid"
        };

        return new OrderTrackingResponse
        {
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            CurrentStatus = order.Status.ToString(),
            PaymentStatus = paymentStatus,
            TrackingNumber = order.TrackingNumber,
            EstimatedDeliveryDate = order.EstimatedDeliveryDate,
            Timeline = histories.Select(h => new OrderStatusHistoryResponse
            {
                Id = h.Id,
                FromStatus = h.FromStatus?.ToString(),
                ToStatus = h.ToStatus.ToString(),
                Note = h.Note,
                ChangedBy = h.ChangedBy.ToString(),
                CreatedAt = h.CreatedAt
            }).ToList()
        };
    }

    // ============================================
    // HELPER
    // ============================================

    /// <summary>
    /// Chuyển entity Order sang DTO trả về cho frontend.
    /// </summary>
    private async Task<OrderResponse> BuildOrderResponseAsync(Order order)
    {
        var itemResponses = new List<OrderItemResponse>();

        foreach (var item in order.Items)
        {
            // Kiểm tra sản phẩm còn tồn tại trong catalog không (để FE biết
            // nên cho user vào trang chi tiết sản phẩm hay không).
            var product = await _productRepository.GetByIdAsync(item.ProductId);

            itemResponses.Add(new OrderItemResponse
            {
                Id = item.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = item.LineTotal,
                ProductTitle = item.ProductTitle,
                ProductSlug = item.ProductSlug,
                ProductImageUrl = item.ProductImageUrl,
                Size = item.Size,
                Color = item.Color,
                Condition = item.Condition,
                ProductExists = product != null && product.IsActive
            });
        }

        return new OrderResponse
        {
            Id = order.Id,
            OrderCode = order.OrderCode,
            UserId = order.UserId,
            Items = itemResponses,
            ShippingAddress = new OrderShippingAddressResponse
            {
                RecipientName = order.ShippingRecipientName,
                PhoneNumber = order.ShippingPhoneNumber,
                Province = order.ShippingProvince,
                District = order.ShippingDistrict,
                Ward = order.ShippingWard,
                StreetAddress = order.ShippingStreetAddress,
                Note = order.ShippingNote,
                FullAddress = order.FullShippingAddress
            },
            PaymentMethod = order.PaymentMethod.ToString(),
            TotalQuantity = order.TotalQuantity,
            SubTotal = order.SubTotal,
            DiscountAmount = order.DiscountAmount,
            ShippingFee = order.ShippingFee,
            TotalAmount = order.TotalAmount,
            Status = order.Status.ToString(),
            CancelReason = order.CancelReason,
            TrackingNumber = order.TrackingNumber,
            EstimatedDeliveryDate = order.EstimatedDeliveryDate,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            IsFinalized = order.IsFinalized,
            // Chỉ còn ở trạng thái chờ thanh toán thì người mua mới tự hủy được.
            CanCancel = order.Status == OrderStatus.AwaitingPayment
        };
    }

    /// <summary>
    /// Tạo ApiResponse thất bại.
    /// </summary>
    private static ApiResponse Fail(string message, string errorCode = ApiErrorCode.Validation)
        => new ApiResponse { Success = false, Message = message, ErrorCode = errorCode };

    /// <summary>
    /// Tạo ApiResponse thất bại do không tìm thấy tài nguyên.
    /// </summary>
    private static ApiResponse FailNotFound(string message)
        => new ApiResponse { Success = false, Message = message, ErrorCode = ApiErrorCode.NotFound };
}