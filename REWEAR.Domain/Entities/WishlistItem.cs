using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// 1 dòng wishlist: 1 user đã lưu 1 product.
/// </summary>
/// <remarks>
/// <para>
/// Lưu collection RIÊNG ("WishlistItems") thay vì embed vào User hay Cart:
/// - Cần truy vấn theo user (danh sách yêu thích) và theo (user, product) (kiểm tra tồn tại).
/// - Unique compound index (userId, productId) chặn được 2 request đồng thời tạo trùng.
/// </para>
/// <para>
/// KHÔNG lưu bản sao giá / ảnh / tồn kho của Product. Mọi thông tin hiển thị được đọc
/// LIVE từ collection Products tại thời điểm đọc, nên sản phẩm đổi giá hoặc bị bán vẫn
/// phản ánh đúng, và không phát sinh dữ liệu lệch.
/// </para>
/// <para>
/// Wishlist KHÔNG giữ hàng, KHÔNG reserve tồn kho, KHÔNG ảnh hưởng Cart/Order/Payment.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public class WishlistItem
{
    /// <summary>
    /// Khóa chính.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Id chủ wishlist (lấy từ JWT, không nhận từ request).
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Id sản phẩm được lưu vào wishlist.
    /// </summary>
    [BsonElement("productId")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm user lưu sản phẩm vào wishlist (dùng để sort mới nhất trước).
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
