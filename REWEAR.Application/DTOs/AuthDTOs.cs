namespace REWEAR.Application.DTOs;

/// <summary>
/// DTO cho yêu cầu đăng ký.
/// </summary>
public class RegisterRequest
{
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;  // Format: YYYY-MM-DD
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PasswordConfirm { get; set; } = string.Empty;

    /// <summary>
    /// Role tùy chọn (chỉ dùng để test qua Swagger).
    /// Frontend KHÔNG gửi field này → sẽ null → mặc định Member.
    /// Giá trị hợp lệ: "Admin", "Staff", "Shipper", "Member".
    /// Admin upgrade role chính thức qua API: PUT /api/admin/users/{id}/role
    /// </summary>
    public string? Role { get; set; }
}

/// <summary>
/// DTO cho yêu cầu kích hoạt tài khoản bằng OTP.
/// </summary>
public class VerifyOtpRequest
{
    public string Email { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
}

/// <summary>
/// DTO cho yêu cầu đăng nhập.
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// DTO phản hồi cho Auth operations.
/// </summary>
public class AuthResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? Role { get; set; }  // Vai trò: Member, Admin, Staff, Shipper
    public bool? RequiresVerification { get; set; }
    public string? Token { get; set; }  // JWT Token
}

/// <summary>
/// DTO cho yêu cầu đặt lại mật khẩu (dùng chung OTP).
/// </summary>
public class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string NewPasswordConfirm { get; set; } = string.Empty;
}

/// <summary>
/// DTO cho yêu cầu quên mật khẩu.
/// </summary>
public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}
