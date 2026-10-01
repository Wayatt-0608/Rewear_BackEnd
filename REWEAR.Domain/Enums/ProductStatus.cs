namespace REWEAR.Domain.Enums;

/// <summary>
/// Trạng thái thương mại của sản phẩm.
/// </summary>
public enum ProductStatus
{
    /// <summary>
    /// Có thể mua.
    /// </summary>
    Available = 0,

    /// <summary>
    /// Đang được giữ trong quá trình giao dịch.
    /// </summary>
    Reserved = 1,

    /// <summary>
    /// Đã bán.
    /// </summary>
    Sold = 2
}
