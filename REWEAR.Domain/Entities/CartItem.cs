using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Một dòng sản phẩm trong giỏ hàng.
/// Được lưu nhúng (embedded) bên trong document Cart, không phải collection riêng.
/// </summary>
public class CartItem
{
    /// <summary>
    /// Khóa của dòng giỏ (dùng để xóa / cập nhật 1 item trong giỏ).
    /// </summary>
    [BsonElement("id")]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Id sản phẩm được thêm vào giỏ.
    /// </summary>
    [BsonElement("productId")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Số lượng mua. Với sản phẩm secondhand đơn lẻ thì thường = 1.
    /// </summary>
    [BsonElement("quantity")]
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Giá tại thời điểm thêm vào giỏ. Dùng để phát hiện sản phẩm bị đổi giá
    /// sau đó (Task Checkout sẽ validate lại với giá hiện tại).
    /// </summary>
    [BsonElement("priceSnapshot")]
    public decimal PriceSnapshot { get; set; }

    /// <summary>
    /// Trạng thái tick chọn của dòng này (mô hình giống Shopee: mua theo nhóm đã tick).
    /// Dùng nullable + [BsonIgnore] để tương thích ngược với giỏ cũ đã lưu trước khi
    /// có tính năng này: document cũ không có field "isSelected" sẽ deserialize thành null
    /// và được xem là ĐÃ CHỌN, giữ nguyên hành vi cũ (checkout toàn bộ giỏ).
    /// </summary>
    [BsonElement("isSelected")]
    public bool? IsSelectedFlag { get; set; }

    /// <summary>
    /// Dòng này có được tick để mua không.
    /// true  = sẽ được đưa vào đơn khi checkout.
    /// false = chỉ để dành trong giỏ, không mua trong lần checkout này.
    /// </summary>
    [BsonIgnore]
    public bool IsSelected
    {
        get => IsSelectedFlag ?? true;
        set => IsSelectedFlag = value;
    }

    /// <summary>
    /// Thời điểm thêm vào giỏ.
    /// </summary>
    [BsonElement("addedAt")]
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thành tiền của dòng này (không lưu xuống DB, tính lúc đọc).
    /// </summary>
    [BsonIgnore]
    public decimal LineTotal => PriceSnapshot * Quantity;
}
