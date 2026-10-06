using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Service xử lý thanh toán qua PayOS (Task 8).
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Tạo phiên thanh toán cho đơn và gọi PayOS để lấy trang thanh toán.
    /// </summary>
    /// <param name="orderId">Id của đơn hàng cần tạo phiên thanh toán.</param>
    /// <param name="expirationMinutes">Số phút khách được giữ chỗ sản phẩm (mặc định 15).</param>
    Task<(ApiResponse Response, CheckoutSessionResponse? Session)> CreateSessionAsync(
        string orderId, int expirationMinutes = 15);

    /// <summary>
    /// Xử lý webhook "thanh toán thành công" từ PayOS. Idempotent: gọi lại nhiều lần
    /// vẫn chỉ xử lý một lần.
    /// </summary>
    /// <param name="orderCode">Mã đơn REWEAR lấy từ payload PayOS.</param>
    /// <param name="transactionId">Id giao dịch từ PayOS.</param>
    /// <param name="amount">Số tiền thực nhận.</param>
    Task<ApiResponse> HandleWebhookSuccessAsync(
        string orderCode, string transactionId, decimal amount);

    /// <summary>
    /// Xử lý webhook "thanh toán thất bại / bị hủy" từ PayOS.
    /// </summary>
    Task<ApiResponse> HandleWebhookFailedAsync(string orderCode, string? message);

    /// <summary>
    /// Tra cứu trạng thái thanh toán của đơn. Nếu hệ thống vẫn đang chờ nhưng đã quá
    /// thời gian hợp lý, chủ động gọi PayOS để hỏi lại — chữa trường hợp webhook bị mất.
    /// </summary>
    Task<PaymentStatusResponse?> GetStatusAsync(string orderId, string? userId);

    /// <summary>
    /// Đối soát chủ động: gọi thẳng PayOS server để xác minh trạng thái thanh toán
    /// của đơn và cập nhật DB nếu có thay đổi. Thay thế webhook trong kiến trúc
    /// không dùng Cloudflare Tunnel/Worker.
    /// </summary>
    /// <remarks>
    /// - PAID trên PayOS mà DB đang Pending: chốt thanh toán, chuyển đơn sang Confirmed.
    /// - CANCELLED/EXPIRED trên PayOS mà DB đang Pending: trả kho, chuyển đơn sang PaymentExpired.
    /// - Đơn đã chốt rồi: idempotent, không làm gì thêm.
    /// - Không tìm thấy payment trên PayOS: trả về null (chưa tạo phiên hoặc ID sai).
    /// </remarks>
    /// <param name="orderId">Id đơn hàng REWEAR cần đối soát.</param>
    /// <returns>Trạng thái thanh toán mới nhất sau khi đối soát, hoặc null nếu lỗi.</returns>
    Task<PaymentStatusResponse?> ReconcileAsync(string orderId);

    /// <summary>
    /// Lấy lịch sử thanh toán của đơn. Nếu truyền userId thì kiểm tra sở hữu.
    /// </summary>
    Task<PaymentHistoryResponse?> GetHistoryAsync(string orderId, string? userId);

    /// <summary>
    /// Đóng các phiên thanh toán đã hết hạn: trả sản phẩm về kho và chuyển đơn
    /// sang PaymentExpired. Gọi bởi background service.
    /// </summary>
    Task<int> ExpireStalePaymentsAsync(int limit = 100);

    /// <summary>
    /// Hoàn tiền cho một đơn đã thanh toán (dùng khi khách hủy đơn).
    /// </summary>
    Task<ApiResponse> RefundAsync(string orderId, string? reason);
}