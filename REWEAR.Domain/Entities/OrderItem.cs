using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Một dòng sản phẩm trong đơn hàng.
/// Được lưu nhúng (embedded) bên trong document Order, không phải collection riêng.
/// </summary>
public class OrderItem
{
    /// <summary>
    /// Khóa của dòng đơn hàng.
    /// </summary>
    [BsonElement("id")]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Id sản phẩm. Chỉ là tham chiếu để tra cứu lại; thông tin hiển thị
    /// được snapshot bên dưới nên đơn hàng không bị ảnh hưởng khi sản phẩm bị sửa/xóa.
    /// </summary>
    [BsonElement("productId")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Số lượng mua.
    /// </summary>
    [BsonElement("quantity")]
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Giá đơn vị TẠI THỜI ĐIỂM CHECKOUT (không phải giá trong giỏ).
    /// Đơn hàng là bản ghi lịch sử tài chính nên phải giữ nguyên giá đã chốt.
    /// </summary>
    [BsonElement("unitPrice")]
    public decimal UnitPrice { get; set; }

    // ====== SNAPSHOT THÔNG TIN SẢN PHẨM ======

    /// <summary>Tên sản phẩm lúc đặt hàng.</summary>
    [BsonElement("productTitle")]
    public string ProductTitle { get; set; } = string.Empty;

    [BsonElement("productSlug")]
    public string ProductSlug { get; set; } = string.Empty;

    [BsonElement("productImageUrl")]
    public string? ProductImageUrl { get; set; }

    [BsonElement("size")]
    public string? Size { get; set; }

    [BsonElement("color")]
    public string? Color { get; set; }

    [BsonElement("condition")]
    public string Condition { get; set; } = string.Empty;

    // ====== TÍNH TOÁN (không lưu xuống DB) ======

    /// <summary>
    /// Thành tiền của dòng này = UnitPrice * Quantity.
    /// </summary>
    [BsonIgnore]
    public decimal LineTotal => UnitPrice * Quantity;
}