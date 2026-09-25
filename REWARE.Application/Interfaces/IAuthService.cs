using REWARE.Application.DTOs;
using REWEAR.Domain.Entities;

namespace REWARE.Application.Interfaces;

/// <summary>
/// Interface cho Authentication Service.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Đăng ký tài khoản mới và gửi OTP qua email.
    /// </summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Kích hoạt tài khoản bằng mã OTP.
    /// </summary>
    Task<AuthResponse> VerifyOtpAsync(VerifyOtpRequest request);

    /// <summary>
    /// Đăng nhập (chỉ tài khoản đã xác thực mới được đăng nhập).
    /// </summary>
    Task<AuthResponse> LoginAsync(LoginRequest request);
}
