namespace REWEAR.Application.DTOs;

// ============================================
// CART DTOs
// ============================================

/// <summary>
/// Request thêm sản phẩm vào giỏ hàng.
/// </summary>
public class AddToCartRequest
{
    /// <summary>
    /// Id sản phẩm cần thêm vào giỏ.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Số lượng mua (mặc định 1).
    /// </summary>
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Request cập nhật số lượng của một dòng trong giỏ.
/// </summary>
public class UpdateCartItemRequest
{
    /// <summary>
    /// Số lượng mới (phải >= 1).
    /// </summary>
    public int Quantity { get; set; }
}

/// <summary>
/// Request tick / bỏ tick một dòng giỏ hàng.
/// </summary>
public class SelectCartItemRequest
{
    /// <summary>
    /// true = tick (sẽ mua khi checkout), false = bỏ tick (chỉ để dành).
    /// </summary>
    public bool IsSelected { get; set; }
}

/// <summary>
/// Request tick / bỏ tick toàn bộ giỏ hàng cùng lúc.
/// </summary>
public class SelectAllCartItemsRequest
{
    /// <summary>
    /// true = tick tất cả, false = bỏ tick tất cả.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// (Tùy chọn) Chỉ áp dụng cho nhóm món còn khả dụng / không khả dụng.
    /// null = áp dụng cho toàn bộ giỏ.
    /// Cho phép FE gửi "tick tất cả những món còn mua được" mà không cần gọi 2 lần.
    /// </summary>
    public bool? OnlyAvailable { get; set; }
}

/// <summary>
/// Response một dòng trong giỏ, đã join với thông tin sản phẩm.
/// </summary>
public class CartItemResponse
{
    /// <summary>
    /// Khóa dòng giỏ (dùng cho PUT/DELETE theo id).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Dòng này có đang được tick để mua không.
    /// FE dùng để render checkbox và quyết định món nào được đưa vào đơn.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    // ====== Thông tin lấy từ Product ======
    public string ProductTitle { get; set; } = string.Empty;
    public string ProductSlug { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// Số món còn lại trong listing (Task 5 checkout dùng để chốt tồn kho).
    /// </summary>
    public int StockQuantity { get; set; } = 1;

    /// <summary>
    /// Số lượng trong giỏ có vượt quá tồn kho hiện tại không
    /// (người mua cần giảm số lượng trước khi thanh toán).
    /// </summary>
    public bool QuantityExceedsStock => Quantity > StockQuantity;

    /// <summary>
    /// Trạng thái thương mại hiện tại của sản phẩm (Available / Reserved / Sold).
    /// </summary>
    public string ProductStatus { get; set; } = string.Empty;

    /// <summary>
    /// Giá hiện tại của sản phẩm trong catalog.
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// Giá đã chụp lúc thêm vào giỏ.
    /// </summary>
    public decimal PriceSnapshot { get; set; }

    /// <summary>
    /// Giá đã thay đổi so với lúc thêm vào giỏ hay chưa.
    /// </summary>
    public bool PriceChanged { get; set; }

    /// <summary>
    /// Sản phẩm còn có thể mua được hay không (còn tồn tại + Available + IsActive).
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Lý do không mua được (nếu IsAvailable = false).
    /// </summary>
    public string? UnavailableReason { get; set; }

    /// <summary>
    /// Thành tiền = CurrentPrice * Quantity.
    /// </summary>
    public decimal LineTotal { get; set; }

    public DateTime AddedAt { get; set; }
}

/// <summary>
/// Response giỏ hàng đầy đủ cho frontend.
/// </summary>
public class CartResponse
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public List<CartItemResponse> Items { get; set; } = new();

    /// <summary>
    /// Tổng số lượng sản phẩm trong giỏ.
    /// </summary>
    public int TotalQuantity { get; set; }

    /// <summary>
    /// Tổng tiền tất cả sản phẩm còn mua được.
    /// </summary>
    public decimal SubTotal { get; set; }

    /// <summary>
    /// Tổng tiền chỉ tính các sản phẩm còn khả dụng (có thể thanh toán).
    /// </summary>
    public decimal AvailableSubTotal { get; set; }

    /// <summary>
    /// Có sản phẩm nào trong giỏ không còn khả dụng không.
    /// </summary>
    public bool HasUnavailableItems { get; set; }

    /// <summary>
    /// Có sản phẩm nào bị đổi giá so với lúc thêm vào giỏ không.
    /// </summary>
    public bool HasPriceChanged { get; set; }

    // ====== Thống kê theo nhóm đã tick ======

    /// <summary>
    /// Số món đang được tick để mua.
    /// </summary>
    public int SelectedQuantity { get; set; }

    /// <summary>
    /// Tổng tiền CHỈ những món đang được tick (nhóm sẽ được đưa vào đơn khi checkout).
    /// Đây là con số FE hiển thị ở ô "Tổng cộng" trên trang thanh toán.
    /// </summary>
    public decimal SelectedSubTotal { get; set; }

    /// <summary>
    /// Số món đang được tick nhưng không còn mua được (đã bán / bị gỡ / hết hàng).
    /// Frontend nên tự bỏ tick và cảnh báo người dùng trước khi bấm "Đặt hàng".
    /// </summary>
    public int SelectedUnavailableCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
