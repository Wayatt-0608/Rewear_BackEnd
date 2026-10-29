namespace REWEAR.Application.DTOs;

// ============================================
// STYLING PROFILE DTOs
// ============================================

/// <summary>
/// Request tạo mới hoặc cập nhật hồ sơ sở thích thời trang của người dùng đang đăng nhập.
/// </summary>
/// <remarks>
/// <para>
/// KHÔNG có trường <c>userId</c>: chủ hồ sơ luôn lấy từ JWT.
/// </para>
/// <para>
/// Ngữ nghĩa của PUT là <b>ghi đè toàn bộ</b> nội dung hồ sơ: trường nào không
/// được gửi (null) sẽ được xem là "không có sở thích" và bị xóa khỏi hồ sơ.
/// Muốn giữ lại một nhóm thông tin thì phải gửi lại nhóm đó.
/// </para>
/// <para>
/// Tất cả trường đều không bắt buộc — người dùng có thể chỉ điền một phần.
/// </para>
/// </remarks>
public class UpsertStylingProfileRequest
{
    /// <summary>
    /// Danh sách phong cách thời trang (Minimalist, Casual, Streetwear, Vintage, Sporty, Classic, Elegant).
    /// Không phân biệt hoa thường. Tối đa 10 phong cách.
    /// </summary>
    public List<string>? Styles { get; set; }

    /// <summary>
    /// Danh sách kích cỡ quần áo (S, M, L, XL...). Tối đa 20 kích cỡ.
    /// </summary>
    public List<string>? Sizes { get; set; }

    /// <summary>
    /// Danh sách màu sắc yêu thích. Tối đa 20 màu.
    /// </summary>
    public List<string>? Colors { get; set; }

    /// <summary>
    /// Danh sách Id danh mục trang phục yêu thích (Category.Id). Tối đa 20 danh mục.
    /// Mỗi id phải là ObjectId hợp lệ và tồn tại trong danh mục.
    /// </summary>
    public List<string>? FavoriteCategoryIds { get; set; }

    /// <summary>
    /// Ngân sách mua sắm tối thiểu (VND). Null = không giới hạn dưới.
    /// </summary>
    public decimal? MinBudget { get; set; }

    /// <summary>
    /// Ngân sách mua sắm tối đa (VND). Null = không giới hạn trên.
    /// </summary>
    public decimal? MaxBudget { get; set; }

    /// <summary>
    /// Ghi chú tự do về gu thẩm mỹ (tối đa 500 ký tự).
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Response hồ sơ sở thích thời trang của người dùng đang đăng nhập.
/// </summary>
public class StylingProfileResponse
{
    /// <summary>
    /// Id bản ghi hồ sơ.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách phong cách đã lưu.
    /// </summary>
    public List<string> Styles { get; set; } = new();

    /// <summary>
    /// Danh sách kích cỡ đã lưu.
    /// </summary>
    public List<string> Sizes { get; set; } = new();

    /// <summary>
    /// Danh sách màu sắc đã lưu.
    /// </summary>
    public List<string> Colors { get; set; } = new();

    /// <summary>
    /// Danh sách Id danh mục yêu thích.
    /// </summary>
    public List<string> FavoriteCategoryIds { get; set; } = new();

    /// <summary>
    /// Danh mục yêu thích kèm tên hiển thị (lấy live từ danh mục).
    /// Danh mục đã bị xóa sẽ không có trong danh sách này.
    /// </summary>
    public List<StylingProfileCategoryResponse> FavoriteCategories { get; set; } = new();

    /// <summary>
    /// Ngân sách tối thiểu (VND), null = không giới hạn.
    /// </summary>
    public decimal? MinBudget { get; set; }

    /// <summary>
    /// Ngân sách tối đa (VND), null = không giới hạn.
    /// </summary>
    public decimal? MaxBudget { get; set; }

    /// <summary>
    /// Ghi chú của người dùng.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Thời điểm tạo hồ sơ.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm cập nhật gần nhất.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Một danh mục trong danh sách danh mục yêu thích của Styling Profile.
/// </summary>
public class StylingProfileCategoryResponse
{
    /// <summary>
    /// Id danh mục.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Tên danh mục.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Slug danh mục.
    /// </summary>
    public string Slug { get; set; } = string.Empty;
}
