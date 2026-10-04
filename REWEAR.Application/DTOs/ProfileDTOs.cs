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
    public string? PhoneNumber { get; set; }
    public string? BirthDate { get; set; }
    public string? Gender { get; set; }
}

/// <summary>
/// Response thông tin profile user.
/// </summary>
public class ProfileResponse
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
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
}
