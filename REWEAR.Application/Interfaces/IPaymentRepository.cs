using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Repository cho Payment (Task 8).
/// </summary>
public interface IPaymentRepository
{
    /// <summary>Tạo bản ghi thanh toán mới (mỗi lần tạo phiên thanh toán là 1 record).</summary>
    Task CreateAsync(Payment payment);

    /// <summary>Cập nhật bản ghi thanh toán.</summary>
    Task UpdateAsync(Payment payment);

    /// <summary>Lấy phiên thanh toán mới nhất của đơn (có thể đã hết hạn).</summary>
    Task<Payment?> GetLatestByOrderIdAsync(string orderId);

    /// <summary>Lấy toàn bộ lịch sử thanh toán của đơn, mới nhất trước.</summary>
    Task<List<Payment>> GetByOrderIdAsync(string orderId);

    /// <summary>
    /// Tìm phiên thanh toán theo paymentLinkId do PayOS cấp.
    /// Dùng cho đường đối soát chủ động khi webhook bị mất.
    /// </summary>
    Task<Payment?> GetByPaymentLinkIdAsync(string paymentLinkId);

    /// <summary>
    /// Lấy các phiên thanh toán đã hết hạn mà chưa được xử lý
    /// (để background job đóng phiên và trả sản phẩm về kho).
    /// </summary>
    Task<List<Payment>> GetExpiredUnpaidAsync(int limit);

    /// <summary>Đếm số phiên thanh toán thành công (dashboard Task 18).</summary>
    Task<long> CountByStatusAsync(PaymentStatus status);
}