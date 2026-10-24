namespace REWEAR.Domain.Enums;

/// <summary>
/// Loại giảm giá của voucher (Task 10).
/// </summary>
public enum VoucherType
{
    /// <summary>
    /// Giảm theo phần trăm trên tổng đơn (vd: Value = 15 → giảm 15%).
    /// Kết hợp với MinDiscount / MaxDiscount để ra số tiền thực tế.
    /// </summary>
    Percentage = 0,

    /// <summary>
    /// Giảm số tiền cố định (vd: Value = 50000 → giảm 50,000đ).
    /// Vẫn áp dụng MinDiscount để đảm bảo user có lợi (vd: voucher 30k nhưng
    /// min 40k thì hệ thống tự nâng lên 40k).
    /// </summary>
    FixedAmount = 1
}
