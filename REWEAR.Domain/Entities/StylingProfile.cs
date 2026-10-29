using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Hồ sơ sở thích thời trang (Styling Profile) của người dùng.
/// </summary>
/// <remarks>
/// <para>
/// Lưu collection RIÊNG ("StylingProfiles"), KHÔNG nhúng vào <c>User</c>:
/// - Đây là hồ sơ sở thích thời trang, khác với hồ sơ tài khoản (FullName, Avatar...)
///   vốn thuộc phạm vi module Profile/User của Backend 2.
/// - Cho phép mở rộng về sau (Recommendation, AI Styling) mà không phải sửa entity User.
/// </para>
/// <para>
/// Mỗi user có tối đa 1 hồ sơ: được MongoDB đảm bảo bằng unique index trên
/// <c>userId</c> của collection "StylingProfiles" (xem <c>MongoDbContext.EnsureIndexes</c>).
/// </para>
/// <para>
/// Mọi trường đều CÓ THỂ để trống: người dùng chỉ cần điền những gì họ muốn chia sẻ.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public class StylingProfile
{
    /// <summary>
    /// Khóa chính.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Id chủ hồ sơ (luôn lấy từ JWT, không nhận từ request).
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách phong cách thời trang người dùng thích.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Getter trả về danh sách rỗng thay vì null khi field bị ghi là null.
    /// </para>
    /// <para>
    /// Lý do: driver MongoDB có thể ghi thẳng vào backing field (không đi qua
    /// property setter) khi deserialize, nên chỉ chuẩn hóa trong setter là không
    /// đủ. Một document ghi tay có field null (hoặc document cũ) sẽ không được
    /// làm hỏng API đọc hồ sơ.
    /// </para>
    /// </remarks>
    [BsonElement("styles")]
    public List<string> Styles
    {
        get => _styles ?? new List<string>();
        set => _styles = value ?? new List<string>();
    }
    private List<string>? _styles = new();

    /// <summary>
    /// Danh sách kích cỡ quần áo người dùng thích (S, M, L, XL...).
    /// </summary>
    /// <remarks>
    /// Giữ dạng chuỗi vì <c>Product.Size</c> cũng là chuỗi tự do, nên hồ sơ có thể
    /// khớp trực tiếp với kích cỡ đang có trong catalog mà không cần bảng tra cứu.
    /// </remarks>
    [BsonElement("sizes")]
    public List<string> Sizes
    {
        get => _sizes ?? new List<string>();
        set => _sizes = value ?? new List<string>();
    }
    private List<string>? _sizes = new();

    /// <summary>
    /// Danh sách màu sắc người dùng thích.
    /// </summary>
    /// <remarks>
    /// Giữ dạng chuỗi vì <c>Product.Color</c> cũng là chuỗi tự do (màu tiếng Việt
    /// hoặc tiếng Anh đều được chấp nhận).
    /// </remarks>
    [BsonElement("colors")]
    public List<string> Colors
    {
        get => _colors ?? new List<string>();
        set => _colors = value ?? new List<string>();
    }
    private List<string>? _colors = new();

    /// <summary>
    /// Danh sách Id danh mục trang phục người dùng thích (tham chiếu <c>Category.Id</c>).
    /// </summary>
    [BsonElement("favoriteCategoryIds")]
    public List<string> FavoriteCategoryIds
    {
        get => _favoriteCategoryIds ?? new List<string>();
        set => _favoriteCategoryIds = value ?? new List<string>();
    }
    private List<string>? _favoriteCategoryIds = new();

    /// <summary>
    /// Ngân sách mua sắm tối thiểu (VND). Null = không giới hạn dưới.
    /// </summary>
    [BsonElement("minBudget")]
    public decimal? MinBudget { get; set; }

    /// <summary>
    /// Ngân sách mua sắm tối đa (VND). Null = không giới hạn trên.
    /// </summary>
    [BsonElement("maxBudget")]
    public decimal? MaxBudget { get; set; }

    /// <summary>
    /// Ghi chú tự do của người dùng về gu thẩm mỹ (không bắt buộc).
    /// </summary>
    [BsonElement("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Thời điểm tạo hồ sơ.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật gần nhất.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
