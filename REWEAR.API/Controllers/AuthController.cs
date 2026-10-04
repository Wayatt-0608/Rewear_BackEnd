using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

/// <summary>
/// Controller xử lý Authentication (Đăng ký, Đăng nhập, Xác thực OTP).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Đăng ký tài khoản mới và gửi OTP qua email.
    /// POST /api/auth/register
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Kích hoạt tài khoản bằng mã OTP.
    /// POST /api/auth/verify-otp
    /// </summary>
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        var result = await _authService.VerifyOtpAsync(request);
        
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Đăng nhập (chỉ tài khoản đã xác thực mới được đăng nhập).
    /// POST /api/auth/login
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        
        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Gửi OTP đặt lại mật khẩu.
    /// POST /api/auth/forgot-password
    /// Body: { "email": "user@example.com" }
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var result = await _authService.ForgotPasswordAsync(request.Email);
        
        // Luôn trả về success để tránh tiết lộ email có tồn tại hay không
        return Ok(result);
    }

    /// <summary>
    /// Đặt lại mật khẩu bằng OTP.
    /// POST /api/auth/reset-password
    /// Body: { "email": "user@example.com", "otpCode": "123456", "newPassword": "...", "newPasswordConfirm": "..." }
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _authService.ResetPasswordAsync(request);
        
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách tất cả users (để test).
    /// GET /api/auth/users
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers([FromServices] IUserRepository userRepository)
    {
        var users = await userRepository.GetAllAsync();
        return Ok(users.Select(u => new 
        {
            u.Id,
            u.Email,
            u.FullName,
            u.IsVerified,
            u.CreatedAt
        }));
    }
}
