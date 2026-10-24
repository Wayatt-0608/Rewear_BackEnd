using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.Interfaces;
namespace REWEAR.API.Controllers;
[ApiController, Route("api/products/{id}/journey"), AllowAnonymous]
[TypeFilter(typeof(REWEAR.API.Filters.ObjectIdRouteFilter))]
public sealed class ProductJourneyController(IProductJourneyQuery journeys) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string id)
    {
        var result = await journeys.GetAsync(id);
        return result == null ? NotFound(new { message = "Không tìm thấy hành trình sản phẩm công khai." }) : Ok(new { data = result });
    }
}
