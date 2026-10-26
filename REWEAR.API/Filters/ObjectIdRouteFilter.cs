using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Bson;
namespace REWEAR.API.Filters;

public sealed class ObjectIdRouteFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var pair in context.RouteData.Values)
        {
            // "productId" dùng cho các route thao tác theo sản phẩm (vd: wishlist).
            if ((pair.Key.Equals("id", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("orderId", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("productId", StringComparison.OrdinalIgnoreCase))
                && !ObjectId.TryParse(pair.Value?.ToString(), out _))
            {
                context.Result = new BadRequestObjectResult(new { success = false, message = "Mã bản ghi không hợp lệ." });
                return;
            }
        }
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
