namespace REWEAR.Application.Interfaces;

/// <summary>
/// Cấu hình cổng thanh toán mà tầng Application cần biết (Task 8).
/// Được implement trong Infrastructure từ appsettings.json để Application
/// không phụ thuộc trực tiếp vào lớp hạ tầng.
/// </summary>
public interface IPaymentGatewayConfig
{
    /// <summary>
    /// Base URL của ứng dụng, dùng để dựng returnUrl / cancelUrl gửi cho PayOS.
    /// Ví dụ: https://api.rewear.vn
    /// </summary>
    string AppBaseUrl { get; }
}