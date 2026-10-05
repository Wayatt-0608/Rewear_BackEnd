namespace REWEAR.Application.DTOs;

// ============================================
// USER PROFILE DTOs
// ============================================

/// <summary>
/// Request cập nhật thông tin cá nhân.
/// </summary>
public class UpdateProfileRequest
{
    public string? FullName { get; set; }
    public string? Gender { get; set; }
}

/// <summary>
/// Response thông tin profile user.
/// </summary>
public class ProfileResponse
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ============================================
// ADDRESS DTOs
// ============================================

/// <summary>
/// Request tạo/cập nhật địa chỉ.
/// </summary>
public class AddressRequest
{
    public string RecipientName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string StreetAddress { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool IsDefault { get; set; } = false;
}

/// <summary>
/// Response thông tin địa chỉ.
/// </summary>
public class AddressResponse
{
    public string Id { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string StreetAddress { get; set; } = string.Empty;
    public string FullAddress { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Response API chung.
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }

    /// <summary>
    /// Mã lỗi nghiệp vụ để Controller chọn đúng HTTP status code
    /// (thay vì đoán qua nội dung Message).
    /// Xem <see cref="ApiErrorCode"/> cho danh sách mã.
    /// </summary>
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Các mã lỗi nghiệp vụ dùng chung cho ApiResponse.ErrorCode.
/// Controller map mã này sang HTTP status code tương ứng.
/// </summary>
public static class ApiErrorCode
{
    /// <summary>Dữ liệu đầu vào không hợp lệ (sai định dạng, thiếu trường, vượt giới hạn).</summary>
    public const string Validation = "VALIDATION_ERROR";

    /// <summary>Không tìm thấy tài nguyên theo Id (vd: mục giỏ hàng không tồn tại).</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Không có quyền truy cập tài nguyên.</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>Xung đột trạng thái (vd: email đã tồn tại, sản phẩm đã được bán).</summary>
    public const string Conflict = "CONFLICT";
}
