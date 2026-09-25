using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace REWEAR.Domain.Entities;

/// <summary>
/// Đại diện cho người dùng trong hệ thống REWEAR.
/// </summary>
public class User
{
    /// <summary>
    /// Khóa chính của người dùng.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Họ và tên đầy đủ.
    /// </summary>
    [BsonElement("fullName")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại.
    /// </summary>
    [BsonElement("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Ngày sinh (format: YYYY-MM-DD).
    /// </summary>
    [BsonElement("birthDate")]
    public string BirthDate { get; set; } = string.Empty;

    /// <summary>
    /// Email của người dùng (duy nhất, dùng để đăng nhập).
    /// </summary>
    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Mật khẩu (đã được hash).
    /// </summary>
    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái xác thực: false = chưa kích hoạt, true = đã kích hoạt.
    /// </summary>
    [BsonElement("isVerified")]
    public bool IsVerified { get; set; } = false;

    /// <summary>
    /// Mã OTP hiện tại (nếu có).
    /// </summary>
    [BsonElement("otpCode")]
    public string? OtpCode { get; set; }

    /// <summary>
    /// Thời điểm hết hạn OTP.
    /// </summary>
    [BsonElement("otpExpiresAt")]
    public DateTime? OtpExpiresAt { get; set; }

    /// <summary>
    /// Ngày tạo tài khoản.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ngày cập nhật gần nhất.
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
