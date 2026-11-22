using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;

namespace REWEAR.API.Controllers;

[ApiController]
[EnableRateLimiting("auth")]
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
        
        return ToResult(result);
    }

    /// <summary>Gửi lại OTP cho tài khoản chưa xác thực; tối đa một lần mỗi phút theo tài khoản.</summary>
    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest request)
        => ToResult(await _authService.ResendOtpAsync(request.Email));

    private IActionResult ToResult(AuthResponse result)
    {
        if (result.Success) return Ok(result);
        if (result.RetryAfterSeconds is int retry && ControllerContext.HttpContext != null)
            Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return result.ErrorCode switch
        {
            "EMAIL_DELIVERY_FAILED" => StatusCode(StatusCodes.Status503ServiceUnavailable, result),
            "OTP_RESEND_COOLDOWN" => StatusCode(StatusCodes.Status429TooManyRequests, result),
            "OTP_REQUEST_CHANGED" => Conflict(result),
            _ => BadRequest(result)
        };
    }

    /// <summary>
    /// Kích hoạt tài khoản bằng mã OTP
    /// </summary>
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        var result = await _authService.VerifyOtpAsync(request);
        
        return ToResult(result);
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
        
        return ToResult(result);
    }

    /// <summary>
    /// Đặt lại mật khẩu bằng mã OTP
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _authService.ResetPasswordAsync(request);
        
        return ToResult(result);
    }

    /// <summary>
    /// Lấy danh sách tất cả người dùng (chỉ Admin)
    /// </summary>
    /// <remarks>
    /// Endpoint này tồn tại cùng với GET /api/admin/users (dùng cho Admin quản lý).
    /// Được giữ lại để tương thích với client cũ, nhưng giờ yêu cầu Role Admin.
    /// Không trả về PasswordHash, OTP hay bất kỳ thông tin bí mật nào.
    /// </remarks>
    [HttpGet("users")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAllUsers([FromServices] IUserRepository userRepository)
    {
        var users = await userRepository.GetAllAsync();
        return Ok(users.Select(u => new
        {
            u.Id,
            u.Email,
            u.FullName,
            Role = u.Role.ToString(),
            u.IsVerified,
            u.CreatedAt
        }));
    }
}
