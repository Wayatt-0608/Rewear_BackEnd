namespace REWARE.Application.DTOs;

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
    public bool? RequiresVerification { get; set; }
}
