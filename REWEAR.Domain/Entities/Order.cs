using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using REWEAR.Domain.Enums;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Một đơn hàng của người mua, được tạo ra từ giỏ hàng tại bước Checkout (Task 5).
/// Lưu 1 document chứa mảng OrderItem nhúng bên trong.
/// </summary>
public class Order
{
    /// <summary>
    /// Khóa chính của đơn hàng.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Mã đơn hiển thị cho người dùng (vd: "RW-20261005-A1B2C3"), tách khỏi Id kỹ thuật.
    /// </summary>
    [BsonElement("orderCode")]
    public string OrderCode { get; set; } = string.Empty;

    /// <summary>
    /// Id chủ đơn hàng.
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách sản phẩm trong đơn.
    /// </summary>
    [BsonElement("items")]
    public List<OrderItem> Items { get; set; } = new();

    // ====== ĐỊA CHỈ GIAO HÀNG (SNAPSHOT) ======
    // Snapshot toàn bộ thay vì chỉ lưu AddressId: nếu user sửa hoặc xóa địa chỉ
    // sau khi đặt hàng thì đơn cũ vẫn giữ đúng địa chỉ đã giao.

    [BsonElement("shippingRecipientName")]
    public string ShippingRecipientName { get; set; } = string.Empty;

    [BsonElement("shippingPhoneNumber")]
    public string ShippingPhoneNumber { get; set; } = string.Empty;

    [BsonElement("shippingProvince")]
    public string ShippingProvince { get; set; } = string.Empty;

    [BsonElement("shippingDistrict")]
    public string ShippingDistrict { get; set; } = string.Empty;

    [BsonElement("shippingWard")]
    public string ShippingWard { get; set; } = string.Empty;

    [BsonElement("shippingStreetAddress")]
    public string ShippingStreetAddress { get; set; } = string.Empty;

    [BsonElement("shippingNote")]
    public string? ShippingNote { get; set; }

    /// <summary>
    /// Id của địa chỉ gốc (chỉ để tra cứu, không phải nguồn dữ liệu hiển thị).
    /// </summary>
    [BsonElement("addressId")]
    public string? AddressId { get; set; }

    /// <summary>
    /// Địa chỉ đầy đủ đã ghép, phục vụ in label vận chuyển (Task 9).
    /// </summary>
    [BsonIgnore]
    public string FullShippingAddress =>
        $"{ShippingStreetAddress}, {ShippingWard}, {ShippingDistrict}, {ShippingProvince}";

    // ====== THANH TOÁN & TỔNG TIỀN ======

    /// <summary>
    /// Phương thức thanh toán đã chọn. Thực thi thanh toán thật thuộc Task 8.
    /// </summary>
    [BsonElement("paymentMethod")]
    [BsonRepresentation(BsonType.String)]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.COD;

    /// <summary>
    /// Tổng tiền hàng trước khi áp voucher (Task 10) và phí vận chuyển (Task 9).
    /// </summary>
    [BsonElement("subTotal")]
    public decimal SubTotal { get; set; }

    /// <summary>
    /// Số tiền giảm sau voucher (Task 10). Hiện luôn = 0.
    /// </summary>
    [BsonElement("discountAmount")]
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Phí vận chuyển (Task 9). Hiện luôn = 0.
    /// </summary>
    [BsonElement("shippingFee")]
    public decimal ShippingFee { get; set; }

    /// <summary>
    /// Tổng tiền phải trả = SubTotal - DiscountAmount + ShippingFee.
    /// </summary>
    [BsonElement("totalAmount")]
    public decimal TotalAmount { get; set; }

    // ====== TRẠNG THÁI ======

    /// <summary>
    /// Trạng thái vòng đời đơn hàng.
    /// </summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// Lý do hủy đơn (nếu Status = Cancelled).
    /// </summary>
    [BsonElement("cancelReason")]
    public string? CancelReason { get; set; }

    /// <summary>
    /// Thời điểm tạo đơn.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật gần nhất.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ====== THUỘC TÍNH TÍNH TOÁN ======

    /// <summary>
    /// Tổng số lượng sản phẩm trong đơn.
    /// </summary>
    [BsonIgnore]
    public int TotalQuantity => Items.Sum(i => i.Quantity);

    /// <summary>
    /// Đơn đã ở trạng thái kết thúc chưa (không còn cho phép thao tác).
    /// </summary>
    [BsonIgnore]
    public bool IsFinalized => Status is OrderStatus.Delivered or OrderStatus.Cancelled;

    /// <summary>
    /// Tạo mã đơn hàng dạng RW-YYYYMMDD-XXXXXX.
    /// </summary>
    public static string GenerateOrderCode()
    {
        string datePart = DateTime.UtcNow.ToString("yyyyMMdd");
        string randomPart = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"RW-{datePart}-{randomPart}";
    }
}