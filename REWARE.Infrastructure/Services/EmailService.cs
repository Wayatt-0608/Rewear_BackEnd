using REWARE.Application.Interfaces;

namespace REWARE.Infrastructure.Services;

/// <summary>
/// Service gửi email OTP.
/// Trong demo, email sẽ được log ra console.
/// Để production, thay thế bằng SMTP thật (MailKit, SendGrid, v.v.)
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
            // Trong demo, log email ra console
            // Production: Dùng SMTP thật (MailKit, SendGrid, AWS SES, v.v.)

            string subject = "Mã xác thực REWEAR - OTP Code";
            string body = $@"
Xin chào {fullName},

Cảm ơn bạn đã đăng ký tài khoản REWEAR!

Mã xác thực (OTP) của bạn là:
━━━━━━━━━━━━━━━━━━━━━━━━━━━
       🔑  {otpCode}  🔑
━━━━━━━━━━━━━━━━━━━━━━━━━━━

⚠️ Mã này có hiệu lực trong 5 phút.
⚠️ Không chia sẻ mã này với bất kỳ ai.

Nếu bạn không thực hiện đăng ký, vui lòng bỏ qua email này.

Trân trọng,
Đội ngũ REWEAR
━━━━━━━━━━━━━━━━━━━━━━━━━━━
📧 Email được gửi tự động - REWEAR System
";

            // ===== GỬI EMAIL THẬT (UNCOMMENT KHI CÓ SMTP) =====
            /*
            using var client = new SmtpClient(_configuration["Email:SmtpHost"], 
                                              int.Parse(_configuration["Email:SmtpPort"]))
            {
                Credentials = new NetworkCredential(_configuration["Email:Username"], 
                                                    _configuration["Email:Password"]),
                EnableSsl = true
            };

            var message = new MailMessage(
                from: _configuration["Email:FromAddress"],
                to: toEmail,
                subject: subject,
                body: body
            );

            await client.SendMailAsync(message);
            */

            // ===== DEMO: LOG RA CONSOLE =====
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@$"
╔══════════════════════════════════════════════════════════════╗
║                    📧 EMAIL ĐƯỢC GỬI                        ║
╠══════════════════════════════════════════════════════════════╣
║  To:      {toEmail,-50} ║
║  Subject: {subject,-50} ║
╠══════════════════════════════════════════════════════════════╣
║                      🔑 OTP CODE 🔑                          ║
║                    {otpCode,-25}                          ║
╠══════════════════════════════════════════════════════════════╣
║  ⚠️ Lưu ý: Email có thể nằm trong thư rác (SPAM)           ║
╚══════════════════════════════════════════════════════════════╝
");
            Console.ResetColor();

            _logger.LogInformation("OTP email sent to {Email} with code {Code}", toEmail, otpCode);

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {Email}", toEmail);
            
            // Trong demo, vẫn trả về true để không block user
            // Production: trả về false và xử lý retry
            return await Task.FromResult(true);
        }
    }
}
