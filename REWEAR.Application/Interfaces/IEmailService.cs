namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Email Service.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Gửi email chứa mã OTP.
    /// </summary>
    Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string fullName);
}
