using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace REWEAR.API.Filters;

/// <summary>
/// Operation filter bổ sung thông tin cho từng endpoint trên Swagger UI:
/// - OperationId: đặt tên rõ ràng, ổn định để Frontend sinh client tự động
///   (NSwag / Swagger Codegen) không phải đoán tên hàm.
/// - Summary: lấy từ thẻ XML &lt;summary&gt; của action; nếu thiếu thì tự sinh
///   từ tên method (vd: GetAllUsers -> "Get All Users").
/// Mục tiêu: Frontend nhìn vào Swagger là hiểu ngay endpoint này dùng để làm gì.
/// </summary>
public class SwaggerOperationInfoFilter : IOperationFilter
{
    /// <summary>
    /// Tách camelCase thành từng từ để sinh mô tả dễ đọc.
    /// Ví dụ: "GetAllUsers" -> "Get All Users", "VerifyOtp" -> "Verify Otp".
    /// </summary>
    private static string SplitCamelCase(string name)
    {
        // Tách ở ranh giới chữ thường/số + chữ hoa: "GetAll" -> "Get All"
        var spaced = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
        // Tách ở ranh giới chữ hoa liên tiếp + chữ hoa theo sau: "GetURL" -> "Get URL"
        spaced = Regex.Replace(spaced, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        return spaced;
    }

    /// <summary>
    /// Bỏ chữ "Controller" ở cuối tên controller nếu có (trường hợp đặt tên đầy đủ).
    /// </summary>
    private static string TrimControllerSuffix(string name)
    {
        const string suffix = "Controller";
        return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var descriptor = context.ApiDescription?.ActionDescriptor as ControllerActionDescriptor;
        var controllerName = TrimControllerSuffix(descriptor?.ControllerName ?? "Api");
        var actionName = context.MethodInfo?.Name;

        // ====== OperationId: tên hàm mà Frontend sẽ gọi khi sinh client tự động ======
        // Bỏ hậu tố "Async" cho gọn: Products_CreateAsync -> "Products_Create"
        if (!string.IsNullOrEmpty(actionName))
        {
            var methodName = actionName.EndsWith("Async", StringComparison.Ordinal)
                && actionName.Length > "Async".Length
                    ? actionName[..^"Async".Length]
                    : actionName;

            operation.OperationId = $"{controllerName}_{methodName}";
        }

        // ====== Summary: dòng tiêu đề đậm, thứ Frontend đọc đầu tiên ======
        // Ưu tiên <summary> trong XML comment; thiếu thì tự sinh từ tên method.
        if (string.IsNullOrWhiteSpace(operation.Summary))
        {
            operation.Summary = !string.IsNullOrEmpty(actionName)
                ? SplitCamelCase(actionName)
                : null;
        }

        // Vẫn rỗng thì báo rõ cần bổ sung thay vì để trống trên Swagger UI.
        if (string.IsNullOrWhiteSpace(operation.Summary))
        {
            operation.Summary = "TODO: bổ sung mô tả";
        }
    }
}
