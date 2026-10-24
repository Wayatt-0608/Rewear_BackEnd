using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Application.Services;

namespace REWEAR.API.Controllers;

[ApiController, Route("api/operations"), Authorize(Policy = "StaffOrAdmin")]
[TypeFilter(typeof(REWEAR.API.Filters.ObjectIdRouteFilter))]
public sealed class OperationsController(CircularFashionService operations, ICloudinaryService images, IUserRepository users, IOperationsQueries queries, IPaymentService payments) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    [HttpGet("orders")] public Task<IActionResult> Orders([FromQuery] OperationsOrderQuery query) => Run(() => queries.OrdersAsync(query));
    [HttpPost("orders/{id}/refund")]
    public async Task<IActionResult> RequestRefund(string id, RefundReasonRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { message = "Cần lý do hoàn tiền." });
        var result = await payments.RefundAsync(id, request.Reason);
        return result.Success ? Ok(result) : Conflict(result);
    }
    [HttpPost("orders/{id}/reconcile")]
    public Task<IActionResult> Reconcile(string id) => Run(() => payments.ReconcileAsync(id));
    [HttpGet("orders/{id}")] public async Task<IActionResult> Order(string id)
    {
        try { var detail = await queries.OrderAsync(id); return detail == null ? NotFound(new { message = "Không tìm thấy đơn hàng." }) : Ok(new { success = true, data = detail }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
    [HttpGet("metrics"), Authorize(Policy = "AdminOnly")]
    public Task<IActionResult> Metrics([FromQuery] DateTime? from, [FromQuery] DateTime? to) => Run(() => queries.MetricsAsync(from ?? DateTime.UtcNow.Date.AddDays(-29), to ?? DateTime.UtcNow.Date.AddDays(1)));
    [HttpGet("inventory")] public Task<IActionResult> Inventory() => Run(operations.InventoryAsync);
    [HttpGet("inventory/{id}/movements")] public Task<IActionResult> Movements(string id) => Run(() => operations.MovementsAsync(id));
    [HttpGet("inventory/{id}/inspections")] public Task<IActionResult> InventoryInspections(string id) => Run(() => operations.InspectionsAsync(id));
    [HttpGet("payouts")] public Task<IActionResult> Payouts() => Run(operations.PayoutsAsync);
    [HttpGet("operators")] public Task<IActionResult> Operators() => Run(async () => (await users.GetAllAsync())
        .Where(u => u.Role is REWEAR.Domain.Entities.UserRole.Admin or REWEAR.Domain.Entities.UserRole.Staff)
        .Select(u => new { u.Id, u.FullName, Role = u.Role.ToString() }).ToList());
    [HttpGet("shippers")] public Task<IActionResult> Shippers() => Run(async () => (await users.GetAllAsync())
        .Where(u => u.Role == REWEAR.Domain.Entities.UserRole.Shipper)
        .Select(u => new { u.Id, u.FullName }).ToList());
    [HttpPost("sourcing/{id}/payout")] public Task<IActionResult> Payout(string id, ConfirmPayoutRequest request) => Run(() => operations.ConfirmPayoutAsync(id, request, Actor));
    [HttpPost("inventory/{id}/qc")] public Task<IActionResult> InventoryQC(string id, QualityDecisionRequest request) => Run(() => operations.InspectInventoryAsync(id, request, Actor));
    [HttpPost("inventory/{id}/publish")] public Task<IActionResult> PublishInventoryResale(string id, PublishResaleRequest request) => Run(() => operations.PublishResaleAsync(id, request, Actor));
    [HttpPost("inventory/{id}/return-to-sender")] public Task<IActionResult> ReturnToSender(string id, ReturnSourceRequest request) => Run(() => operations.ReturnSourceAsync(id, request.Reference, Actor));
    [HttpGet("projects")] public Task<IActionResult> Projects() => Run(operations.ProjectsAsync);
    [HttpGet("projects/{id}")] public async Task<IActionResult> Project(string id)
    {
        var project = await operations.ProjectAsync(id);
        return project == null ? NotFound(new { message = "Không tìm thấy dự án." })
            : Ok(new { success = true, data = new { Project = project, Inspections = await operations.InspectionsAsync(id) } });
    }
    [HttpPost("projects")] public Task<IActionResult> Create(CreateUpcyclingRequest request) => Run(() => operations.CreateProjectAsync(request, Actor));
    [HttpPatch("projects/{id}/status")] public Task<IActionResult> Update(string id, UpdateProjectRequest request) => Run(() => operations.UpdateProjectAsync(id, request, Actor));
    [HttpPost("projects/{id}/costs")] public Task<IActionResult> Cost(string id, RecordCostRequest request) => Run(() => operations.AddCostAsync(id, request, Actor));
    [HttpPost("projects/{id}/qc")] public Task<IActionResult> QC(string id, QualityDecisionRequest request) => Run(() => operations.InspectProjectAsync(id, request, Actor));
    [HttpPost("projects/{id}/publish")] public Task<IActionResult> Publish(string id, PublishProjectRequest request) => Run(() => operations.PublishProjectAsync(id, request, Actor));

    [HttpPost("images"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file.Length is < 12 or > 5 * 1024 * 1024) return BadRequest(new { message = "Ảnh phải nhỏ hơn 5 MB." });
        await using var stream = file.OpenReadStream();
        var header = new byte[12]; await stream.ReadExactlyAsync(header); stream.Position = 0;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        bool valid = (extension is ".jpg" or ".jpeg" && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff)
            || (extension == ".png" && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            || (extension == ".webp" && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP");
        if (!valid) return BadRequest(new { message = "Chỉ nhận nội dung ảnh JPEG, PNG hoặc WebP hợp lệ." });
        var result = await images.UploadImageAsync(new() { FileStream = stream, FileName = Path.GetFileName(file.FileName), Folder = "upcycling", PublicId = Guid.NewGuid().ToString("N") });
        return result.Success ? Ok(new { data = new { url = result.Url } }) : StatusCode(502, new { message = "Chưa tải ảnh lên được, vui lòng thử lại." });
    }

    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(new { success = true, data = await action() }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
public sealed class ReturnSourceRequest { public string Reference { get; set; } = ""; }
public sealed class RefundReasonRequest { public string Reason { get; set; } = ""; }
