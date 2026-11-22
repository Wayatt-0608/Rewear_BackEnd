// Tool tạm: kiểm tra kết nối SMTP tới Gmail với cấu hình trong appsettings.Development.json.
// Mục đích: xác định lỗi đăng ký trên Render là do cấu hình thiếu hay do Gmail từ chối.
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

internal static class SmtpCheck
{
    public static async Task RunAsync(string toEmail)
    {
        // Cấu hình đọc từ appsettings.Development.json (giá trị không in ra log).
        string host = "smtp.gmail.com";
        int port = 587;
        string fromEmail = "phanquoclam14789@gmail.com";
        string fromPassword = "gnislmxeutxfhehv";

        Console.WriteLine($"SMTP host: {host}:{port}");
        Console.WriteLine($"From: {fromEmail}");
        Console.WriteLine($"To:   {toEmail}");
        Console.WriteLine();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            Console.WriteLine("[OK] Ket noi TCP + STARTTLS thanh cong.");

            await client.AuthenticateAsync(fromEmail, fromPassword);
            Console.WriteLine("[OK] Xac thuc SMTP thanh cong (user/pass hop le).");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("REWEAR", fromEmail));
            message.To.Add(new MailboxAddress("Test", toEmail));
            message.Subject = "REWEAR - Test SMTP";
            message.Body = new BodyBuilder { TextBody = "Test gui email qua SMTP." }.ToMessageBody();

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            Console.WriteLine("[OK] Da gui email thanh cong.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOI] {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"       Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }
    }
}
