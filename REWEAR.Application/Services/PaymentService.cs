using Microsoft.Extensions.Logging;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;
/// <summary>
/// Service xử lý thanh toán trả trước qua PayOS (Task 8).
/// </summary>
/// <remarks>
/// Mô hình REWEAR: khách phải trả tiền trước thì mới mua được hàng. Mỗi lần checkout
/// tạo 1 phiên thanh toán có thời hạn; sản phẩm được giữ chỗ trong thời gian đó.
/// </remarks>
public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderStatusHistoryRepository _historyRepository;
    private readonly IProductRepository _productRepository;
    private readonly IPayOsService _payOsService;
    private readonly IOrderService _orderService;
    private readonly IPaymentGatewayConfig _gatewayConfig;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        IOrderStatusHistoryRepository historyRepository,
        IProductRepository productRepository,
        IPayOsService payOsService,
        IOrderService orderService,
        IPaymentGatewayConfig gatewayConfig,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _historyRepository = historyRepository;
        _productRepository = productRepository;
        _payOsService = payOsService;
        _orderService = orderService;
        _gatewayConfig = gatewayConfig;
        _logger = logger;
    }

    /// <summary>
    /// Tạo phiên thanh toán cho đơn và gọi PayOS để lấy trang thanh toán.
    /// </summary>
    /// <param name="orderId">Id của đơn hàng cần tạo phiên thanh toán.</param>
    /// <param name="expirationMinutes">Số phút khách được giữ chỗ sản phẩm (mặc định 15).</param>
    public async Task<(ApiResponse Response, CheckoutSessionResponse? Session)> CreateSessionAsync(
        string orderId, int expirationMinutes = 15)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return (Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound), null);

        // Chỉ đơn đang chờ thanh toán mới tạo được phiên mới.
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            return (Fail(
                "Đơn hàng không còn ở trạng thái chờ thanh toán.",
                ApiErrorCode.Conflict), null);
        }

        if (expirationMinutes < 5 || expirationMinutes > 60)
            expirationMinutes = 15;

        DateTime expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

        // Đóng phiên cũ (nếu có) trước khi tạo phiên mới để polling không nhầm
        // vào phiên đã hết hạn.
        var previous = await _paymentRepository.GetLatestByOrderIdAsync(orderId);
        if (previous != null && previous.Status == PaymentStatus.Pending)
        {
            previous.Status = PaymentStatus.Expired;
            previous.UpdatedAt = DateTime.UtcNow;
            await _paymentRepository.UpdateAsync(previous);
        }

        var payment = new Payment
        {
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            UserId = order.UserId,
            Method = PaymentMethod.PayOs,
            Amount = order.TotalAmount,
            Status = PaymentStatus.Pending,
            ExpiresAt = expiresAt
        };

        // Tạo payment request trên PayOS. Nếu PayOS từ chối (mạng lỗi, key sai...)
        // thì phải trả sản phẩm về kho, nếu không sẽ kẹt hàng vô thời gian.
        //
        // returnUrl / cancelUrl: PayOS redirect về đây khi khách đóng trang thanh
        // toán. Cả 2 đều trỏ về cùng trang "đợi thanh toán" của FE, kèm orderCode
        // để FE tự polling GET /api/payments/{id}/status. Không phụ thuộc webhook
        // nữa nên returnUrl chỉ cần đưa khách về UI polling.
        var result = await _payOsService.CreatePaymentRequestAsync(
            order.OrderCode, order.TotalAmount,
            returnUrl: $"{GetBaseUrl()}/payment/waiting?orderCode={order.OrderCode}",
            cancelUrl: $"{GetBaseUrl()}/payment/waiting?orderCode={order.OrderCode}&cancelled=1",
            expiredAt: expiresAt);

        if (result == null)
        {
            _logger.LogError("Khong tao duoc phien thanh toan PayOS cho don {OrderCode}", order.OrderCode);
            await ReleaseOrderStockAsync(order);

            return (new ApiResponse
            {
                Success = false,
                Message = "Không tạo được phiên thanh toán. Vui lòng thử lại sau.",
                ErrorCode = ApiErrorCode.Conflict
            }, null);
        }

        payment.PayOsTransactionId = result.PaymentLinkId;
        payment.PaymentLinkId = result.PaymentLinkId;
        payment.CheckoutUrl = result.CheckoutUrl;
        payment.QrCodeUrl = result.QrCode;

        await _paymentRepository.CreateAsync(payment);

        return (new ApiResponse { Success = true, Message = "Tạo phiên thanh toán thành công." },
            new CheckoutSessionResponse
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                TotalAmount = order.TotalAmount,
                CheckoutUrl = result.CheckoutUrl,
                QrCode = result.QrCode,
                PaymentLinkId = result.PaymentLinkId,
                ExpiresAt = expiresAt,
                ExpiresInSeconds = expirationMinutes * 60
            });
    }

    /// <summary>
    /// Xử lý webhook "thanh toán thành công" từ PayOS.
    /// </summary>
    public async Task<ApiResponse> HandleWebhookSuccessAsync(
        string orderCode, string transactionId, decimal amount)
    {
        var order = await _orderRepository.GetByOrderCodeAsync(orderCode);
        if (order == null)
        {
            _logger.LogWarning("Webhook thanh toan den voi ma don khong ton tai: {OrderCode}", orderCode);
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);
        }

        var payment = await _paymentRepository.GetLatestByOrderIdAsync(order.Id);
        if (payment == null)
        {
            _logger.LogWarning("Webhook thanh toan den ma khong co phien thanh toan: {OrderCode}", orderCode);
            return Fail("Không tìm thấy phiên thanh toán.", ApiErrorCode.NotFound);
        }

        // ===== IDEMPOTENCY =====
        // PayOS gọi webhook nhiều lần cho cùng 1 giao dịch. Nếu đã Paid thì trả về
        // thành công ngay, không ghi timeline 2 lần / không gửi mail 2 lần.
        if (payment.Status == PaymentStatus.Paid)
        {
            return new ApiResponse { Success = true, Message = "Giao dịch đã được xử lý trước đó." };
        }

        // Đơn đã bị hủy hoặc hết hạn -> không nhận tiền vào đơn này nữa.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.PaymentExpired)
        {
            _logger.LogWarning(
                "Webhook thanh toan den cho don {OrderCode} da huy/het han (status {Status}). Can hoan tien thu cong.",
                orderCode, order.Status);
            return Fail("Đơn hàng đã bị hủy hoặc hết hạn.", ApiErrorCode.Conflict);
        }

        // Kiểm tra số tiền khớp. Lệch là dấu hiệu giao dịch không hợp lệ -> không tự
        // động chấp nhận, để nhân viên đối soát.
        if (amount != 0 && Math.Abs((decimal)amount - payment.Amount) > 0.01m)
        {
            _logger.LogError(
                "So tien khong khop cho don {OrderCode}. Yeu cau: {Expected}, thuc te: {Actual}",
                orderCode, payment.Amount, amount);
            return Fail("Số tiền thanh toán không khớp với đơn hàng.", ApiErrorCode.Conflict);
        }

        // ===== CHỐT THANH TOÁN =====
        payment.Status = PaymentStatus.Paid;
        payment.PayOsTransactionId = transactionId;
        payment.ReceivedAmount = amount;
        payment.PaidAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;

        await _paymentRepository.UpdateAsync(payment);

        // ✅ Ghi log rõ ràng để audit trail khi có tranh chấp.
        _logger.LogInformation(
            "PAYMENT SUCCESS: don {OrderCode}, amount={Amount}, transactionId={TransactionId}",
            order.OrderCode, amount, transactionId);

        // Chuyển đơn sang Confirmed. Sản phẩm VẪN giữ ở Reserved: chỉ khi giao
        // thành công mới chốt Sold, để việc hủy/hoàn tiền còn trả kho được.
        await _orderService.UpdateStatusAsync(
            order.Id, OrderStatus.Confirmed,
            note: $"Đã nhận thanh toán {amount:N0} đ qua PayOS.",
            trackingNumber: null, estimatedDeliveryDate: null,
            changedBy: OrderStatusChangedBy.System, changedByUserId: null);

        return new ApiResponse
        {
            Success = true,
            Message = "Đã ghi nhận thanh toán thành công."
        };
    }

    /// <summary>
    /// Xử lý webhook "thanh toán thất bại / bị hủy" từ PayOS.
    /// </summary>
    public async Task<ApiResponse> HandleWebhookFailedAsync(string orderCode, string? message)
    {
        var order = await _orderRepository.GetByOrderCodeAsync(orderCode);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        // Đơn đã thu tiền rồi thì webhook "failed" là báo sai -> bỏ qua.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.PaymentExpired
            or OrderStatus.Confirmed or OrderStatus.Shipping or OrderStatus.Delivered)
        {
            return new ApiResponse { Success = true, Message = "Bỏ qua: đơn đã xử lý." };
        }

        var payment = await _paymentRepository.GetLatestByOrderIdAsync(order.Id);
        if (payment != null && payment.Status == PaymentStatus.Pending)
        {
            payment.Status = PaymentStatus.Failed;
            payment.Note = message;
            payment.UpdatedAt = DateTime.UtcNow;
            await _paymentRepository.UpdateAsync(payment);
        }

        // Trả sản phẩm về kho vì khách không mua.
        foreach (var item in order.Items)
        {
            await _productRepository.ReleaseStockAsync(item.ProductId, item.Quantity);
        }

        order.Status = OrderStatus.PaymentExpired;
        order.CancelReason = string.IsNullOrWhiteSpace(message)
            ? "Thanh toán không thành công."
            : message;
        order.UpdatedAt = DateTime.UtcNow;
        await _orderRepository.UpdateAsync(order);

        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.AwaitingPayment,
            ToStatus = OrderStatus.PaymentExpired,
            Note = order.CancelReason,
            ChangedBy = OrderStatusChangedBy.System
        });

        return new ApiResponse { Success = true, Message = "Đã xử lý thanh toán thất bại." };
    }

    /// <summary>
    /// Tra cứu trạng thái thanh toán của đơn.
    /// </summary>
    /// <remarks>
    /// Polling thay cho webhook: cứ Pending là gọi thẳng PayOS server để hỏi.
    /// PayOS trả về PAID/CANCELLED/EXPIRED thì cập nhật DB luôn. Idempotent:
    /// gọi nhiều lần cũng chỉ chốt đơn 1 lần.
    /// </remarks>
    public async Task<PaymentStatusResponse?> GetStatusAsync(string orderId, string? userId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return null;

        if (userId != null && order.UserId != userId)
            return null;

        var payment = await _paymentRepository.GetLatestByOrderIdAsync(orderId);
        if (payment == null)
            return null;

        // ===== ĐỐI SOÁT CHỦ ĐỘNG =====
        // Phiên vẫn Pending: hỏi thẳng PayOS trạng thái thật của giao dịch.
        // Đây là cách backend phát hiện khách đã trả tiền khi không có webhook.
        if (payment.Status == PaymentStatus.Pending
            && !string.IsNullOrWhiteSpace(payment.PayOsTransactionId))
        {
            if (await ReconcileWithPayOsAsync(payment, order))
            {
                // Đối soát đã chốt được giao dịch -> đọc lại từ DB để trả về
                // đúng trạng thái mới nhất, tránh báo "chờ thanh toán" khi
                // khách thực ra đã trả.
                var updatedPayment = await _paymentRepository.GetLatestByOrderIdAsync(orderId);
                var updatedOrder = await _orderRepository.GetByIdAsync(orderId);

                if (updatedPayment != null) payment = updatedPayment;
                if (updatedOrder != null) order = updatedOrder;
            }
        }

        int remainingSeconds = (int)Math.Max(
            0, (payment.ExpiresAt - DateTime.UtcNow).TotalSeconds);

        return new PaymentStatusResponse
        {
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            PaymentStatus = payment.Status.ToString(),
            OrderStatus = order.Status.ToString(),
            IsPaid = payment.Status == PaymentStatus.Paid,
            IsExpired = payment.Status == PaymentStatus.Expired
                || DateTime.UtcNow > payment.ExpiresAt,
            ExpiresInSeconds = payment.Status == PaymentStatus.Pending ? remainingSeconds : 0
        };
    }

    /// <summary>
    /// Hỏi PayOS về trạng thái thật của giao dịch và cập nhật nếu cần.
    /// </summary>
    /// <remarks>
    /// Xử lý cả 3 trạng thái kết thúc:
    /// - PAID: chốt thanh toán, chuyển đơn sang Confirmed.
    /// - CANCELLED: khách bấm huỷ, trả kho + chuyển đơn sang PaymentExpired.
    /// - EXPIRED: phiên hết hạn trên PayOS, trả kho + chuyển đơn sang PaymentExpired.
    /// </remarks>
    /// <returns>True nếu giao dịch đã được chốt (PAID hoặc CANCELLED/EXPIRED).</returns>
    private async Task<bool> ReconcileWithPayOsAsync(Payment payment, Order order)
    {
        var info = await _payOsService.GetTransactionAsync(payment.PayOsTransactionId!);
        if (info == null)
            return false;

        if (string.Equals(info.Status, "PAID", StringComparison.OrdinalIgnoreCase)
            || (info.PaidAmount > 0 && info.PaidAmount >= payment.Amount))
        {
            // ✅ Trường hợp quan trọng: PayOS trả EXPIRED/CANCELLED nhưng thực tế
            // paidAmount > 0 nghĩa là khách ĐÃ TRẢ TIỀN. Phải chốt đơn PAID ngay.
            if (!string.Equals(info.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "PayOS tra ve status={Status} nhung paidAmount={Amount} > 0 cho don {OrderCode}. " +
                    "Xu ly nhu PAID de tranh mat tien khach.",
                    info.Status, info.PaidAmount, order.OrderCode);
            }
            else
            {
                _logger.LogInformation(
                    "Doi soat thanh cong don {OrderCode}: PayOS da ghi nhan tien.",
                    order.OrderCode);
            }

            var result = await HandleWebhookSuccessAsync(
                order.OrderCode, info.Id, info.PaidAmount);

            return result.Success;
        }

        // Khách huỷ hoặc phiên hết hạn trên PayOS (chưa nhận tiền).
        // Xử lý luôn để FE không phải đợi background service chạy.
        if (string.Equals(info.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(info.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Doi soat phien {OrderCode} da dong ben PayOS (status={Status}).",
                order.OrderCode, info.Status);

            var result = await HandleWebhookFailedAsync(
                order.OrderCode,
                info.Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)
                    ? "Khách huỷ thanh toán."
                    : "Phiên thanh toán đã hết hạn.");

            return result.Success;
        }

        return false;
    }

    /// <summary>
    /// Đối soát chủ động với PayOS: gọi thẳng server PayOS để xác minh trạng thái
    /// thanh toán và cập nhật DB nếu có thay đổi. Đây là cơ chế thay thế webhook
    /// trong kiến trúc không dùng Cloudflare Tunnel/Worker.
    /// </summary>
    /// <remarks>
    /// Flow:
    /// 1. Lấy order + payment gần nhất trong DB.
    /// 2. Nếu đơn đã ở trạng thái kết thúc (Confirmed/Cancelled/PaymentExpired): trả về status hiện tại.
    /// 3. Nếu chưa có paymentLinkId (chưa từng tạo phiên PayOS): trả về status hiện tại.
    /// 4. Gọi PayOS GET /v2/payment-requests/{id}.
    /// 5. Nếu PAID: gọi HandleWebhookSuccessAsync để chốt thanh toán.
    /// 6. Nếu CANCELLED/EXPIRED: gọi HandleWebhookFailedAsync để trả kho + hủy đơn.
    /// 7. Trả về status mới nhất sau khi đối soát.
    /// </remarks>
    public async Task<PaymentStatusResponse?> ReconcileAsync(string orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
        {
            _logger.LogWarning("Reconcile: khong tim thay don {OrderId}", orderId);
            return null;
        }

        // Đơn đã chốt rồi thì không cần đối soát nữa.
        if (order.Status is OrderStatus.Confirmed
            or OrderStatus.Shipping
            or OrderStatus.Delivered
            or OrderStatus.Cancelled
            or OrderStatus.PaymentExpired)
        {
            return new PaymentStatusResponse
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                PaymentStatus = "Paid",
                OrderStatus = order.Status.ToString(),
                IsPaid = order.Status is OrderStatus.Confirmed
                    or OrderStatus.Shipping
                    or OrderStatus.Delivered,
                IsExpired = order.Status == OrderStatus.PaymentExpired,
                ExpiresInSeconds = 0
            };
        }

        var payment = await _paymentRepository.GetLatestByOrderIdAsync(orderId);
        if (payment == null || string.IsNullOrWhiteSpace(payment.PayOsTransactionId))
        {
            _logger.LogWarning(
                "Reconcile: don {OrderCode} chua co phien PayOS de doi soat.",
                order.OrderCode);
            return new PaymentStatusResponse
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                PaymentStatus = "Pending",
                OrderStatus = order.Status.ToString(),
                IsPaid = false,
                IsExpired = false,
                ExpiresInSeconds = (int)Math.Max(0d,
                    ((payment?.ExpiresAt ?? DateTime.UtcNow) - DateTime.UtcNow).TotalSeconds)
            };
        }

        // Gọi PayOS lấy trạng thái thật.
        _logger.LogInformation(
            "Reconcile: dang hoi PayOS ve don {OrderCode} (paymentLinkId={PaymentLinkId})",
            order.OrderCode, payment.PayOsTransactionId);

        var info = await _payOsService.GetTransactionAsync(payment.PayOsTransactionId);
        if (info == null)
        {
            _logger.LogError(
                "Reconcile: khong goi duoc PayOS cho don {OrderCode}",
                order.OrderCode);
            return null;
        }

        _logger.LogInformation(
            "Reconcile: PayOS tra ve don {OrderCode} status={Status}, amountPaid={Amount}",
            order.OrderCode, info.Status, info.PaidAmount);

        // Xử lý theo status từ PayOS.
        // ✅ QUAN TRỌNG: ưu tiên PAID khi paidAmount > 0, kể cả khi status là EXPIRED/CANCELLED
        // (PayOS đôi khi trả EXPIRED nhưng vẫn ghi nhận tiền trong cùng session).
        // Tính amount đang chờ để so sánh.
        decimal expectedAmount = payment.Amount;
        if (string.Equals(info.Status, "PAID", StringComparison.OrdinalIgnoreCase)
            || (info.PaidAmount > 0 && info.PaidAmount >= expectedAmount))
        {
            if (!string.Equals(info.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Reconcile: PayOS status={Status} nhung paidAmount={Amount} cho don {OrderCode}. " +
                    "Xu ly nhu PAID.",
                    info.Status, info.PaidAmount, order.OrderCode);
            }

            var result = await HandleWebhookSuccessAsync(
                order.OrderCode, info.Id, info.PaidAmount);
            if (!result.Success)
            {
                _logger.LogError(
                    "Reconcile: HandleWebhookSuccess that bai cho don {OrderCode}: {Message}",
                    order.OrderCode, result.Message);
            }
        }
        else if (string.Equals(info.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(info.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            var reason = string.Equals(info.Status, "CANCELLED",
                StringComparison.OrdinalIgnoreCase)
                    ? "Khách huỷ thanh toán."
                    : "Phiên thanh toán đã hết hạn.";

            var result = await HandleWebhookFailedAsync(order.OrderCode, reason);
            if (!result.Success)
            {
                _logger.LogError(
                    "Reconcile: HandleWebhookFailed that bai cho don {OrderCode}: {Message}",
                    order.OrderCode, result.Message);
            }
        }
        // PENDING hoặc các status khác: giữ nguyên DB.

        // Đọc lại DB để trả về status mới nhất.
        var refreshedOrder = await _orderRepository.GetByIdAsync(orderId);
        var refreshedPayment = await _paymentRepository.GetLatestByOrderIdAsync(orderId);
        if (refreshedOrder == null || refreshedPayment == null)
            return null;

        return new PaymentStatusResponse
        {
            OrderId = refreshedOrder.Id,
            OrderCode = refreshedOrder.OrderCode,
            PaymentStatus = refreshedPayment.Status.ToString(),
            OrderStatus = refreshedOrder.Status.ToString(),
            IsPaid = refreshedPayment.Status == PaymentStatus.Paid,
            IsExpired = refreshedPayment.Status == PaymentStatus.Expired
                || DateTime.UtcNow > refreshedPayment.ExpiresAt,
            ExpiresInSeconds = refreshedPayment.Status == PaymentStatus.Pending
                ? (int)Math.Max(0,
                    (refreshedPayment.ExpiresAt - DateTime.UtcNow).TotalSeconds)
                : 0
        };
    }

    /// <summary>
    /// Lấy lịch sử thanh toán của đơn. Nếu truyền userId thì kiểm tra sở hữu.
    /// </summary>
    public async Task<PaymentHistoryResponse?> GetHistoryAsync(string orderId, string? userId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return null;

        // Nếu có userId (call từ người mua) thì chỉ xem được đơn của chính mình.
        if (userId != null && order.UserId != userId)
            return null;

        var payments = await _paymentRepository.GetByOrderIdAsync(orderId);

        var responses = new List<PaymentResponse>();
        foreach (var p in payments)
        {
            responses.Add(BuildPaymentResponse(p));
        }

        var current = payments.FirstOrDefault();

        return new PaymentHistoryResponse
        {
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            CurrentPayment = current != null ? BuildPaymentResponse(current) : null,
            Payments = responses,
            IsPaid = current?.Status == PaymentStatus.Paid
        };
    }

    /// <summary>
    /// Đóng các phiên thanh toán đã hết hạn: trả sản phẩm về kho và chuyển đơn
    /// sang PaymentExpired. Gọi bởi background service định kỳ.
    /// </summary>
    public async Task<int> ExpireStalePaymentsAsync(int limit = 100)
    {
        var stalePayments = await _paymentRepository.GetExpiredUnpaidAsync(limit);
        int processed = 0;

        foreach (var payment in stalePayments)
        {
            var order = await _orderRepository.GetByIdAsync(payment.OrderId);
            if (order == null)
                continue;

            // Đơn đã đi xa hơn AwaitingPayment (đã trả tiền, đã hủy...) -> bỏ qua.
            if (order.Status != OrderStatus.AwaitingPayment)
            {
                payment.Status = PaymentStatus.Expired;
                payment.UpdatedAt = DateTime.UtcNow;
                await _paymentRepository.UpdateAsync(payment);
                continue;
            }

            payment.Status = PaymentStatus.Expired;
            payment.Note = "Không hoàn tất thanh toán trong thời gian cho phép.";
            payment.UpdatedAt = DateTime.UtcNow;
            await _paymentRepository.UpdateAsync(payment);

            // Trả sản phẩm về kho.
            foreach (var item in order.Items)
            {
                await _productRepository.ReleaseStockAsync(item.ProductId, item.Quantity);
            }

            order.Status = OrderStatus.PaymentExpired;
            order.CancelReason = "Bạn không hoàn tất thanh toán trong thời gian cho phép.";
            order.UpdatedAt = DateTime.UtcNow;
            await _orderRepository.UpdateAsync(order);

            await _historyRepository.CreateAsync(new OrderStatusHistory
            {
                OrderId = order.Id,
                FromStatus = OrderStatus.AwaitingPayment,
                ToStatus = OrderStatus.PaymentExpired,
                Note = order.CancelReason,
                ChangedBy = OrderStatusChangedBy.System
            });

            processed++;
        }

        if (processed > 0)
            _logger.LogInformation("Da dong {Count} phien thanh toan het han.", processed);

        return processed;
    }

    /// <summary>
    /// Hoàn tiền cho một đơn đã thanh toán (dùng khi khách hủy đơn).
    /// </summary>
    public async Task<ApiResponse> RefundAsync(string orderId, string? reason)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);

        // Đã hủy/hết hạn rồi thì không hoàn 2 lần.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.PaymentExpired)
            return Fail("Đơn hàng đã được hủy trước đó.", ApiErrorCode.Conflict);

        // Đơn chưa thu tiền thì chỉ cần hủy, không có gì để hoàn.
        if (order.Status == OrderStatus.AwaitingPayment)
        {
            return await _orderService.CancelOrderAsync(order.UserId, orderId, reason);
        }

        var payment = await _paymentRepository.GetLatestByOrderIdAsync(orderId);

        // Đã hoàn tiền rồi -> không hoàn 2 lần.
        if (payment?.Status == PaymentStatus.Refunded)
            return Fail("Đơn hàng đã được hoàn tiền trước đó.", ApiErrorCode.Conflict);

        // Chưa có transactionId thì không hoàn được, cần nhân viên xử lý tay.
        if (payment == null || string.IsNullOrWhiteSpace(payment.PayOsTransactionId))
        {
            _logger.LogError(
                "Don {OrderCode} chua co transactionId nen khong hoan tien duoc. Can xu ly tay.",
                order.OrderCode);
            return Fail("Chưa tìm thấy giao dịch để hoàn tiền. Vui lòng liên hệ hỗ trợ.", ApiErrorCode.Conflict);
        }

        var refundResult = await _payOsService.RefundAsync(
            payment.PayOsTransactionId,
            string.IsNullOrWhiteSpace(reason) ? "Khách hủy đơn" : reason);

        if (!refundResult.Success)
        {
            // HOÀN TIỀN THẤT BẠI: đơn giữ nguyên trạng thái để không mất tiền.
            _logger.LogError("Hoan tien that bai cho don {OrderCode}", order.OrderCode);
            return Fail(
                "Hoàn tiền thất bại. Đơn hàng vẫn giữ nguyên, vui lòng liên hệ hỗ trợ.",
                ApiErrorCode.Conflict);
        }

        // Hoàn tiền thành công -> cập nhật payment + trả kho + hủy đơn.
        payment.Status = PaymentStatus.Refunded;
        payment.RefundedAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;
        await _paymentRepository.UpdateAsync(payment);

        foreach (var item in order.Items)
        {
            await _productRepository.ReleaseStockAsync(item.ProductId, item.Quantity);
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelReason = string.IsNullOrWhiteSpace(reason) ? "Người mua hủy đơn." : reason;
        order.UpdatedAt = DateTime.UtcNow;
        await _orderRepository.UpdateAsync(order);

        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.Confirmed,
            ToStatus = OrderStatus.Cancelled,
            Note = "Đã hoàn tiền qua PayOS. " + order.CancelReason,
            ChangedBy = OrderStatusChangedBy.System
        });

        return new ApiResponse
        {
            Success = true,
            Message = "Đã hủy đơn và hoàn tiền thành công."
        };
    }

    // ============================================
    // HELPER
    // ============================================

    /// <summary>
    /// Trả toàn bộ sản phẩm trong đơn về kho. Dùng khi không tạo được phiên thanh toán.
    /// </summary>
    private async Task ReleaseOrderStockAsync(Order order)
    {
        foreach (var item in order.Items)
        {
            await _productRepository.ReleaseStockAsync(item.ProductId, item.Quantity);
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelReason = "Không tạo được phiên thanh toán. Vui lòng đặt hàng lại.";
        order.UpdatedAt = DateTime.UtcNow;
        await _orderRepository.UpdateAsync(order);

        await _historyRepository.CreateAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.AwaitingPayment,
            ToStatus = OrderStatus.Cancelled,
            Note = order.CancelReason,
            ChangedBy = OrderStatusChangedBy.System
        });
    }

    /// <summary>
    /// Base URL của API để dựng returnUrl / cancelUrl gửi cho PayOS.
    /// </summary>
    private string GetBaseUrl() => _gatewayConfig.AppBaseUrl.TrimEnd('/');

    /// <summary>
    /// Chuyển entity Payment sang DTO trả về cho frontend.
    /// </summary>
    private static PaymentResponse BuildPaymentResponse(Payment p)
    {
        return new PaymentResponse
        {
            Id = p.Id,
            OrderId = p.OrderId,
            OrderCode = p.OrderCode,
            Method = p.Method.ToString(),
            Amount = p.Amount,
            Status = p.Status.ToString(),
            // Chỉ trả link thanh toán khi phiên còn hiệu lực, tránh FE mở
            // link đã hết hạn và báo lỗi khó hiểu.
            CheckoutUrl = p.Status == PaymentStatus.Pending ? p.CheckoutUrl : null,
            QrCode = p.Status == PaymentStatus.Pending ? p.QrCodeUrl : null,
            ReceivedAmount = p.ReceivedAmount,
            PaidAt = p.PaidAt,
            ExpiresAt = p.ExpiresAt,
            CreatedAt = p.CreatedAt
        };
    }

    private static ApiResponse Fail(string message, string errorCode = ApiErrorCode.Validation)
        => new ApiResponse { Success = false, Message = message, ErrorCode = errorCode };
}