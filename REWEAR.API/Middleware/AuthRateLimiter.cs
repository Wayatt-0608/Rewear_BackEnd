using System.Collections.Concurrent;
using System.Text.Json;

namespace REWEAR.API.Middleware;

/// <summary>
/// Rate limit cho nhóm route xác thực (đăng nhập, đăng ký, OTP).
/// </summary>
/// <remarks>
/// <para>
/// Hạn mức được chia theo <b>IP + route + email</b>, không chỉ theo IP.
/// Lý do: nhiều người dùng thực tế đi qua cùng một IP công cộng (NAT, Wi-Fi quán
/// cà phê, mạng doanh nghiệp, VPN). Nếu chỉ chia theo IP thì một người nhấn
/// đăng nhập nhiều lần sẽ khóa luôn những người khác cùng IP, dẫn tới lỗi 429
/// hàng loạt mà không liên quan tới họ.
/// </para>
/// <para>
/// Mỗi email vẫn bị giới hạn riêng nên vẫn chống được brute-force mật khẩu:
/// kẻ tấn công đoán nhiều mật khẩu cho cùng một tài khoản vẫn bị chặn.
/// </para>
/// <para>
/// Khi bị từ chối, response trả về đúng format <c>ApiResponse</c> như các API khác
/// kèm header <c>Retry-After</c> để client biết phải chờ bao lâu trước khi thử lại.
/// </para>
/// <para>
/// Đây là middleware tùy biến thay cho <c>EnableRateLimiting</c> của framework
/// vì partition key cần đọc email trong request body, mà framework chỉ hỗ trợ
/// partition factory đồng bộ (đọc body đồng bộ bị Kestrel từ chối).
/// </para>
/// </remarks>
public sealed class AuthRateLimiterMiddleware
{
    /// <summary>Số lần cho phép trong mỗi cửa sổ thời gian, tính theo IP + route + email.</summary>
    public const int PermitLimit = 20;

    /// <summary>Độ dài cửa sổ thời gian tính rate limit.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Header cho client biết số giây cần chờ trước khi thử lại.</summary>
    public const string RetryAfterHeader = "Retry-After";

    /// <summary>Prefix route được middleware này canh giữ.</summary>
    private static readonly string[] ProtectedPrefixes =
    {
        "/api/auth/register",
        "/api/auth/login",
        "/api/auth/forgot-password",
        "/api/auth/verify-otp",
        "/api/auth/reset-password"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<AuthRateLimiterMiddleware> _logger;
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new();
    private long _cleanupTicks;

    public AuthRateLimiterMiddleware(RequestDelegate next, ILogger<AuthRateLimiterMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsProtected(context.Request.Path))
        {
            await _next(context);
            return;
        }

        string email = await ReadEmailAsync(context);
        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string path = context.Request.Path.Value ?? "/";
        string key = string.Concat(ip, "|", path, "|", email);

        var counter = _counters.GetOrAdd(key, _ => new WindowCounter());
        int requestCount = counter.Increment();

        // Dọn các cửa sổ đã hết hạn để dictionary không phình vô hạn.
        // Chạy tối đa một lần mỗi 30 giây để không dọn trên mọi request.
        if (Interlocked.Read(ref _cleanupTicks) < Environment.TickCount64)
        {
            if (Interlocked.Exchange(ref _cleanupTicks, Environment.TickCount64 + 30_000) != 0)
                CleanupExpired();
        }

        if (requestCount > PermitLimit)
        {
            int retryAfter = (int)Math.Ceiling(counter.SecondsRemaining());
            _logger.LogWarning(
                "Auth rate limit exceeded for {Path} from {Ip} (email={Email}). Retry-After={RetryAfter}s.",
                path, ip, email, retryAfter);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers[RetryAfterHeader] = retryAfter.ToString();
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Bạn đã thử quá nhiều lần. Vui lòng thử lại sau " + retryAfter + " giây.",
                data = (object?)null,
                errorCode = "RATE_LIMITED"
            });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Kiểm tra route có nằm trong nhóm được bảo vệ hay không (không phân biệt hoa thường).
    /// </summary>
    private static bool IsProtected(PathString path)
    {
        if (!path.HasValue) return false;
        string value = path.Value!;
        foreach (var prefix in ProtectedPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Đọc trường "email" từ request body để tách quota theo tài khoản.
    /// </summary>
    /// <remarks>
    /// Phải đọc bất đồng bộ: Kestrel chặn đọc đồng bộ trên request body
    /// (InvalidOperationException "Synchronous operations are disallowed").
    /// Body được buffer lại và reset về đầu để controller vẫn đọc được nguyên body.
    /// </remarks>
    private static async Task<string> ReadEmailAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPut(context.Request.Method))
            return "-";

        bool isJson = context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false;
        if (!isJson) return "-";

        try
        {
            if (!context.Request.Body.CanRead) return "-";

            // EnableBuffering phải được gọi TRƯỚC lần đọc body đầu tiên.
            context.Request.EnableBuffering();

            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            string raw = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(raw)) return "-";

            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "-";
            if (!document.RootElement.TryGetProperty("email", out var emailElement)) return "-";
            if (emailElement.ValueKind != JsonValueKind.String) return "-";

            string email = emailElement.GetString()?.Trim().ToLowerInvariant() ?? "";
            return email.Length == 0 ? "-" : email;
        }
        catch (JsonException)
        {
            // Body không phải JSON hợp lệ -> không lấy được email, dùng fallback IP + route.
            try { context.Request.Body.Position = 0; } catch { /* ignore */ }
            return "-";
        }
        catch (Exception)
        {
            try { context.Request.Body.Position = 0; } catch { /* ignore */ }
            return "-";
        }
    }

    /// <summary>
    /// Xoá các mục đã quá hạn khỏi dictionary để bộ nhớ không phình vô hạn.
    /// </summary>
    private void CleanupExpired()
    {
        DateTime now = DateTime.UtcNow;
        foreach (var pair in _counters)
        {
            if (pair.Value.IsExpired(now))
                _counters.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>
    /// Bộ đếm số request trong một cửa sổ thời gian cố định.
    /// </summary>
    private sealed class WindowCounter
    {
        private readonly object _lock = new();
        private DateTime _windowStart = DateTime.UtcNow;
        private int _count;

        public int Increment()
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                if (now - _windowStart >= Window)
                {
                    _windowStart = now;
                    _count = 0;
                }
                _count++;
                return _count;
            }
        }

        public double SecondsRemaining()
        {
            lock (_lock)
            {
                double remaining = Window.TotalSeconds - (DateTime.UtcNow - _windowStart).TotalSeconds;
                return remaining > 0 ? remaining : 0;
            }
        }

        public bool IsExpired(DateTime now)
        {
            lock (_lock)
            {
                return now - _windowStart >= Window;
            }
        }
    }
}
