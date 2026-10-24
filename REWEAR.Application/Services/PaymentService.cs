using System.Globalization;
using Microsoft.Extensions.Logging;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>Gateway I/O stays outside database transactions. Each financial outcome and its inventory/history commit together.</summary>
public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _payments;
    private readonly IOrderRepository _orders;
    private readonly IOrderStatusHistoryRepository _history;
    private readonly IProductRepository _products;
    private readonly IPayOsService _gateway;
    private readonly ICommerceUnitOfWork _transactions;
    private readonly ICartRepository _carts;
    private readonly IShippingRepository _shipping;
    private readonly IPaymentGatewayConfig _config;
    private readonly ILogger<PaymentService> _logger;
    private readonly ILifecycleStore _lifecycle;
    private readonly IVoucherService _vouchers;

    public PaymentService(IPaymentRepository payments, IOrderRepository orders, IOrderStatusHistoryRepository history,
        IProductRepository products, IPayOsService gateway, ICommerceUnitOfWork transactions, ICartRepository carts,
        IShippingRepository shipping, IPaymentGatewayConfig config, ILogger<PaymentService> logger, ILifecycleStore lifecycle,
        IVoucherService vouchers)
    { _payments = payments; _orders = orders; _history = history; _products = products; _gateway = gateway;
      _transactions = transactions; _carts = carts; _shipping = shipping; _config = config; _logger = logger; _lifecycle = lifecycle;
      _vouchers = vouchers; }

    public async Task<(ApiResponse Response, CheckoutSessionResponse? Session)> CreateSessionAsync(string orderId, int expirationMinutes = 15)
    {
        if (expirationMinutes is < 2 or > 60) expirationMinutes = 15;
        var preparation = await _transactions.ExecuteAsync(async () =>
        {
            var order = await _orders.GetByIdAsync(orderId);
            if (order == null || order.Status != OrderStatus.AwaitingPayment) return (Payment: (Payment?)null, OwnsCreation: false);
            if (order.ReservationExpiresAt == null)
            { order.ReservationExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes); await _orders.UpdateAsync(order); }
            if (order.ReservationExpiresAt <= DateTime.UtcNow) return (Payment: (Payment?)null, OwnsCreation: false);
            // Serialize session preparation on the existing order document. Two transactions
            // must not both observe 'no payment' and create different gateway identities.
            await _orders.UpdateAsync(order);
            var payment = await _payments.GetLatestByOrderIdAsync(orderId);
            if (payment != null)
            {
                if (payment.Status != PaymentStatus.Pending || payment.RequiresReconciliation) return (Payment: (Payment?)null, OwnsCreation: false);
                if (!string.IsNullOrEmpty(payment.CheckoutUrl) || payment.SessionCreating) return (Payment: payment, OwnsCreation: false);
                payment.SessionCreating = true;
                await _payments.UpdateAsync(payment);
                return (Payment: payment, OwnsCreation: true);
            }
            payment = new Payment { OrderId = order.Id, OrderCode = order.OrderCode, UserId = order.UserId,
                Amount = order.TotalAmount, ExpiresAt = order.ReservationExpiresAt.Value, SessionCreating = true };
            // A durable gateway code is reused on retries. Never create a second charge identity for one payment.
            payment.GatewayOrderCode = BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payment.Id)), 0) & 0x1FFFFFFFFFFFFF;
            await _payments.CreateAsync(payment);
            return (Payment: payment, OwnsCreation: true);
        });
        var pending = preparation.Payment;
        if (pending == null) return (Fail("Đơn không còn trong thời gian thanh toán.", ApiErrorCode.Conflict), null);
        if (!string.IsNullOrEmpty(pending.CheckoutUrl)) return (Ok("Tiếp tục thanh toán."), Session(pending));
        if (!preparation.OwnsCreation) return (Fail("Phiên thanh toán đang được tạo. Vui lòng kiểm tra lại đơn.", ApiErrorCode.Conflict), null);

        PayOsCreateResult? result = null;
        try
        {
            result = await _gateway.CreatePaymentRequestAsync(pending.OrderCode, pending.Amount,
                $"{_config.AppBaseUrl.TrimEnd('/')}/payment/waiting?orderId={orderId}",
                $"{_config.AppBaseUrl.TrimEnd('/')}/payment/waiting?orderId={orderId}&cancelled=1",
                pending.ExpiresAt, pending.GatewayOrderCode);
        }
        catch (Exception) { _logger.LogWarning("Không nhận được kết quả tạo phiên thanh toán; cần đối soát."); }

        return await _transactions.ExecuteAsync(async () =>
        {
            var current = await _payments.GetLatestByOrderIdAsync(orderId) ?? throw new InvalidOperationException("Thiếu bản ghi thanh toán.");
            current.SessionCreating = false;
            if (result == null || !Uri.TryCreate(result.CheckoutUrl, UriKind.Absolute, out var checkoutUri)
                || checkoutUri.Scheme != "https" || !(checkoutUri.Host == "pay.payos.vn" || checkoutUri.Host.EndsWith(".payos.vn", StringComparison.OrdinalIgnoreCase)))
            {
                // Ambiguous network failure is not a failed transfer. Keep the cart and reconcile the durable gateway identity.
                await _payments.UpdateAsync(current);
                return (new ApiResponse { Success = false, ErrorCode = ApiErrorCode.Conflict,
                    Message = "Chưa tạo được đường dẫn thanh toán. Giỏ hàng được giữ nguyên; hãy mở Đơn mua để kiểm tra lại.",
                    Data = new { orderId } }, (CheckoutSessionResponse?)null);
            }
            current.PaymentLinkId = result.PaymentLinkId;
            current.CheckoutUrl = result.CheckoutUrl;
            current.QrCodeUrl = result.QrCode;
            await _payments.UpdateAsync(current);
            var order = await _orders.GetByIdAsync(orderId) ?? throw new InvalidOperationException("Thiếu đơn hàng.");
            if (order.Status != OrderStatus.AwaitingPayment || current.Status != PaymentStatus.Pending)
                return (Fail("Đơn đã thay đổi trạng thái; vui lòng kiểm tra lại.", ApiErrorCode.Conflict), (CheckoutSessionResponse?)null);
            var cart = await _carts.GetByUserIdAsync(order.UserId);
            if (cart != null)
            {
                cart.Items.RemoveAll(i => order.SelectedCartItemIds.Contains(i.Id));
                await _carts.UpdateAsync(cart);
            }
            return (Ok("Đã tạo phiên thanh toán."), (CheckoutSessionResponse?)Session(current));
        });
    }

    public Task<ApiResponse> HandleWebhookSuccessAsync(string orderCode, string paymentLinkId, decimal amount, string? transactionReference = null) =>
        RecordReceiptAsync(orderCode, paymentLinkId, amount, transactionReference, true);

    private Task<ApiResponse> RecordReceiptAsync(string orderCode, string paymentLinkId, decimal amount, string? transactionReference, bool settlementConfirmed) =>
        _transactions.ExecuteAsync(async () =>
        {
            var order = await _orders.GetByOrderCodeAsync(orderCode);
            if (order == null) return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);
            var payment = await _payments.GetLatestByOrderIdAsync(order.Id);
            if (payment == null || string.IsNullOrWhiteSpace(paymentLinkId) || payment.PaymentLinkId != paymentLinkId)
                return Fail("Phiên thanh toán không khớp.", ApiErrorCode.Conflict);
            if (amount <= 0) return Fail("Số tiền thanh toán không hợp lệ.");
            if (!string.IsNullOrWhiteSpace(transactionReference))
            {
                var receipt = payment.Receipts.FirstOrDefault(r => r.Reference == transactionReference);
                if (receipt != null) return receipt.Amount == amount ? Ok("Giao dịch đã được ghi nhận.") : Fail("Chứng từ trùng nhưng khác số tiền.", ApiErrorCode.Conflict);
                payment.Receipts.Add(new() { Reference = transactionReference, Amount = amount });
                amount = Math.Max(payment.ReceivedAmount ?? 0, payment.Receipts.Sum(r => r.Amount));
            }
            if (payment.Status == PaymentStatus.Refunded)
            {
                var previousReceived = payment.ReceivedAmount ?? payment.Amount;
                payment.RefundedAmount ??= previousReceived;
                if (amount > previousReceived)
                {
                    SetReceivedAmount(payment, amount); payment.RequiresReconciliation = true;
                    payment.ReconciliationReason = "Có tiền nhận thêm sau khi đã hoàn tiền; cần xử lý khoản hoàn riêng.";
                    payment.RefundState = "RequiresSeparateRefund";
                }
                await _payments.UpdateAsync(payment);
                return Ok("Đã ghi nhận chứng từ sau hoàn tiền để đối soát riêng; không đổi tồn kho.");
            }
            if (payment.Status == PaymentStatus.Paid)
            {
                if (amount > (payment.ReceivedAmount ?? payment.Amount))
                {
                    SetReceivedAmount(payment, amount); payment.RequiresReconciliation = true;
                    payment.ReconciliationReason = "Phát sinh tiền nhận thêm sau khi đã xác nhận thanh toán; cần đối soát khoản dư.";
                    payment.RefundState = "Requested"; payment.RefundRequestedAt ??= DateTime.UtcNow;
                }
                payment.PayOsTransactionId ??= transactionReference;
                await _payments.UpdateAsync(payment);
                return Ok("Đã ghi nhận chứng từ; không chốt đơn hoặc trừ kho thêm lần nữa.");
            }
            SetReceivedAmount(payment, amount);
            payment.PaidAt = DateTime.UtcNow;
            payment.PayOsTransactionId = string.IsNullOrWhiteSpace(transactionReference) ? null : transactionReference;
            payment.Status = PaymentStatus.Paid;
            bool expired = DateTime.UtcNow >= (order.ReservationExpiresAt ?? payment.ExpiresAt);
            if (!settlementConfirmed || amount != payment.Amount || order.Status != OrderStatus.AwaitingPayment || expired)
            {
                payment.RequiresReconciliation = true;
                payment.ReconciliationReason = !settlementConfirmed ? "Cổng thanh toán ghi nhận có tiền nhưng chưa xác nhận thanh toán đầy đủ."
                    : amount != payment.Amount ? "Số tiền không khớp." : "Nhận tiền sau khi đơn hết hạn hoặc thay đổi trạng thái.";
                payment.RefundState = "Requested";
                payment.RefundRequestedAt = DateTime.UtcNow;
                if (order.Status == OrderStatus.AwaitingPayment)
                    await CloseUnpaidAsync(order, OrderStatus.PaymentExpired, payment.ReconciliationReason);
                await _payments.UpdateAsync(payment);
                return Ok("Đã ghi nhận tiền nhận được; cần đối soát, chưa xác nhận giao hàng.");
            }
            foreach (var item in order.Items)
                if (await _products.CommitStockAsync(item.ProductId, item.Quantity, order.Id) == 0)
                    throw new InvalidOperationException("Không tìm thấy giữ hàng hợp lệ; giao dịch cần được thử lại/đối soát.");

            // ===== TASK 10: VOUCHER — GHI NHẬN DÙNG THẬT =====
            // Voucher đã được GIỮ CHỖ (Reserved) lúc tạo đơn. Chỉ khi thanh toán
            // thành công mới chuyển sang Used và tăng UsedCount. ConsumeForOrderAsync
            // là atomic + idempotent nên webhook lặp chỉ ghi nhận 1 lần.
            if (!string.IsNullOrWhiteSpace(order.VoucherId))
            {
                bool consumed = await _vouchers.ConsumeForOrderAsync(
                    order.UserId, order.VoucherId, order.Id);
                if (!consumed)
                    _logger.LogWarning(
                        "Voucher khong the ghi nhan su dung cho don {OrderCode}, voucher={VoucherId}.",
                        order.OrderCode, order.VoucherId);
                else
                    _logger.LogInformation(
                        "Voucher consumed: don {OrderCode}, voucher={VoucherId}", order.OrderCode, order.VoucherId);
            }

            order.Status = OrderStatus.Confirmed;
            order.PaidAt = payment.PaidAt;
            await _payments.UpdateAsync(payment);
            await _orders.UpdateAsync(order);
            await AddHistory(order, OrderStatus.AwaitingPayment, OrderStatus.Confirmed, "Backend đã xác nhận thanh toán.");
            return Ok("Đã ghi nhận thanh toán thành công.");
        });

    public Task<ApiResponse> HandleWebhookFailedAsync(string orderCode, string? message) => _transactions.ExecuteAsync(async () =>
    {
        var order = await _orders.GetByOrderCodeAsync(orderCode);
        if (order == null) return Fail("Không tìm thấy đơn hàng.", ApiErrorCode.NotFound);
        var payment = await _payments.GetLatestByOrderIdAsync(order.Id);
        if (payment == null || payment.Status != PaymentStatus.Pending || order.Status != OrderStatus.AwaitingPayment) return Ok("Đơn đã được xử lý.");
        payment.Status = PaymentStatus.Failed;
        payment.Note = message;
        await _payments.UpdateAsync(payment);
        await CloseUnpaidAsync(order, OrderStatus.PaymentExpired, message ?? "Thanh toán chưa hoàn tất.");
        return Ok("Đã đóng phiên thanh toán.");
    });

    public async Task<PaymentStatusResponse?> GetStatusAsync(string orderId, string? userId)
    {
        var order = await _orders.GetByIdAsync(orderId);
        if (order == null || (userId != null && order.UserId != userId)) return null;
        var payment = await _payments.GetLatestByOrderIdAsync(orderId);
        if (payment == null) return null;
        if (payment.Status != PaymentStatus.Refunded && payment.Status != PaymentStatus.Paid) await ReconcilePaymentAsync(payment);
        return await ReadStatusAsync(orderId);
    }

    public async Task<PaymentStatusResponse?> ReconcileAsync(string orderId)
    {
        var payment = await _payments.GetLatestByOrderIdAsync(orderId);
        if (payment == null) return null;
        if (payment.Status != PaymentStatus.Refunded) await ReconcilePaymentAsync(payment);
        return await ReadStatusAsync(orderId);
    }

    private async Task ReconcilePaymentAsync(Payment payment)
    {
        await _transactions.ExecuteAsync(async () =>
        {
            var current = await _payments.GetLatestByOrderIdAsync(payment.OrderId);
            if (current != null) { current.LastGatewayCheckedAt = DateTime.UtcNow; await _payments.UpdateAsync(current); }
            return true;
        });
        var id = payment.PaymentLinkId ?? (payment.GatewayOrderCode > 0 ? payment.GatewayOrderCode.ToString(CultureInfo.InvariantCulture) : payment.PayOsTransactionId);
        if (string.IsNullOrWhiteSpace(id)) return;
        var info = await _gateway.GetTransactionAsync(id);
        if (info == null) return;
        if (payment.GatewayOrderCode > 0 && info.Reference != payment.GatewayOrderCode.ToString(CultureInfo.InvariantCulture)) return;
        if (info.Amount != payment.Amount || string.IsNullOrWhiteSpace(info.PaymentLinkId)) return;
        if (info.PaymentLinkId != payment.PaymentLinkId && payment.PaymentLinkId != null) return;
        if (info.Status.Equals("PAID", StringComparison.OrdinalIgnoreCase) || info.PaidAmount > 0)
        {
            if (payment.PaymentLinkId == null)
                await _transactions.ExecuteAsync(async () =>
                {
                    var p = await _payments.GetLatestByOrderIdAsync(payment.OrderId);
                    if (p != null && p.PaymentLinkId == null) { p.PaymentLinkId = info.PaymentLinkId; await _payments.UpdateAsync(p); }
                    return true;
                });
            await RecordReceiptAsync(payment.OrderCode, info.PaymentLinkId, info.PaidAmount, null, info.Status.Equals("PAID", StringComparison.OrdinalIgnoreCase));
        }
        else if (payment.Status == PaymentStatus.Pending && (info.Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) || info.Status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase)))
            await HandleWebhookFailedAsync(payment.OrderCode, "Cổng thanh toán đã đóng phiên.");
    }

    public async Task<int> ExpireStalePaymentsAsync(int limit = 100)
    {
        int count = 0;
        foreach (var stale in await _payments.GetExpiredUnpaidAsync(limit))
        {
            // Close the entire stock-expiry batch before any gateway I/O. A gateway outage
            // must not hold later orders' inventory behind a series of network timeouts.
            bool changed = await _transactions.ExecuteAsync(async () =>
            {
                var payment = await _payments.GetLatestByOrderIdAsync(stale.OrderId);
                var order = await _orders.GetByIdAsync(stale.OrderId);
                if (payment == null || order == null || payment.Id != stale.Id || payment.Status != PaymentStatus.Pending || payment.ExpiresAt > DateTime.UtcNow) return false;
                payment.Status = PaymentStatus.Expired;
                await _payments.UpdateAsync(payment);
                if (order.Status == OrderStatus.AwaitingPayment) await CloseUnpaidAsync(order, OrderStatus.PaymentExpired, "Hết thời gian thanh toán.");
                return true;
            });
            if (changed) count++;
        }
        // A process can stop after checkout committed but before a Payment was inserted.
        // Expire those order-owned reservations as well; never infer ownership from product status.
        foreach (var stale in await _orders.GetExpiredAwaitingAsync(DateTime.UtcNow, limit))
        {
            bool changed = await _transactions.ExecuteAsync(async () =>
            {
                var order = await _orders.GetByIdAsync(stale.Id);
                if (order == null || order.Status != OrderStatus.AwaitingPayment || order.ReservationExpiresAt > DateTime.UtcNow) return false;
                if (await _payments.GetLatestByOrderIdAsync(order.Id) != null) return false;
                await CloseUnpaidAsync(order, OrderStatus.PaymentExpired, "Phiên đặt hàng hết hạn trước khi tạo thanh toán.");
                return true;
            });
            if (changed) count++;
        }
        // A missed webhook or closed browser must not hide money received after local expiry.
        // This bounded catch-up runs after stock expiry and does not extend reservations.
        foreach (var candidate in await _payments.GetReconciliationCandidatesAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddMinutes(-5), Math.Min(limit, 10)))
        {
            try { await ReconcilePaymentAsync(candidate); }
            catch (Exception) { _logger.LogWarning("Chưa đối soát được một giao dịch; sẽ thử lại ở vòng quét sau."); }
        }
        return count;
    }

    public Task<ApiResponse> RefundAsync(string orderId, string? reason) => _transactions.ExecuteAsync(async () =>
    {
        var order = await _orders.GetByIdAsync(orderId);
        var payment = await _payments.GetLatestByOrderIdAsync(orderId);
        if (order == null || payment == null) return Fail("Không tìm thấy giao dịch.", ApiErrorCode.NotFound);
        if (payment.Status == PaymentStatus.Refunded) return Ok("Đã hoàn tiền trước đó.");
        if (payment.Status != PaymentStatus.Paid) return Fail("Đơn chưa có thanh toán để hoàn.", ApiErrorCode.Conflict);
        if (order.Status != OrderStatus.Confirmed && !payment.RequiresReconciliation)
            return Fail("Đơn đã giao cho vận chuyển; cần tiếp nhận và kiểm định hàng hoàn trước khi xử lý.", ApiErrorCode.Conflict);
        var shipping = await _shipping.GetByOrderIdAsync(orderId);
        if (shipping != null && shipping.Status != ShippingStatus.Pending) return Fail("Đơn đã rời kho; cần quy trình hàng hoàn.", ApiErrorCode.Conflict);
        if (payment.RefundState != "Requested")
        {
            payment.RefundState = "Requested"; payment.RefundRequestedAt = DateTime.UtcNow; payment.Note = reason;
            await _payments.UpdateAsync(payment);
            await AddHistory(order, order.Status, order.Status, "Đã yêu cầu hoàn tiền, chờ đối soát và xác nhận.");
        }
        return Ok("Đã ghi nhận yêu cầu hoàn tiền; chưa xác nhận tiền đã được hoàn.");
    });

    public Task<ApiResponse> ConfirmManualRefundAsync(string orderId, string reference, decimal amount, string actor) => _transactions.ExecuteAsync(async () =>
    {
        reference = reference?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 120 || string.IsNullOrWhiteSpace(actor)) return Fail("Cần mã giao dịch hoàn tiền và người xác nhận.");
        var order = await _orders.GetByIdAsync(orderId);
        var payment = await _payments.GetLatestByOrderIdAsync(orderId);
        if (order == null || payment == null) return Fail("Không tìm thấy giao dịch.", ApiErrorCode.NotFound);
        if (payment.Status == PaymentStatus.Refunded) return payment.RefundReference == reference && (payment.RefundedAmount ?? payment.ReceivedAmount ?? payment.Amount) == amount
            ? Ok("Đã xác nhận hoàn tiền trước đó.") : Fail("Giao dịch đã có chứng từ hoàn tiền với nội dung khác.", ApiErrorCode.Conflict);
        if (payment.Status != PaymentStatus.Paid || payment.RefundState != "Requested" || amount <= 0 || amount != (payment.ReceivedAmount ?? payment.Amount)) return Fail("Số tiền hoặc trạng thái hoàn tiền không hợp lệ.", ApiErrorCode.Conflict);
        var shipping = await _shipping.GetByOrderIdAsync(orderId);
        var returned = await _lifecycle.GetAsync<ReturnRequest>(orderId);
        bool inspectedReturn = shipping?.Status == ShippingStatus.Returned && order.Status == OrderStatus.Returned && returned?.State == "Inspected";
        if (shipping != null && shipping.Status != ShippingStatus.Pending && !inspectedReturn) return Fail("Cần xử lý hàng hoàn trước khi xác nhận.", ApiErrorCode.Conflict);
        if (order.Status == OrderStatus.Confirmed)
            foreach (var item in order.Items) await _products.RestorePaidStockAsync(item.ProductId, item.Quantity, order.Id);
        if (inspectedReturn)
        {
            foreach (var decision in returned!.Items.Where(i => i.RestockApproved))
            {
                var item = order.Items.Single(i => i.ProductId == decision.ProductId);
                if (await _products.RestorePaidStockAsync(item.ProductId, item.Quantity, order.Id) != 1)
                    throw new InvalidOperationException("Thiếu phân bổ tồn kho gốc; cần đối soát dữ liệu cũ trước khi mở bán lại.");
                var product = (await _products.GetByIdAsync(item.ProductId))!; product.Condition = decision.Condition; await _products.UpdateAsync(product);
                await _lifecycle.InsertAsync(new InventoryMovement { InventoryId = "return:" + order.Id + ":" + item.ProductId,
                    FromState = "ReturnAwaitingRefund", ToState = "ReadyRetail", ActorId = actor, RelatedId = order.Id, Reason = decision.Note });
            }
            returned!.State = "RefundConfirmed"; returned.Timeline.Add(new() { From = "Inspected", To = "RefundConfirmed", ActorId = actor, Note = "Đã xác nhận hoàn tiền; chỉ mở bán món được kiểm định duyệt." });
            await _lifecycle.SaveAsync(returned);
        }
        var from = order.Status;
        order.Status = inspectedReturn ? OrderStatus.Returned : OrderStatus.Cancelled;
        order.CancelReason = "Đã xác nhận hoàn tiền theo chứng từ.";
        payment.Status = PaymentStatus.Refunded; payment.RefundState = "Confirmed"; payment.RefundReference = reference;
        payment.RefundedAmount = amount;
        payment.RefundConfirmedBy = actor; payment.RefundedAt = DateTime.UtcNow; payment.RequiresReconciliation = false;
        await _payments.UpdateAsync(payment); await _orders.UpdateAsync(order);
        await AddHistory(order, from, order.Status, "Admin đã xác nhận chứng từ hoàn tiền.", actor);
        return Ok("Đã xác nhận hoàn tiền và cập nhật đơn.");
    });

    public async Task<PaymentHistoryResponse?> GetHistoryAsync(string orderId, string? userId)
    {
        var order = await _orders.GetByIdAsync(orderId);
        if (order == null || (userId != null && order.UserId != userId)) return null;
        var rows = await _payments.GetByOrderIdAsync(orderId);
        return new PaymentHistoryResponse { OrderId = order.Id, OrderCode = order.OrderCode,
            Payments = rows.Select(Response).ToList(), CurrentPayment = rows.Count > 0 ? Response(rows[0]) : null,
            IsPaid = rows.Any(p => p.Status == PaymentStatus.Paid && !p.RequiresReconciliation) };
    }

    private async Task<PaymentStatusResponse?> ReadStatusAsync(string orderId)
    {
        var order = await _orders.GetByIdAsync(orderId); var p = await _payments.GetLatestByOrderIdAsync(orderId);
        if (order == null || p == null) return null;
        return new PaymentStatusResponse { OrderId = order.Id, OrderCode = order.OrderCode, OrderStatus = order.Status.ToString(),
            PaymentStatus = p.RequiresReconciliation ? "ReconciliationRequired" : p.Status.ToString(), RequiresReconciliation = p.RequiresReconciliation,
            RefundState = p.RefundState, IsPaid = p.Status == PaymentStatus.Paid && !p.RequiresReconciliation,
            IsExpired = p.Status == PaymentStatus.Expired, ExpiresInSeconds = p.Status == PaymentStatus.Pending ? Math.Max(0, (int)(p.ExpiresAt - DateTime.UtcNow).TotalSeconds) : 0 };
    }
    private async Task CloseUnpaidAsync(Order order, OrderStatus status, string reason)
    {
        foreach (var item in order.Items) await _products.ReleaseStockAsync(item.ProductId, item.Quantity, order.Id);

        // ===== TASK 10: Giải phóng voucher đã giữ chỗ cho đơn này =====
        // Đơn này chưa thanh toán nên voucher chỉ mới ở trạng thái Reserved.
        // TryRelease là atomic theo orderId nên gọi lại nhiều lần cũng không
        // giải phóng nhầm voucher của đơn khác.
        if (!string.IsNullOrWhiteSpace(order.VoucherId))
        {
            bool released = await _vouchers.ReleaseForOrderAsync(
                order.UserId, order.VoucherId, order.Id);
            if (!released)
                _logger.LogWarning(
                    "Voucher cua don {OrderCode} khong o trang thai giu cho, bo qua viec giai phong.",
                    order.OrderCode);
        }

        var from = order.Status; order.Status = status; order.CancelReason = reason; await _orders.UpdateAsync(order);
        await AddHistory(order, from, status, reason);
    }
    private Task AddHistory(Order o, OrderStatus from, OrderStatus to, string note, string? actor = null) => _history.CreateAsync(new OrderStatusHistory
        { OrderId = o.Id, FromStatus = from, ToStatus = to, Note = note, ChangedBy = actor == null ? OrderStatusChangedBy.System : OrderStatusChangedBy.Staff, ChangedByUserId = actor });
    private static CheckoutSessionResponse Session(Payment p) => new() { OrderId = p.OrderId, OrderCode = p.OrderCode, TotalAmount = p.Amount,
        CheckoutUrl = p.CheckoutUrl, QrCode = p.QrCodeUrl, PaymentLinkId = p.PaymentLinkId, ExpiresAt = p.ExpiresAt,
        ExpiresInSeconds = Math.Max(0, (int)(p.ExpiresAt - DateTime.UtcNow).TotalSeconds) };
    private static PaymentResponse Response(Payment p) => new() { Id = p.Id, OrderId = p.OrderId, OrderCode = p.OrderCode, Method = p.Method.ToString(),
        Amount = p.Amount, Status = p.RequiresReconciliation ? "ReconciliationRequired" : p.Status.ToString(), CheckoutUrl = p.Status == PaymentStatus.Pending ? p.CheckoutUrl : null,
        QrCode = p.QrCodeUrl, ReceivedAmount = p.ReceivedAmount, PaidAt = p.PaidAt, ExpiresAt = p.ExpiresAt, CreatedAt = p.CreatedAt };
    private static void SetReceivedAmount(Payment payment, decimal amount)
    {
        decimal previous = payment.ReceivedAmount ?? (payment.Status is PaymentStatus.Paid or PaymentStatus.Refunded ? payment.Amount : 0);
        if (amount > previous)
        {
            if (payment.CashEvents.Count == 0 && previous > 0)
                payment.CashEvents.Add(new() { Amount = previous, RecordedAt = payment.PaidAt, LegacyTimestamp = true });
            payment.CashEvents.Add(new() { Amount = amount - previous, RecordedAt = DateTime.UtcNow });
        }
        payment.ReceivedAmount = amount;
    }

    private static ApiResponse Ok(string message) => new() { Success = true, Message = message };
    private static ApiResponse Fail(string message, string code = ApiErrorCode.Validation) => new() { Success = false, Message = message, ErrorCode = code };
}
