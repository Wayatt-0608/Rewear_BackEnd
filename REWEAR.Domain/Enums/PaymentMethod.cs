namespace REWEAR.Domain.Enums;

/// <summary>
/// Phương thức thanh toán của một đơn hàng.
/// Thực thi thanh toán thật thuộc Task 8 (Payment), hiện Task 5 chỉ ghi nhận lựa chọn.
/// </summary>
public enum PaymentMethod
{
    /// <summary>Thanh toán khi nhận hàng (COD).</summary>
    COD = 0,

    /// <summary>Chuyển khoản ngân hàng (Task 8).</summary>
    Banking = 1,

    /// <summary>Ví Momo (Task 8).</summary>
    Momo = 2,

    /// <summary>Cổng thanh toán VNPay (Task 8).</summary>
    VNPay = 3
}