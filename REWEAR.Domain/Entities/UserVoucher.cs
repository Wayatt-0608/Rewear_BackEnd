using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;

/// <remarks>
/// Lý do dùng bảng riêng thay vì embed vào User:
/// - User có thể được phát nhiều voucher (danh sách dài).
/// - Cần tra cứu ngược: voucher này đã phát cho những user nào, ai đã dùng.
/// - Tách biệt rõ ràng quyền sở hữu và vòng đời voucher.
///
/// VÒNG ĐỜI 1 LƯỢT VOUCHER (3 trạng thái tách biệt):
/// - Available: đã cấp cho user, chưa gắn với đơn nào.
/// - Reserved : đang giữ chỗ cho 1 đơn CHỜ THANH TOÁN — chưa tiêu thụ thật.
/// - Used     : đã dùng thật cho 1 đơn ĐÃ THANH TOÁN — mới là lúc bị tiêu thụ.
///
/// Vì vậy "giữ chỗ" KHÔNG được ghi nhận là "đã dùng": nếu đơn bị hủy / hết hạn /
/// thanh toán lỗi thì voucher phải quay lại Available.
/// </remarks>
[BsonIgnoreExtraElements]
public class UserVoucher
{
    /// <summary>
    /// Khóa chính.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Id user được phát voucher.
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Id voucher.
    /// </summary>
    [BsonElement("voucherId")]
    public string VoucherId { get; set; } = string.Empty;

    // ====== THEO DÕI SỬ DỤNG ======

    /// <summary>
    /// Trạng thái hiện tại của lượt voucher này (xem <see cref="UserVoucherStatus"/>).
    /// </summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public UserVoucherStatus Status { get; set; } = UserVoucherStatus.Available;

    /// <summary>
    /// Số lần user đã dùng THẬT voucher này. Dùng để kiểm tra UsageLimitPerUser.
    /// </summary>
    [BsonElement("usageCount")]
    public int UsageCount { get; set; } = 0;

    /// <summary>
    /// Id đơn hàng đang gắn voucher (đang giữ chỗ hoặc đã dùng). Null = chưa gắn đơn nào.
    /// </summary>
    [BsonElement("orderId")]
    public string? OrderId { get; set; }

    /// <summary>
    /// Thời điểm giữ chỗ cho đơn hàng (null = không giữ chỗ).
    /// </summary>
    [BsonElement("reservedAt")]
    public DateTime? ReservedAt { get; set; }

    /// <summary>
    /// Thời điểm dùng thật (null = chưa dùng thật).
    /// </summary>
    [BsonElement("usedAt")]
    public DateTime? UsedAt { get; set; }

    // ====== CẤP PHÁT ======

    /// <summary>
    /// Thời điểm admin/staff phát voucher cho user.
    /// </summary>
    [BsonElement("grantedAt")]
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Id người phát (admin). Có thể null nếu phát hàng loạt qua batch job.
    /// </summary>
    [BsonElement("grantedBy")]
    public string? GrantedBy { get; set; }

    /// <summary>
    /// True nếu lượt voucher này đã bị tiêu thụ thật (đơn đã thanh toán thành công).
    /// </summary>
    [BsonIgnore]
    public bool IsUsed => Status == UserVoucherStatus.Used;
}
