using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using REWARE.Application.Interfaces;

namespace REWEAR.Infrastructure.Services;

/// <summary>
/// Service gui email OTP.
/// Trong demo, email se duoc log ra console.
/// De production, thay the bang SMTP that (MailKit, SendGrid, v.v.)
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string fullName)
    {
        try
        {
            string subject = "Ma xac thuc REWEAR - OTP Code";
            string body = $"Xin chao {fullName},\n\nMa OTP cua ban la: {otpCode}\n\nCo hieu luc trong 5 phut.";

            // DEMO: LOG RA CONSOLE
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("========================================");
            Console.WriteLine("  EMAIL DUOC GUI (DEMO - Khong that)");
            Console.WriteLine("========================================");
            Console.WriteLine($"  To:      {toEmail}");
            Console.WriteLine($"  Subject: {subject}");
            Console.WriteLine($"  OTP:     {otpCode}");
            Console.WriteLine("========================================");
            Console.ResetColor();

            _logger.LogInformation("OTP email sent to {Email} with code {Code}", toEmail, otpCode);

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {Email}", toEmail);
            return await Task.FromResult(true);
        }
    }
}
