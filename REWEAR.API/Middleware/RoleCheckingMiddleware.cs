using REWEAR.Application.DTOs;
using REWEAR.Domain.Entities;

namespace REWEAR.API.Middleware;

/// <summary>
/// Middleware kiểm tra role của user trước khi request tới controller.
/// Ghi log và thêm thông tin role/permission vào HttpContext.Items
/// để các API có thể dùng thêm (ví dụ: lọc dữ liệu theo role).
/// </summary>
public class RoleCheckingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RoleCheckingMiddleware> _logger;

    /// <summary>
    /// Key dùng để lấy lại role sau khi middleware đã chạy.
    /// </summary>
    public const string UserRoleItemKey = "Rewear.UserRole";

    public RoleCheckingMiddleware(RequestDelegate next, ILogger<RoleCheckingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Chỉ xử lý khi user đã đăng nhập (token hợp lệ)
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var roleClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = UserRole.Member;

            if (!string.IsNullOrWhiteSpace(roleClaim) && Enum.TryParse<UserRole>(roleClaim, true, out var parsedRole))
            {
                userRole = parsedRole;
            }
            else
            {
                _logger.LogWarning("Token không chứa role hợp lệ, mặc định về {Role}.", UserRole.Member);
            }

            // Lưu role vào HttpContext.Items cho các tầng sau dùng
            context.Items[UserRoleItemKey] = userRole;

            // Ghi log ai đang gọi API gì với role nào
            _logger.LogInformation(
                "[AuthZ] {Method} {Path} | User: {UserId} | Role: {Role}",
                context.Request.Method,
                context.Request.Path,
                userId,
                userRole);

            // Chặn API nội bộ chỉ dành cho Admin
            if (context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase)
                && userRole != UserRole.Admin)
            {
                _logger.LogWarning(
                    "[AuthZ] Chặn truy cập: User {UserId} ({Role}) thử gọi admin API {Path}",
                    userId, userRole, context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response.WriteAsJsonAsync(new ApiResponse
                {
                    Success = false,
                    Message = "Bạn không có quyền truy cập chức năng này."
                });
                return;
            }
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method cho phép đăng ký RoleCheckingMiddleware gọn gàng trong Program.cs.
/// </summary>
public static class RoleCheckingMiddlewareExtensions
{
    public static IApplicationBuilder UseRoleChecking(this IApplicationBuilder app)
        => app.UseMiddleware<RoleCheckingMiddleware>();
}
