namespace REWEAR.Domain.Enums;

/// <summary>
/// Phương thức thanh toán của một đơn hàng.
/// REWEAR chỉ hỗ trợ thanh toán trực tuyến qua PayOS: khách phải trả tiền trước
/// thì mới mua được hàng (Task 8).
/// </summary>
public enum PaymentMethod
{
    /// <summary>Thanh toán trực tuyến qua cổng PayOS (QR, thẻ ngân hàng, ví điện tử).</summary>
    PayOs = 0
}