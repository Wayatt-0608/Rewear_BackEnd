using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Địa chỉ giao hàng của người dùng.
/// </summary>
[BsonIgnoreExtraElements]
public class Address
{
    /// <summary>
    /// Khóa chính.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// ID của người dùng sở hữu địa chỉ này.
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Tên người nhận hàng.
    /// </summary>
    [BsonElement("recipientName")]
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại người nhận.
    /// </summary>
    [BsonElement("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Tỉnh/Thành phố.
    /// </summary>
    [BsonElement("province")]
    public string Province { get; set; } = string.Empty;

    /// <summary>
    /// Quận/Huyện.
    /// </summary>
    [BsonElement("district")]
    public string District { get; set; } = string.Empty;

    /// <summary>
    /// Phường/Xã.
    /// </summary>
    [BsonElement("ward")]
    public string Ward { get; set; } = string.Empty;

    /// <summary>
    /// Địa chỉ chi tiết (số nhà, tên đường...).
    /// </summary>
    [BsonElement("streetAddress")]
    public string StreetAddress { get; set; } = string.Empty;

    /// <summary>
    /// Ghi chú cho đơn vị vận chuyển (nếu có).
    /// </summary>
    [BsonElement("note")]
    public string? Note { get; set; }

    /// <summary>
    /// Có phải địa chỉ mặc định hay không.
    /// </summary>
    [BsonElement("isDefault")]
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Ngày tạo.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ngày cập nhật gần nhất.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tạo địa chỉ đầy đủ từ các trường thành phần.
    /// </summary>
    [BsonIgnore]
    public string FullAddress => $"{StreetAddress}, {Ward}, {District}, {Province}";
}
