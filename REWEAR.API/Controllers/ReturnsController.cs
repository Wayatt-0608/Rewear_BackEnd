using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Services;
namespace REWEAR.API.Controllers;

[ApiController, Route("api/returns"), Authorize]
[TypeFilter(typeof(REWEAR.API.Filters.ObjectIdRouteFilter))]
public sealed class ReturnsController(ReturnService returns) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    [HttpGet("orders/{orderId}")] public async Task<IActionResult> Own(string orderId)
    { var record = await returns.OwnAsync(orderId, Actor); return record == null ? NotFound(new { message = "Chưa có yêu cầu trả hàng." }) : Ok(new { data = record }); }
    [HttpPost("orders/{orderId}")] public Task<IActionResult> CreateRequest(string orderId, RequestReturnDto request) => Run(() => returns.RequestAsync(orderId, Actor, request.Reason));
    [HttpGet, Authorize(Policy = "StaffOrAdmin")] public Task<IActionResult> List() => Run(returns.ListAsync);
    [HttpPost("orders/{orderId}/receive"), Authorize(Policy = "StaffOrAdmin")]
    public Task<IActionResult> Receive(string orderId, ReceiveReturnDto request) => Run(() => returns.ReceiveAsync(orderId, request, Actor));
    [HttpPost("orders/{orderId}/inspect"), Authorize(Policy = "StaffOrAdmin")]
    public Task<IActionResult> Inspect(string orderId, InspectReturnDto request) => Run(() => returns.InspectAsync(orderId, request, Actor));
    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(new { success = true, data = await action() }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
