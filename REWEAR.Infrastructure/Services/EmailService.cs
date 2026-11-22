using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using REWEAR.Application.Interfaces;

namespace REWEAR.Infrastructure.Services;

/// <summary>Sends OTP using SMTP locally or the Brevo HTTPS API on Render Free.</summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly HttpClient _httpClient;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger, HttpClient httpClient)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string fullName)
    {
        var settings = _configuration.GetSection("EmailSettings");
        var configuredProvider = (settings["Provider"] ?? "Auto").Trim();
        var apiKey = settings["BrevoApiKey"]?.Trim();
        var provider = configuredProvider;
        if (provider.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            // Render Free blocks SMTP. Never silently fall back to it after an API failure.
            bool render = string.Equals(_configuration["RENDER"], "true", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(_configuration["RENDER_SERVICE_ID"]);
            provider = !string.IsNullOrWhiteSpace(apiKey) || render ? "Brevo" : "Smtp";
        }
        if (!provider.Equals("Brevo", StringComparison.OrdinalIgnoreCase)
            && !provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            return Failed("Unknown", "Configuration", "INVALID_PROVIDER");

        provider = provider.Equals("Brevo", StringComparison.OrdinalIgnoreCase) ? "Brevo" : "Smtp";
        if (provider == "Brevo" && string.IsNullOrWhiteSpace(apiKey))
            return Failed(provider, "Configuration", "MISSING_API_KEY");
        if (string.IsNullOrWhiteSpace(settings["FromEmail"]) || string.IsNullOrWhiteSpace(toEmail)
            || !MailboxAddress.TryParse(settings["FromEmail"], out var sender)
            || !MailboxAddress.TryParse(toEmail, out var recipient)
            || !sender.Address.Contains('@') || !recipient.Address.Contains('@'))
            return Failed(provider, "Configuration", "INVALID_EMAIL_ADDRESS");

        var fromName = settings["FromName"]?.Trim();
        if (string.IsNullOrWhiteSpace(fromName)) fromName = "REWEAR";
        const string subject = "Mã xác thực REWEAR - OTP Code";
        var htmlBody = BuildOtpEmailTemplate(fullName, otpCode);
        var textBody = $"Mã OTP của bạn là: {otpCode} (có hiệu lực trong 5 phút)";
        var stage = "Connect";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            if (provider == "Brevo")
            {
                stage = "Send";
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                request.Headers.Add("api-key", apiKey!);
                request.Content = JsonContent.Create(new
                {
                    sender = new { email = sender.Address, name = fromName },
                    to = new[] { new { email = recipient.Address } },
                    subject,
                    htmlContent = htmlBody,
                    textContent = textBody
                });
                using var response = await _httpClient.SendAsync(request, deadline.Token);
                // Do not log provider response bodies: they can contain addresses or message content.
                if (response.StatusCode != HttpStatusCode.Created)
                    return Failed(provider, stage, $"HTTP_{(int)response.StatusCode}");
                using var json = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(deadline.Token), cancellationToken: deadline.Token);
                if (json.RootElement.ValueKind != JsonValueKind.Object
                    || !json.RootElement.TryGetProperty("messageId", out var messageId)
                    || messageId.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(messageId.GetString()))
                    return Failed(provider, stage, "INVALID_RESPONSE");
            }
            else
            {
                var host = settings["SmtpHost"]?.Trim();
                if (string.IsNullOrWhiteSpace(host)
                    || !int.TryParse(settings["SmtpPort"] ?? "587", out var port)
                    || port is < 1 or > 65535
                    || string.IsNullOrWhiteSpace(settings["FromPassword"]))
                    return Failed(provider, "Configuration", "MISSING_SMTP_CONFIGURATION");

                var password = settings["FromPassword"]!;
                var security = settings["SmtpSecurity"]?.Trim() ?? "Auto";
                var tls = security.ToLowerInvariant() switch
                {
                    "auto" => port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                    "starttls" => SecureSocketOptions.StartTls,
                    "sslonconnect" => SecureSocketOptions.SslOnConnect,
                    _ => SecureSocketOptions.None
                };
                if (tls == SecureSocketOptions.None)
                    return Failed(provider, "Configuration", "INVALID_SMTP_SECURITY");

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(fromName, sender.Address));
                message.To.Add(new MailboxAddress(fullName, recipient.Address));
                message.Subject = subject;
                message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

                using var client = new SmtpClient { Timeout = 15000 };
                await client.ConnectAsync(host, port, tls, deadline.Token);
                stage = "Authenticate";
                await client.AuthenticateAsync(settings["SmtpUsername"] ?? sender.Address,
                    password, deadline.Token);
                stage = "Send";
                await client.SendAsync(message, deadline.Token);
                // Once SMTP accepts the message, a failed QUIT must not report delivery failure.
                try { await client.DisconnectAsync(true, deadline.Token); }
                catch (Exception) { _logger.LogWarning("Email SMTP disconnect failed after message acceptance."); }
            }

            _logger.LogInformation("Email accepted by provider {Provider}.", provider);
            return true;
        }
        catch (OperationCanceledException) { return Failed(provider, stage, "TIMEOUT"); }
        catch (SmtpCommandException ex) { return Failed(provider, stage, $"SMTP_{(int)ex.StatusCode}"); }
        catch (Exception ex) { return Failed(provider, stage, ex.GetType().Name); }
    }

    private bool Failed(string provider, string stage, string code)
    {
        // Only bounded diagnostic metadata. Never log OTP, recipients, credentials or exception messages.
        _logger.LogWarning("Email delivery failed. Provider={Provider}, Stage={Stage}, Code={Code}.", provider, stage, code);
        return false;
    }

    private string BuildOtpEmailTemplate(string fullName, string otpCode)
    {
        fullName = WebUtility.HtmlEncode(fullName);
        // Encode user-controlled content before inserting it into HTML.
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
