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
    private readonly ICommerceUnitOfWork _transactions;
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly JwtSettings _jwtSettings;

    // Thời gian hết hạn OTP: 5 phút
    private const int OTP_EXPIRY_MINUTES = 5;

    public AuthService(IUserRepository userRepository, IEmailService emailService, JwtSettings jwtSettings, ICommerceUnitOfWork transactions)
    {
        _transactions = transactions;
        _userRepository = userRepository;
        _emailService = emailService;
        _jwtSettings = jwtSettings;

        if (string.IsNullOrWhiteSpace(_jwtSettings?.SecretKey))
            throw new InvalidOperationException("JwtSettings.SecretKey chưa được cấu hình");
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
        if (!Regex.IsMatch(request.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return new AuthResponse { Success = false, Message = "Email không hợp lệ." };

        // 3. Validate password strength
        if (request.Password.Length < 8)
            return new AuthResponse { Success = false, Message = "Mật khẩu phải có ít nhất 8 ký tự." };

        // 6. Kiểm tra password match
        if (request.Password != request.PasswordConfirm)
            return new AuthResponse { Success = false, Message = "Mật khẩu và xác nhận mật khẩu không khớp." };

        // 7. Kiểm tra email đã tồn tại
        var existingUser = await _userRepository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (existingUser != null)
        {
            if (existingUser.IsVerified)
                return new AuthResponse { Success = false, Message = "Email đã được sử dụng." };
            // Retrying a failed signup must not overwrite the existing password or privileged role.
            if (!BCrypt.Net.BCrypt.Verify(request.Password, existingUser.PasswordHash))
                return new AuthResponse
                {
                    Success = false, RequiresVerification = true,
                    Message = "Tài khoản đang chờ xác thực. Hãy dùng mật khẩu đã đăng ký hoặc chức năng gửi lại OTP."
                };
            return await IssueOtpAsync(existingUser, "Registration");
        }

        // ===== TẠO OTP =====
        string otpCode = GenerateOtpCode();
        DateTime otpExpiry = DateTime.UtcNow.AddMinutes(OTP_EXPIRY_MINUTES);

        // Public registration always creates a Member; privileged roles require Admin.
        const UserRole userRole = UserRole.Member;

        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsVerified = false,  // Chưa xác thực
            OtpCode = otpCode,
            OtpExpiresAt = otpExpiry,
            OtpIssuedAt = DateTime.UtcNow,
            OtpPurpose = "Registration",
            Role = userRole,  // Tự động set theo logic: null/rỗng/invalid → Member
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // ===== LƯU USER =====
        await _userRepository.CreateAsync(user);

        return await SendRegistrationOtpAsync(user);
    }

    /// <summary>Resend registration OTP without modifying the pending account's password or role.</summary>
    public async Task<AuthResponse> ResendOtpAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)
            || !Regex.IsMatch(email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return new AuthResponse { Success = false, Message = "Email không hợp lệ." };

        var user = await _userRepository.GetByEmailAsync(email.Trim().ToLowerInvariant());
        if (user == null || user.IsVerified)
            return new AuthResponse { Success = true, Message = "Nếu tài khoản đang chờ xác thực, mã OTP sẽ được gửi." };

        return await IssueOtpAsync(user, "Registration");
    }

    private async Task<AuthResponse> IssueOtpAsync(User user, string purpose)
    {
        var now = DateTime.UtcNow;
        if (user.OtpIssuedAt is DateTime issuedAt && issuedAt.AddSeconds(60) > now)
            return new AuthResponse
            {
                Success = false, ErrorCode = "OTP_RESEND_COOLDOWN",
                Message = "Vui lòng chờ trước khi yêu cầu mã OTP mới.",
                RequiresVerification = purpose == "Registration" ? true : null,
                RetryAfterSeconds = Math.Max(1, (int)Math.Ceiling((issuedAt.AddSeconds(60) - now).TotalSeconds))
            };

        var otp = GenerateOtpCode();
        var expiresAt = now.AddMinutes(OTP_EXPIRY_MINUTES);
        // One atomic, field-scoped write prevents concurrent resends and never overwrites role/password/profile.
        if (!await _userRepository.TryIssueOtpAsync(user, otp, expiresAt, purpose, now))
            return new AuthResponse
            {
                Success = false, ErrorCode = "OTP_REQUEST_CHANGED",
                Message = "Yêu cầu xác thực vừa thay đổi. Vui lòng thử lại sau.",
                RequiresVerification = purpose == "Registration" ? true : null
            };
        user.OtpCode = otp;
        user.OtpExpiresAt = expiresAt;
        user.OtpIssuedAt = now;
        user.OtpPurpose = purpose;
        user.OtpAttempts = 0;

        if (purpose == "Registration") return await SendRegistrationOtpAsync(user);
        bool sent = await _emailService.SendOtpEmailAsync(user.Email, otp, user.FullName);
        return new AuthResponse
        {
            Success = sent,
            ErrorCode = sent ? null : "EMAIL_DELIVERY_FAILED",
            Message = sent ? "Mã OTP đã được gửi." : "Chưa gửi được mã OTP. Vui lòng thử lại sau một phút."
        };
    }

    private async Task<AuthResponse> SendRegistrationOtpAsync(User user)
    {
        bool sent = await _emailService.SendOtpEmailAsync(user.Email, user.OtpCode!, user.FullName);
        return new AuthResponse
        {
            Success = sent,
            Message = sent
                ? "Mã OTP đã được gửi. Vui lòng xác thực để hoàn tất đăng ký."
                : "Tài khoản đang chờ xác thực nhưng chưa gửi được OTP. Vui lòng thử đăng ký lại hoặc gửi lại OTP sau một phút.",
            ErrorCode = sent ? null : "EMAIL_DELIVERY_FAILED",
            RequiresVerification = true,
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString()
        };
    }

    /// <summary>
    /// Kích hoạt tài khoản bằng mã OTP.
    /// </summary>
    public Task<AuthResponse> VerifyOtpAsync(VerifyOtpRequest request) => _transactions.ExecuteAsync(() => VerifyOtpCoreAsync(request));

    private async Task<AuthResponse> VerifyOtpCoreAsync(VerifyOtpRequest request)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.Email))
            return new AuthResponse { Success = false, Message = "Email không được để trống." };

        if (string.IsNullOrWhiteSpace(request.OtpCode))
            return new AuthResponse { Success = false, Message = "Mã OTP không được để trống." };

        // Tìm user
        var user = await _userRepository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user == null)
            return new AuthResponse { Success = false, Message = "Không tìm thấy tài khoản với email này." };

        // Kiểm tra đã verify chưa
        if (user.IsVerified)
            return new AuthResponse { Success = false, Message = "Tài khoản đã được xác thực trước đó." };

        if (user.OtpPurpose != null && user.OtpPurpose != "Registration")
            return new AuthResponse { Success = false, Message = "Mã OTP này không dùng để kích hoạt tài khoản." };

        // Kiểm tra OTP có không
        if (string.IsNullOrEmpty(user.OtpCode))
            return new AuthResponse { Success = false, Message = "Không có mã OTP. Vui lòng đăng ký lại." };

        // Kiểm tra OTP hết hạn
        if (user.OtpExpiresAt == null || user.OtpExpiresAt < DateTime.UtcNow)
            return new AuthResponse { Success = false, Message = "Mã OTP đã hết hạn. Vui lòng đăng ký lại để nhận mã mới." };

        // Kiểm tra OTP đúng không
        if (user.OtpAttempts >= 5) return new AuthResponse { Success = false, Message = "Đã vượt số lần thử OTP. Vui lòng yêu cầu mã mới." };
        if (user.OtpCode != request.OtpCode.Trim())
        { user.OtpAttempts++; await _userRepository.UpdateAsync(user); return new AuthResponse { Success = false, Message = "Mã OTP không chính xác." }; }

        // ===== XÁC THỰC THÀNH CÔNG =====
        user.IsVerified = true;
        user.OtpPurpose = null;
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
        var user = await _userRepository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
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
        var user = await _userRepository.GetByEmailAsync(email.Trim().ToLowerInvariant());
        if (user == null)
        {
            // Không tiết lộ email có tồn tại hay không
            return new AuthResponse
            {
                Success = true,
                Message = "Nếu email tồn tại trong hệ thống, mã OTP đã được gửi."
            };
        }

        return await IssueOtpAsync(user, "PasswordReset");
    }

    /// <summary>
    /// Đặt lại mật khẩu bằng OTP.
    /// </summary>
    public Task<AuthResponse> ResetPasswordAsync(ResetPasswordRequest request) => _transactions.ExecuteAsync(() => ResetPasswordCoreAsync(request));

    private async Task<AuthResponse> ResetPasswordCoreAsync(ResetPasswordRequest request)
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
        if (request.NewPassword.Length < 8)
            return new AuthResponse { Success = false, Message = "Mật khẩu phải có ít nhất 8 ký tự." };

        // Kiểm tra password match
        if (request.NewPassword != request.NewPasswordConfirm)
            return new AuthResponse { Success = false, Message = "Mật khẩu và xác nhận mật khẩu không khớp." };

        // Tìm user
        var user = await _userRepository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user == null)
            return new AuthResponse { Success = false, Message = "Không tìm thấy tài khoản với email này." };

        if (user.OtpPurpose != null && user.OtpPurpose != "PasswordReset")
            return new AuthResponse { Success = false, Message = "Vui lòng yêu cầu mã OTP đặt lại mật khẩu." };

        // Kiểm tra OTP
        if (string.IsNullOrEmpty(user.OtpCode))
            return new AuthResponse { Success = false, Message = "Không có mã OTP. Vui lòng gửi yêu cầu đặt lại mật khẩu trước." };

        if (user.OtpAttempts >= 5) return new AuthResponse { Success = false, Message = "Đã vượt số lần thử OTP. Vui lòng yêu cầu mã mới." };
        if (user.OtpCode != request.OtpCode.Trim())
        { user.OtpAttempts++; await _userRepository.UpdateAsync(user); return new AuthResponse { Success = false, Message = "Mã OTP không chính xác." }; }

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
        user.OtpPurpose = null;
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
        return System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    }

    /// <summary>
    /// Tạo JWT Token cho user.
    /// </summary>
    public string GenerateJwtToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
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
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_jwtSettings.ExpiryHours),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
