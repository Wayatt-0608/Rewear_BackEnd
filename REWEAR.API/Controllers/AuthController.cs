using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

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
    /// Đăng ký tài khoản mới và gửi OTP về email
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
    /// Kích hoạt tài khoản bằng mã OTP
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
    /// Đăng nhập bằng email và mật khẩu
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
    /// Gửi OTP đặt lại mật khẩu
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var result = await _authService.ForgotPasswordAsync(request.Email);
        
        // Luôn trả về success để tránh tiết lộ email có tồn tại hay không
        return Ok(result);
    }

    /// <summary>
    /// Đặt lại mật khẩu bằng mã OTP
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
    /// Lấy danh sách tất cả người dùng (chỉ dùng để test)
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
