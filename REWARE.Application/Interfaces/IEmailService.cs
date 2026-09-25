namespace REWARE.Application.Interfaces;

/// <summary>
/// Interface cho Email Service.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Gửi email chứa mã OTP.
    /// </summary>
    /// <param name="toEmail">Email người nhận.</param>
    /// <param name="otpCode">Mã OTP 6 số.</param>
    /// <param name="fullName">Tên người nhận.</param>
    /// <returns>true nếu gửi thành công.</returns>
    Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string fullName);
}
