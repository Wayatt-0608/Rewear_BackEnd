using REWARE.Application.DTOs;
using REWARE.Application.Interfaces;
using REWEAR.Domain.Entities;
using System.Text.RegularExpressions;

namespace REWARE.Application.Services;

/// <summary>
/// Service xử lý đăng nhập, đăng ký, xác thực OTP.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;

    // Thời gian hết hạn OTP: 5 phút
    private const int OTP_EXPIRY_MINUTES = 5;

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

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return new AuthResponse { Success = false, Message = "Số điện thoại không được để trống." };

        if (string.IsNullOrWhiteSpace(request.Email))
            return new AuthResponse { Success = false, Message = "Email không được để trống." };

        if (string.IsNullOrWhiteSpace(request.Password))
            return new AuthResponse { Success = false, Message = "Mật khẩu không được để trống." };

        if (string.IsNullOrWhiteSpace(request.PasswordConfirm))
            return new AuthResponse { Success = false, Message = "Xác nhận mật khẩu không được để trống." };

        // 2. Validate email format
        if (!Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return new AuthResponse { Success = false, Message = "Email không hợp lệ." };

        // 3. Validate phone format (10-11 số, bắt đầu bằng 0)
        if (!Regex.IsMatch(request.PhoneNumber, @"^0[0-9]{9,10}$"))
            return new AuthResponse { Success = false, Message = "Số điện thoại không hợp lệ (phải 10-11 số, bắt đầu bằng 0)." };

        // 4. Validate birthdate format (YYYY-MM-DD)
        if (!Regex.IsMatch(request.BirthDate, @"^\d{4}-\d{2}-\d{2}$"))
            return new AuthResponse { Success = false, Message = "Ngày sinh phải theo định dạng YYYY-MM-DD." };

        // Parse và validate ngày sinh
        if (!DateTime.TryParse(request.BirthDate, out DateTime birthDate))
            return new AuthResponse { Success = false, Message = "Ngày sinh không hợp lệ." };

        // Kiểm tra tuổi >= 13
        var age = DateTime.Today.Year - birthDate.Year;
        if (DateTime.Today < birthDate.AddYears(age)) age--;
        if (age < 13)
            return new AuthResponse { Success = false, Message = "Bạn phải từ 13 tuổi trở lên để đăng ký." };

        // 5. Validate password strength
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
        var user = new User
        {
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            BirthDate = request.BirthDate,
            Email = request.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsVerified = false,  // Chưa xác thực
            OtpCode = otpCode,
            OtpExpiresAt = otpExpiry,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // ===== LƯU USER =====
        await _userRepository.CreateAsync(user);

        // ===== GỬI EMAIL OTP =====
        bool emailSent = await _emailService.SendOtpEmailAsync(user.Email, otpCode, user.FullName);

        if (!emailSent)
        {
            // Vẫn tạo user nhưng cảnh báo
            return new AuthResponse
            {
                Success = true,
                Message = $"Đăng ký thành công! Mã OTP đã được gửi đến email {user.Email}. (Lưu ý: Email có thể nằm trong thư rác)",
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

        return new AuthResponse
        {
            Success = true,
            Message = "Xác thực thành công! Tài khoản của bạn đã được kích hoạt.",
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            RequiresVerification = false
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

        return new AuthResponse
        {
            Success = true,
            Message = "Đăng nhập thành công!",
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName
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
}
