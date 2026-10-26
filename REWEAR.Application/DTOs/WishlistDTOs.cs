namespace REWEAR.Application.DTOs;

// ============================================
// WISHLIST DTOs
// ============================================

/// <summary>
/// Query parameters cho API lấy danh sách wishlist của tôi.
/// </summary>
public class WishlistQueryParameters
{
    /// <summary>
    /// Trang hiện tại (bắt đầu từ 1).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Số bản ghi mỗi trang (tối đa 100).
    /// </summary>
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Một item wishlist đã join với thông tin Product hiện tại.
/// </summary>
/// <remarks>
/// KHÔNG chứa dữ liệu nội bộ của Product (acquisition cost, production cost, admin note...).
/// Nếu Product đã bị xóa cứng khỏi catalog thì chỉ trả thông tin tối thiểu an toàn.
/// </remarks>
public class WishlistItemResponse
{
    /// <summary>
    /// Id sản phẩm.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Tên sản phẩm (rỗng nếu sản phẩm không còn tồn tại trong catalog).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Giá hiện tại của sản phẩm (0 nếu sản phẩm không còn tồn tại).
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Ảnh đầu tiên của sản phẩm (null nếu không có).
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Size của sản phẩm.
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// Màu sắc của sản phẩm.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Sản phẩm còn mua được hay không.
    /// True khi: IsActive && Status == Available && StockQuantity > 0.
    /// </summary>
    public bool IsAvailableForPurchase { get; set; }

    /// <summary>
    /// Thời điểm user lưu sản phẩm vào wishlist.
    /// </summary>
    public DateTime AddedAt { get; set; }
}

/// <summary>
/// Kết quả kiểm tra 1 sản phẩm có nằm trong wishlist của tôi không.
/// </summary>
public class WishlistCheckResponse
{
    /// <summary>
    /// Id sản phẩm được kiểm tra.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// true = đang có trong wishlist, false = chưa có.
    /// </summary>
    public bool IsWishlisted { get; set; }
}

/// <summary>
/// Response phân trang cho danh sách wishlist của tôi.
/// </summary>
public class PagedWishlistResponse
{
    /// <summary>
    /// Danh sách item trong trang hiện tại.
    /// </summary>
    public List<WishlistItemResponse> Items { get; set; } = new();

    /// <summary>
    /// Tổng số item trong wishlist của user.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Trang hiện tại.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Số bản ghi mỗi trang.
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Tổng số trang.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    /// <summary>
    /// Có trang trước không.
    /// </summary>
    public bool HasPrevious => Page > 1;

    /// <summary>
    /// Có trang sau không.
    /// </summary>
    public bool HasNext => Page < TotalPages;
}
