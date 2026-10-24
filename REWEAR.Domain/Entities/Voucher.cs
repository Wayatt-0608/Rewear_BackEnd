using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;


/// <remarks>
/// Hệ thống dùng mô hình giống Shopee:
/// - <see cref="Type"/> = Percentage: giảm theo % (vd: 15%).
/// - <see cref="Type"/> = FixedAmount: giảm số tiền cố định (vd: 50,000đ).
/// - <see cref="MinOrderAmount"/>: đơn phải đạt tối thiểu mới được dùng.
/// - <see cref="MinDiscount"/>: số tiền giảm tối thiểu (áp dụng cho cả 2 loại
///   để user không bị "lỗ" khi % quá nhỏ, vd: đơn 250k * 15% = 37,500đ nhưng
///   hệ thống sẽ nâng lên 40,000đ).
/// - <see cref="MaxDiscount"/>: trần giảm tối đa (chỉ ý nghĩa với Percentage,
///   vd: đơn 2tr * 15% = 300k nhưng max 100k thì chỉ giảm 100k).
/// - <see cref="UsageLimit"/>: tổng lượt được dùng (toàn hệ thống).
/// - <see cref="UsageLimitPerUser"/>: mỗi user được dùng tối đa bao nhiêu lần.
/// </remarks>
[BsonIgnoreExtraElements]
public class Voucher
{
    /// <summary>
    /// Khóa chính.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Mã voucher hiển thị, viết HOA không dấu (vd: "TET2026", "FREESHIP").
    /// Unique để user nhập / FE tìm chính xác.
    /// </summary>
    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Tên hiển thị cho admin và user (vd: "Tết 2026 - Giảm 15% đơn từ 400k").
    /// </summary>
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết (vd: "Áp dụng cho mọi sản phẩm. Không áp dụng cho đơn có
    /// voucher khác."). Có thể null.
    /// </summary>
    [BsonElement("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Loại giảm giá: Percentage hoặc FixedAmount.
    /// </summary>
    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public VoucherType Type { get; set; } = VoucherType.Percentage;

    /// <summary>
    /// Giá trị giảm:
    /// - Percentage: số phần trăm (vd: 15 = 15%).
    /// - FixedAmount: số tiền cố định (vd: 50000 = 50,000đ).
    /// </summary>
    [BsonElement("value")]
    public decimal Value { get; set; }

    // ====== ĐIỀU KIỆN ĐƠN HÀNG ======

    /// <summary>
    /// Tổng đơn tối thiểu mới được dùng voucher (vd: 400,000đ).
    /// Nếu đơn nhỏ hơn → không đủ điều kiện.
    /// </summary>
    [BsonElement("minOrderAmount")]
    public decimal MinOrderAmount { get; set; } = 0m;

    /// <summary>
    /// Số tiền giảm tối thiểu (áp dụng cho cả Percentage và FixedAmount).
    /// Tránh trường hợp đơn nhỏ * % nhỏ = vài nghìn → quá lỗ với shop.
    /// VD: đơn 250k * 15% = 37,500đ nhưng MinDiscount = 40,000đ → giảm 40k.
    /// </summary>
    [BsonElement("minDiscount")]
    public decimal MinDiscount { get; set; } = 0m;

    /// <summary>
    /// Số tiền giảm tối đa (chủ yếu cho Percentage).
    /// VD: đơn 2tr * 15% = 300k nhưng MaxDiscount = 100k → chỉ giảm 100k.
    /// FixedAmount thường bỏ qua (để 0 = không giới hạn).
    /// </summary>
    [BsonElement("maxDiscount")]
    public decimal MaxDiscount { get; set; } = 0m;

    // ====== THỜI HẠN ======

    /// <summary>
    /// Ngày bắt đầu áp dụng. Trước mốc này voucher chưa dùng được.
    /// </summary>
    [BsonElement("startDate")]
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Ngày hết hạn. Sau mốc này voucher không dùng được.
    /// </summary>
    [BsonElement("expiryDate")]
    public DateTime ExpiryDate { get; set; }

    // ====== GIỚI HẠN SỬ DỤNG ======

    /// <summary>
    /// Tổng lượt được dùng trên toàn hệ thống.
    /// 0 = không giới hạn.
    /// </summary>
    [BsonElement("usageLimit")]
    public int UsageLimit { get; set; } = 0;

    /// <summary>
    /// Số lần đã được DÙNG THẬT (đơn đã thanh toán thành công).
    /// Chỉ tăng khi thanh toán thành công, KHÔNG tăng lúc giữ chỗ.
    /// </summary>
    [BsonElement("usedCount")]
    public int UsedCount { get; set; } = 0;

    /// <summary>
    /// Số lượt đang được GIỮ CHỖ cho các đơn chờ thanh toán.
    /// Được tính vào giới hạn tổng (UsedCount + ReservedCount) để không oversell
    /// voucher, nhưng KHÔNG phải lượt đã dùng thật.
    /// </summary>
    [BsonElement("reservedCount")]
    public int ReservedCount { get; set; } = 0;

    /// <summary>
    /// Mỗi user được dùng tối đa bao nhiêu lần.
    /// 0 = không giới hạn. Mặc định 1 để tránh spam.
    /// </summary>
    [BsonElement("usageLimitPerUser")]
    public int UsageLimitPerUser { get; set; } = 1;

    // ====== TRẠNG THÁI ======

    /// <summary>
    /// Voucher còn được phép dùng hay không.
    /// Admin có thể tắt thủ công; Background service tự tắt khi hết hạn.
    /// </summary>
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    // ====== AUDIT ======

    /// <summary>
    /// Id admin đã tạo voucher.
    /// </summary>
    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ====== THUỘC TÍNH TÍNH TOÁN ======

    /// <summary>
    /// Voucher còn trong thời gian hiệu lực hay không (theo thời điểm hiện tại).
    /// </summary>
    [BsonIgnore]
    public bool IsWithinValidDateRange
    {
        get
        {
            var now = DateTime.UtcNow;
            return now >= StartDate && now <= ExpiryDate;
        }
    }

    /// <summary>
    /// Voucher còn lượt dùng hay không (tính cả lượt đã dùng thật và lượt đang giữ chỗ).
    /// </summary>
    [BsonIgnore]
    public bool HasRemainingUsage =>
        UsageLimit <= 0 || (UsedCount + ReservedCount) < UsageLimit;

    /// <summary>
    /// Số lượt còn có thể cấp thêm (đã trừ cả lượt đã dùng và lượt đang giữ chỗ).
    /// -1 = không giới hạn.
    /// </summary>
    [BsonIgnore]
    public int RemainingUsage => UsageLimit <= 0
        ? -1
        : Math.Max(0, UsageLimit - UsedCount - ReservedCount);

    /// <summary>
    /// Tạo mã voucher ngẫu nhiên (8 ký tự A-Z + 0-9). Dùng làm gợi ý khi admin
    /// tạo voucher mới, FE có thể dùng làm placeholder.
    /// </summary>
    public static string GenerateRandomCode(int length = 8)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        var buffer = new char[length];
        for (int i = 0; i < length; i++)
        {
            buffer[i] = chars[random.Next(chars.Length)];
        }
        return new string(buffer);
    }
}
