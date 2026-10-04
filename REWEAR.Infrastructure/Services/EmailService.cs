using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MailKit.Net.Smtp;
using REWEAR.Application.Interfaces;

namespace REWEAR.Infrastructure.Services;

/// <summary>
/// Service gui email OTP voi HTML template dep.
/// Dung MailKit de gui email that qua SMTP.
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
            var emailSettings = _configuration.GetSection("EmailSettings");
            
            string subject = "Mã xác thực REWEAR - OTP Code";
            string htmlBody = BuildOtpEmailTemplate(fullName, otpCode);

            // ===== GUI EMAIL THAT BANG MAILKIT =====
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                "REWEAR",
                emailSettings["FromEmail"] ?? ""
            ));
            message.To.Add(new MailboxAddress(fullName, toEmail));
            message.Subject = subject;
            
            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = $"Mã OTP của bạn là: {otpCode} (có hiệu lực trong 5 phút)"
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            
            await client.ConnectAsync(
                emailSettings["SmtpHost"] ?? "",
                int.Parse(emailSettings["SmtpPort"] ?? "587"),
                MailKit.Security.SecureSocketOptions.StartTls
            );

            await client.AuthenticateAsync(
                emailSettings["FromEmail"] ?? "",
                emailSettings["FromPassword"] ?? ""
            );

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("OTP email sent to {Email} with code {Code}", toEmail, otpCode);
            Console.WriteLine($"[EmailService] Email sent successfully to {toEmail}");

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {Email}", toEmail);
            Console.WriteLine($"[EmailService] Failed to send email: {ex.Message}");
            return await Task.FromResult(true);
        }
    }

    private string BuildOtpEmailTemplate(string fullName, string otpCode)
    {
        // HTML Template voi Unicode tieng Viet - can giua, chu to, dep mat
        return $@"
<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Mã xác thực REWEAR</title>
</head>
<body style=""margin: 0; padding: 0; font-family: Arial, Helvetica, sans-serif; background-color: #f4f4f4;"">
    <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color: #f4f4f4; padding: 40px 20px;"">
        <tr>
            <td align=""center"">
                <table role=""presentation"" width=""600"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 12px rgba(0,0,0,0.1); overflow: hidden;"">
                    
                    <!-- Header voi gradient -->
                    <tr>
                        <td style=""background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 40px 30px; text-align: center;"">
                            <h1 style=""margin: 0; color: #ffffff; font-size: 36px; font-weight: bold; letter-spacing: 2px;"">
                                REWEAR
                            </h1>
                            <p style=""margin: 10px 0 0 0; color: #ffffff; font-size: 16px; opacity: 0.9;"">
                                Thời trang bền vững - Tái sử dụng phong cách
                            </p>
                        </td>
                    </tr>
                    
                    <!-- Body content -->
                    <tr>
                        <td style=""padding: 50px 40px;"">
                            <h2 style=""margin: 0 0 20px 0; color: #333333; font-size: 24px; text-align: center;"">
                                Xin chào <span style=""color: #667eea;"">{fullName}</span>!
                            </h2>
                            
                            <p style=""margin: 0 0 30px 0; color: #555555; font-size: 16px; line-height: 1.6; text-align: center;"">
                                Cảm ơn bạn đã đăng ký tài khoản <strong>REWEAR</strong>.<br>
                                Vui lòng sử dụng mã OTP bên dưới để xác thực tài khoản của bạn:
                            </p>
                            
                            <!-- OTP Box -->
                            <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""margin: 30px 0;"">
                                <tr>
                                    <td align=""center"">
                                        <div style=""display: inline-block; background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%); padding: 30px 50px; border-radius: 12px; border: 3px dashed #667eea;"">
                                            <p style=""margin: 0 0 10px 0; color: #666666; font-size: 14px; text-transform: uppercase; letter-spacing: 2px; text-align: center;"">
                                                MÃ XÁC THỰC CỦA BẠN
                                            </p>
                                            <h1 style=""margin: 0; color: #764ba2; font-size: 48px; font-weight: bold; letter-spacing: 12px; text-align: center; font-family: 'Courier New', monospace;"">
                                                {otpCode}
                                            </h1>
                                        </div>
                                    </td>
                                </tr>
                            </table>
                            
                            <!-- Warning -->
                            <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""margin: 30px 0; background-color: #fff3cd; border-radius: 8px;"">
                                <tr>
                                    <td style=""padding: 20px;"">
                                        <p style=""margin: 0; color: #856404; font-size: 14px; line-height: 1.6; text-align: center;"">
                                            <strong>Mã có hiệu lực trong 5 phút</strong><br>
                                            <strong>Không chia sẻ mã này với bất kỳ ai</strong>
                                        </p>
                                    </td>
                                </tr>
                            </table>
                            
                            <p style=""margin: 30px 0 0 0; color: #777777; font-size: 14px; line-height: 1.6; text-align: center;"">
                                Nếu bạn không thực hiện đăng ký, vui lòng bỏ qua email này.
                            </p>
                        </td>
                    </tr>
                    
                    <!-- Footer -->
                    <tr>
                        <td style=""background-color: #f8f9fa; padding: 30px 40px; text-align: center; border-top: 1px solid #e9ecef;"">
                            <p style=""margin: 0 0 10px 0; color: #667eea; font-size: 16px; font-weight: bold;"">
                                Trân trọng, Đội ngũ REWEAR
                            </p>
                            <p style=""margin: 10px 0 0 0; color: #999999; font-size: 12px;"">
                                Email được gửi tự động, vui lòng không reply.
                            </p>
                        </td>
                    </tr>
                    
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }
}
