namespace REWEAR.Domain.Enums;

/// <summary>
/// Trạng thái của yêu cầu thu mua đồ cũ (Sourcing Request).
/// </summary>
public enum SourcingStatus
{
    /// <summary>
    /// Mới gửi yêu cầu, đang chờ REWEAR review.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// REWEAR đang review/thẩm định request.
    /// </summary>
    UnderReview = 1,

    /// <summary>
    /// REWEAR đã đưa OfferedPrice, chờ customer quyết định.
    /// </summary>
    Priced = 2,

    /// <summary>
    /// Customer đã đồng ý OfferedPrice.
    /// </summary>
    Accepted = 3,

    /// <summary>
    /// REWEAR đã nhận được physical item.
    /// </summary>
    Received = 4,

    /// <summary>
    /// REWEAR đã kiểm định và chấp nhận item để bán.
    /// </summary>
    Approved = 5,

    /// <summary>
    /// Request đã được convert thành Product.
    /// </summary>
    Completed = 6,

    /// <summary>
    /// REWEAR/Admin/Staff từ chối request.
    /// </summary>
    Rejected = 7,

    /// <summary>
    /// Customer từ chối OfferedPrice.
    /// </summary>
    Declined = 8
}
