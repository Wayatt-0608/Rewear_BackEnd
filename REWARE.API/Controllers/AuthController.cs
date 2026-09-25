using Microsoft.AspNetCore.Mvc;
using REWARE.Application.DTOs;
using REWARE.Application.Interfaces;

namespace REWARE.API.Controllers;

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
