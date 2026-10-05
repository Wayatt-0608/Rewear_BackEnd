using Microsoft.IdentityModel.Tokens;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý đăng nhập, đăng ký, xác thực OTP, logout, forgot/reset password.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;

    // Thời gian hết hạn OTP: 5 phút
    private const int OTP_EXPIRY_MINUTES = 5;

    // JWT Configuration
    private const string JWT_SECRET = "RewearSecretKey2024!@#$%^&*()_+MinLength32Chars";
    private const string JWT_ISSUER = "RewearAPI";
    private const string JWT_AUDIENCE = "RewearApp";
    private const int JWT_EXPIRY_HOURS = 24;

    public AuthService(IUserRepository userRepository, IEmailService emailService)
    {
        _userRepository = userRepository;
        _emailService = emailService;
    }

    /// <summary>
    /// Đăng ký tài khoản mới với xác thực email.
    /// </summary>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // ===== VALIDATION =====

        // 1. Kiểm tra rỗng
        if (string.IsNullOrWhiteSpace(request.FullName))
            return new AuthResponse { Success = false, Message = "Họ và tên không được để trống." };

        if (string.IsNullOrWhiteSpace(request.Email))
            return new AuthResponse { Success = false, Message = "Email không được để trống." };

        if (string.IsNullOrWhiteSpace(request.Password))
            return new AuthResponse { Success = false, Message = "Mật khẩu không được để trống." };

        if (string.IsNullOrWhiteSpace(request.PasswordConfirm))
            return new AuthResponse { Success = false, Message = "Xác nhận mật khẩu không được để trống." };

        // 2. Validate email format
        if (!Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return new AuthResponse { Success = false, Message = "Email không hợp lệ." };

        // 3. Validate password strength
        if (request.Password.Length < 6)
            return new AuthResponse { Success = false, Message = "Mật khẩu phải có ít nhất 6 ký tự." };

        // 6. Kiểm tra password match
        if (request.Password != request.PasswordConfirm)
            return new AuthResponse { Success = false, Message = "Mật khẩu và xác nhận mật khẩu không khớp." };

        // 7. Kiểm tra email đã tồn tại
        var existingUser = await _userRepository.GetByEmailAsync(request.Email);
        if (existingUser != null)
            return new AuthResponse { Success = false, Message = "Email đã được sử dụng." };

        // ===== TẠO OTP =====
        string otpCode = GenerateOtpCode();
        DateTime otpExpiry = DateTime.UtcNow.AddMinutes(OTP_EXPIRY_MINUTES);

        // ===== TẠO USER =====
        // Parse role từ request: nếu null/rỗng/invalid → mặc định Member.
        // Frontend KHÔNG gửi field role (sẽ null) → luôn set Member.
        // Swagger có thể gửi role bất kỳ để test nhanh.
        // Admin upgrade role chính thức qua: PUT /api/admin/users/{id}/role
        UserRole userRole = UserRole.Member;
        if (!string.IsNullOrWhiteSpace(request.Role)
            && Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var parsedRole))
        {
            userRole = parsedRole;
        }

        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsVerified = false,  // Chưa xác thực
            OtpCode = otpCode,
            OtpExpiresAt = otpExpiry,
            Role = userRole,  // Tự động set theo logic: null/rỗng/invalid → Member
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // ===== LƯU USER =====
        await _userRepository.CreateAsync(user);

        // ===== GỬI EMAIL OTP =====
        bool emailSent = await _emailService.SendOtpEmailAsync(user.Email, otpCode, user.FullName);

        if (!emailSent)
        {
            // Email gửi thất bại - vẫn tạo user nhưng báo cho user biết
            return new AuthResponse
            {
                Success = false,
                Message = $"Đăng ký không thành công! Không thể gửi mã OTP đến email {user.Email}. Vui lòng kiểm tra lại email.",
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                RequiresVerification = true
            };
        }

        return new AuthResponse
        {
            Success = true,
            Message = $"Đăng ký thành công! Mã OTP đã được gửi đến email {user.Email}.",
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            RequiresVerification = true
        };
    }

    /// <summary>
    /// Kích hoạt tài khoản bằng mã OTP.
    /// </summary>
    public async Task<AuthResponse> VerifyOtpAsync(VerifyOtpRequest request)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.Email))
            return new AuthResponse { Success = false, Message = "Email không được để trống." };

        if (string.IsNullOrWhiteSpace(request.OtpCode))
            return new AuthResponse { Success = false, Message = "Mã OTP không được để trống." };

        // Tìm user
        var user = await _userRepository.GetByEmailAsync(request.Email.ToLower().Trim());
        if (user == null)
            return new AuthResponse { Success = false, Message = "Không tìm thấy tài khoản với email này." };

        // Kiểm tra đã verify chưa
        if (user.IsVerified)
            return new AuthResponse { Success = false, Message = "Tài khoản đã được xác thực trước đó." };

        // Kiểm tra OTP có không
        if (string.IsNullOrEmpty(user.OtpCode))
            return new AuthResponse { Success = false, Message = "Không có mã OTP. Vui lòng đăng ký lại." };

        // Kiểm tra OTP hết hạn
        if (user.OtpExpiresAt == null || user.OtpExpiresAt < DateTime.UtcNow)
            return new AuthResponse { Success = false, Message = "Mã OTP đã hết hạn. Vui lòng đăng ký lại để nhận mã mới." };

        // Kiểm tra OTP đúng không
        if (user.OtpCode != request.OtpCode.Trim())
            return new AuthResponse { Success = false, Message = "Mã OTP không chính xác." };

        // ===== XÁC THỰC THÀNH CÔNG =====
        user.IsVerified = true;
        user.OtpCode = null;  // Xóa OTP sau khi sử dụng
        user.OtpExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);

        // Tạo JWT Token
        string token = GenerateJwtToken(user);

        return new AuthResponse
        {
            Success = true,
            Message = "Xác thực thành công! Tài khoản của bạn đã được kích hoạt.",
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            RequiresVerification = false,
            Token = "Bearer " + token
        };
    }

    /// <summary>
    /// Đăng nhập.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.Email))
            return new AuthResponse { Success = false, Message = "Email không được để trống." };

        if (string.IsNullOrWhiteSpace(request.Password))
            return new AuthResponse { Success = false, Message = "Mật khẩu không được để trống." };

        // Tìm user
        var user = await _userRepository.GetByEmailAsync(request.Email.ToLower().Trim());
        if (user == null)
            return new AuthResponse { Success = false, Message = "Email hoặc mật khẩu không chính xác." };

        // Kiểm tra đã verify chưa
        if (!user.IsVerified)
            return new AuthResponse
            {
                Success = false,
                Message = "Tài khoản chưa được xác thực. Vui lòng kiểm tra email và nhập mã OTP để kích hoạt.",
                RequiresVerification = true
            };

        // Kiểm tra password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return new AuthResponse { Success = false, Message = "Email hoặc mật khẩu không chính xác." };

        // Tạo JWT Token
        string token = GenerateJwtToken(user);

        return new AuthResponse
        {
            Success = true,
            Message = "Đăng nhập thành công!",
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            Token = "Bearer " + token
        };
    }

    /// <summary>
    /// Gửi email đặt lại mật khẩu (gửi OTP).
    /// </summary>
    public async Task<AuthResponse> ForgotPasswordAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new AuthResponse { Success = false, Message = "Email không được để trống." };

        // Tìm user
        var user = await _userRepository.GetByEmailAsync(email.ToLower().Trim());
        if (user == null)
        {
            // Không tiết lộ email có tồn tại hay không
            return new AuthResponse
            {
                Success = true,
                Message = "Nếu email tồn tại trong hệ thống, mã OTP đã được gửi."
            };
        }

        // Tạo OTP mới
        string otpCode = GenerateOtpCode();
        user.OtpCode = otpCode;
        user.OtpExpiresAt = DateTime.UtcNow.AddMinutes(OTP_EXPIRY_MINUTES);
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        // Gửi email OTP
        bool emailSent = await _emailService.SendOtpEmailAsync(user.Email, otpCode, user.FullName);

        if (!emailSent)
        {
            return new AuthResponse
            {
                Success = false,
                Message = "Không thể gửi mã OTP. Vui lòng thử lại sau."
            };
        }

        return new AuthResponse
        {
            Success = true,
            Message = $"Mã OTP đã được gửi đến email {user.Email}."
        };
    }

    /// <summary>
    /// Đặt lại mật khẩu bằng OTP.
    /// </summary>
    public async Task<AuthResponse> ResetPasswordAsync(ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return new AuthResponse { Success = false, Message = "Email không được để trống." };

        if (string.IsNullOrWhiteSpace(request.OtpCode))
            return new AuthResponse { Success = false, Message = "Mã OTP không được để trống." };

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return new AuthResponse { Success = false, Message = "Mật khẩu mới không được để trống." };

        if (string.IsNullOrWhiteSpace(request.NewPasswordConfirm))
            return new AuthResponse { Success = false, Message = "Xác nhận mật khẩu không được để trống." };

        // Validate password strength
        if (request.NewPassword.Length < 6)
            return new AuthResponse { Success = false, Message = "Mật khẩu phải có ít nhất 6 ký tự." };

        // Kiểm tra password match
        if (request.NewPassword != request.NewPasswordConfirm)
            return new AuthResponse { Success = false, Message = "Mật khẩu và xác nhận mật khẩu không khớp." };

        // Tìm user
        var user = await _userRepository.GetByEmailAsync(request.Email.ToLower().Trim());
        if (user == null)
            return new AuthResponse { Success = false, Message = "Không tìm thấy tài khoản với email này." };

        // Kiểm tra OTP
        if (string.IsNullOrEmpty(user.OtpCode))
            return new AuthResponse { Success = false, Message = "Không có mã OTP. Vui lòng gửi yêu cầu đặt lại mật khẩu trước." };

        if (user.OtpCode != request.OtpCode.Trim())
            return new AuthResponse { Success = false, Message = "Mã OTP không chính xác." };

        // Kiểm tra OTP hết hạn
        if (user.OtpExpiresAt == null || user.OtpExpiresAt < DateTime.UtcNow)
        {
            user.OtpCode = null;
            user.OtpExpiresAt = null;
            await _userRepository.UpdateAsync(user);
            return new AuthResponse { Success = false, Message = "Mã OTP đã hết hạn. Vui lòng gửi yêu cầu đặt lại mật khẩu trước." };
        }

        // Đặt lại mật khẩu
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.OtpCode = null;
        user.OtpExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        return new AuthResponse
        {
            Success = true,
            Message = "Mật khẩu đã được đặt lại thành công. Vui lòng đăng nhập với mật khẩu mới."
        };
    }

    /// <summary>
    /// Tạo mã OTP 6 số ngẫu nhiên.
    /// </summary>
    private static string GenerateOtpCode()
    {
        Random random = new Random();
        return random.Next(100000, 999999).ToString();
    }

    /// <summary>
    /// Tạo JWT Token cho user.
    /// </summary>
    public string GenerateJwtToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JWT_SECRET));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id ?? ""),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new Claim(JwtRegisteredClaimNames.Name, user.FullName ?? ""),
            new Claim(ClaimTypes.Role, user.Role.ToString()),  // Thêm Role vào JWT
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: JWT_ISSUER,
            audience: JWT_AUDIENCE,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(JWT_EXPIRY_HOURS),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
