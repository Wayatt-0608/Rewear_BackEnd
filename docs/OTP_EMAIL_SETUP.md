# OTP email: local và Render Free

## Transport

- Local: giữ Gmail SMTP qua TLS (smtp.gmail.com, port 587 + STARTTLS; port 465 + TLS-on-connect cũng được hỗ trợ).
- Render Free: gửi bằng Brevo HTTP API, POST https://api.brevo.com/v3/smtp/email, qua HTTPS.
- Không đổi SMTP sang port 465 để khắc phục Render Free: Render chặn cả 25/465/587.
- Không cần migration. Không xóa tài khoản đang chờ xác thực.

Nguồn: [Render Free limitations](https://render.com/docs/free),
[Brevo transactional email API](https://developers.brevo.com/reference/send-transac-email),
[verified senders](https://developers.brevo.com/docs/getting-started-with-senders-and-domains).

## Thiết lập Brevo cho Render

1. Tạo tài khoản Brevo và hoàn tất yêu cầu kích hoạt transactional email của tài khoản.
2. Tạo sender trong **Settings → Senders, Domains & IPs** và xác minh email sender.
   Nếu có domain riêng, xác thực domain theo hướng dẫn Brevo.
   Nếu dùng địa chỉ Gmail làm sender, kiểm tra chính sách sender hiện hành và sender hiển thị trong mail thực tế:
   [Brevo có thể thay địa chỉ gửi bằng địa chỉ compliant](https://help.brevo.com/hc/en-us/articles/14925263522578-Comply-with-Gmail-Yahoo-and-Microsoft-s-requirements-for-email-senders).
3. Trong **SMTP & API → API Keys**, tạo API key. Cần **API key**, không phải SMTP key hoặc mật khẩu đăng nhập.
4. Trên Render, chọn backend service → **Environment**, đặt:

| Biến | Giá trị cần nhập |
| --- | --- |
| EmailSettings__Provider | Brevo |
| EmailSettings__BrevoApiKey | API key của chính tài khoản Brevo; không ghi vào repository/chat |
| EmailSettings__FromEmail | Địa chỉ sender đã xác minh trong Brevo |
| EmailSettings__FromName | REWEAR |

Không cần SMTP password cho transport Brevo. Các cấu hình MongoDB/JWT/Cloudinary/PayOS hiện có vẫn cần giữ.
Không đổi ASPNETCORE_ENVIRONMENT sang Development để lấy cấu hình SMTP.

5. Lưu các biến môi trường và deploy revision chứa thay đổi OTP.
6. Kiểm tra log có “Email accepted by provider Brevo.” và recipient thực sự nhận được thư.
   Provider chấp nhận thư chưa chứng minh thư đã vào Inbox; kiểm tra cả Spam và Transactional logs trên Brevo.
   Nếu tài khoản chưa được bật transactional email, sender chưa verified, key sai hoặc hết quota, kiểm tra trên dashboard Brevo.
   Không bật log request/response body hay SMTP protocol logging.

## Local

Trong Development, cấu hình EmailSettings theo precedence ASP.NET Core:
appsettings → appsettings.Development → User Secrets → process environment → command-line arguments.
Không suy ra cấu hình hosting từ file Development.

- Provider=Smtp để chọn SMTP tường minh.
- Đặt FromEmail và FromPassword bằng User Secrets/environment; với Gmail, dùng App Password còn hiệu lực.
- SmtpHost=smtp.gmail.com, SmtpPort=587; SmtpSecurity=Auto hoặc StartTls.
- Provider=Auto là mặc định: có BrevoApiKey hoặc Render marker thì chọn Brevo; ngoài Render và không có key thì chọn SMTP.
- Có thể dùng Provider=Brevo cùng API key/sender trên local để kiểm tra đúng transport sẽ deploy.
- Mỗi lần gửi giới hạn trong 15 giây; không tắt TLS certificate validation.
- Nếu đã đặt Brevo API key rồi muốn thử Gmail SMTP, đặt Provider=Smtp để tránh Auto chọn Brevo.

Chạy backend:

~~~powershell
dotnet run --project REWEAR.API --launch-profile http
~~~

Backend dùng http://localhost:5000 theo launch profile. Frontend Vite thử local phải đặt /api proxy target
thành http://localhost:5000 và restart Vite. Nếu target vẫn là Render thì đang kiểm tra backend Render.

## Đăng ký lại và gửi lại OTP

- Đăng ký mới vẫn tạo Member, không lấy role từ body làm quyền.
- Khi provider lỗi, tài khoản giữ IsVerified=false; response có RequiresVerification=true,
  ErrorCode=EMAIL_DELIVERY_FAILED, HTTP 503.
- Chờ ít nhất 60 giây, đăng ký lại bằng **cùng email và mật khẩu** để tiếp tục.
  Không tạo document User thứ hai, không ghi đè fullName/password/role.
- Nếu cần gửi lại mà không nhập lại toàn bộ form, gọi:

~~~http
POST /api/Auth/resend-otp
Content-Type: application/json

{"email":"recipient@example.test"}
~~~

Địa chỉ .test trên chỉ là ví dụ, không phải mailbox để gửi thật. Frontend nên thêm nút “Gửi lại OTP” gọi endpoint này;
nếu chưa thêm nút, đường đăng ký lại vẫn sử dụng được.

| HTTP | Ý nghĩa |
| --- | --- |
| 200 | Yêu cầu được xử lý; với tài khoản pending hợp lệ, provider đã nhận yêu cầu gửi |
| 400 | Dữ liệu không hợp lệ hoặc đăng ký trùng tài khoản đã xác thực |
| 409 | OTP/account vừa thay đổi bởi request khác; đọc lại trạng thái |
| 429 | Cooldown theo tài khoản hoặc auth rate limiter; cooldown có Retry-After |
| 503 | Dịch vụ email chưa nhận được yêu cầu gửi thành công |

Cooldown dùng field persisted OtpIssuedAt, không chỉ memory hoặc IP, áp dụng chung register retry/resend/forgot-password.
Atomic issue chỉ thay các field OTP; không thay password, profile hoặc role.
Nếu gửi thất bại, cooldown vẫn giữ để tránh spam provider; thử lại sau một phút.
Mã mới có thời hạn 5 phút, dùng thay mã cũ và reset bộ đếm thử sai.
Đăng ký và password reset được phân biệt bằng OtpPurpose.
OTP legacy chưa có purpose vẫn tương thích trong thời gian còn hiệu lực; không rewrite dữ liệu cũ.

## Log an toàn và chẩn đoán

Chỉ log Provider, Stage, Code; không log OTP, API key, password, recipient hay raw provider response/exception message.

| Code | Kiểm tra |
| --- | --- |
| MISSING_API_KEY | Đặt Brevo API key trong đúng Render service environment |
| INVALID_EMAIL_ADDRESS | Kiểm tra sender và input email |
| HTTP_401 / HTTP_403 | API key, account/IP authorization, sender/account activation trong Brevo |
| HTTP_400 | Sender và transactional request settings trong dashboard |
| HTTP_429 | Quota/rate limits của provider |
| TIMEOUT | Network/provider; xác nhận đã chọn Brevo trên Render Free |
| MISSING_SMTP_CONFIGURATION | SMTP host/port/password của backend local |
| SMTP Authenticate failure | Gmail App Password, account policy và TLS settings |

Không tự động fallback từ Brevo sang SMTP khi Brevo lỗi trên Render.

## Verification

Tests mới chạy độc lập với MongoDB và không gửi email thật:

~~~powershell
dotnet test REWEAR.Tests/REWEAR.Tests.csproj --filter "FullyQualifiedName~AuthEmailRecoveryTests|FullyQualifiedName~EmailDeliveryTests"
~~~

Commerce integration tests hiện có cần MongoDB test riêng như REWEAR.Tests/README.md.
Trước khi xác nhận live thành công: dùng tài khoản do người thử kiểm soát, đăng ký, nhận thư,
verify OTP, login và thử gửi lại sau lỗi. Không dùng database/migration production để dựng test fixture.

Push source không tự tạo Brevo account, xác minh sender hoặc cài Render environment variables.
Chưa có API key/sender thực tế thì chỉ xác minh được code và tests với provider giả lập.
