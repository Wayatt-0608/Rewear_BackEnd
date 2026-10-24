using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
namespace REWEAR.Application.Services;

public sealed class ReturnService(ILifecycleStore store, IOrderRepository orders, IPaymentRepository payments, IShippingRepository shipping,
    IUserRepository users, ICommerceUnitOfWork transactions, IOrderStatusHistoryRepository history)
{
    public Task<List<ReturnRequest>> ListAsync() => store.ListAsync<ReturnRequest>();
    public async Task<ReturnRequest?> OwnAsync(string orderId, string owner)
    { var order = await orders.GetByIdAsync(orderId); return order?.UserId == owner ? await store.GetAsync<ReturnRequest>(orderId) : null; }

    public Task<ReturnRequest> RequestAsync(string orderId, string owner, string reason) => transactions.ExecuteAsync(async () =>
    {
        Required(reason); var order = await orders.GetByIdAsync(orderId);
        if (order == null || order.UserId != owner) throw new UnauthorizedAccessException();
        var existing = await store.GetAsync<ReturnRequest>(orderId); if (existing != null) return existing;
        if (order.Status != OrderStatus.Delivered) throw new InvalidOperationException("Chỉ yêu cầu trả hàng cho đơn đã giao; liên hệ cửa hàng với trường hợp khác.");
        var p = await payments.GetLatestByOrderIdAsync(orderId);
        if (p?.Status != PaymentStatus.Paid || p.RequiresReconciliation) throw new InvalidOperationException("Thanh toán cần được kiểm tra trước khi trả hàng.");
        var record = new ReturnRequest { Id = orderId, OrderId = orderId, CustomerId = owner, Reason = reason };
        record.Timeline.Add(new() { To = "Requested", ActorId = owner, Note = reason }); await store.InsertAsync(record);
        var from = order.Status; order.Status = OrderStatus.ReturnRequested; await orders.UpdateAsync(order);
        await History(order, from, "Người mua yêu cầu trả toàn bộ đơn; chờ nhân viên tiếp nhận.", owner, OrderStatusChangedBy.Customer);
        return record;
    });
    public Task<ReturnRequest> ReceiveAsync(string orderId, ReceiveReturnDto request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); Required(request.Reference);
        var order = await orders.GetByIdAsync(orderId) ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");
        var record = await store.GetAsync<ReturnRequest>(orderId);
        if (record?.ReceivedAt != null)
        { if (record.ReceiptReference != request.Reference) throw new InvalidOperationException("Đã có chứng từ tiếp nhận khác."); return record; }
        if (order.Status is not (OrderStatus.Failed or OrderStatus.Delivered or OrderStatus.ReturnRequested)) throw new InvalidOperationException("Đơn chưa đủ điều kiện tiếp nhận hàng hoàn.");
        var shipment = await shipping.GetByOrderIdAsync(orderId);
        if (shipment == null || shipment.Status is not (ShippingStatus.Failed or ShippingStatus.Delivered)) throw new InvalidOperationException("Cần xác minh vận chuyển trước khi nhận hoàn.");
        var payment = await payments.GetLatestByOrderIdAsync(orderId);
        if (payment?.Status != PaymentStatus.Paid || payment.RequiresReconciliation) throw new InvalidOperationException("Cần đối soát thanh toán trước khi nhận hoàn.");
        if (record == null)
        {
            Required(request.Reason); record = new() { Id = orderId, OrderId = orderId, CustomerId = order.UserId, Reason = request.Reason };
            await store.InsertAsync(record);
        }
        record.ReceiptReference = request.Reference; record.ReceivedAt = DateTime.UtcNow; record.ReceivedBy = actor; record.State = "Received";
        record.Timeline.Add(new() { From = "Requested", To = "Received", ActorId = actor, Note = "Đã nhận đủ hàng theo chứng từ: " + request.Reference }); await store.SaveAsync(record);
        shipment.Status = ShippingStatus.Returned; await shipping.UpdateAsync(shipment);
        foreach (var item in order.Items) await MoveInventory(item.ProductId, "ReturnAwaitingInspection", orderId, actor, "Đã nhận hàng hoàn; chưa kiểm định.");
        var from = order.Status; order.Status = OrderStatus.ReturnReceived; await orders.UpdateAsync(order);
        await History(order, from, "Đã nhận hàng hoàn; chưa kiểm định và chưa mở bán lại.", actor);
        return record;
    });
    public Task<ReturnRequest> InspectAsync(string orderId, InspectReturnDto request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); var record = await store.GetAsync<ReturnRequest>(orderId) ?? throw new InvalidOperationException("Chưa tiếp nhận hàng hoàn.");
        if (record.State != "Received") throw new InvalidOperationException("Chỉ kiểm định sau khi đã nhận đủ hàng; kết quả đã ghi không được ghi đè.");
        var order = (await orders.GetByIdAsync(orderId))!;
        if (request.Items.Count != order.Items.Count || request.Items.Select(i => i.ProductId).Distinct().Count() != order.Items.Count
            || request.Items.Any(i => !order.Items.Any(o => o.ProductId == i.ProductId) || !Enum.IsDefined(i.Condition) || string.IsNullOrWhiteSpace(i.Note)))
            throw new ArgumentException("Cần kiểm định và ghi chú từng sản phẩm trong toàn bộ đơn.");
        var p = await payments.GetLatestByOrderIdAsync(orderId);
        if (p?.Status != PaymentStatus.Paid) throw new InvalidOperationException("Trạng thái thanh toán đã thay đổi.");
        record.Items = request.Items; record.InspectedAt = DateTime.UtcNow; record.InspectedBy = actor; record.State = "Inspected";
        foreach (var item in record.Items) await MoveInventory(item.ProductId, item.RestockApproved ? "ReturnAwaitingRefund" : "Quarantined", orderId, actor, item.Note);
        record.Timeline.Add(new() { From = "Received", To = "Inspected", ActorId = actor, Note = "Đã kiểm định; hàng chờ hoàn tiền, món không được duyệt bán lại tiếp tục cách ly." }); await store.SaveAsync(record);
        p.RefundState = "Requested"; p.RefundRequestedAt ??= DateTime.UtcNow; await payments.UpdateAsync(p);
        var from = order.Status; order.Status = OrderStatus.Returned; await orders.UpdateAsync(order);
        await History(order, from, "Đã kiểm định hàng hoàn và yêu cầu hoàn tiền; tồn kho chưa mở bán.", actor);
        return record;
    });
    private async Task Operator(string id)
    { if ((await users.GetByIdAsync(id))?.Role is not (UserRole.Admin or UserRole.Staff)) throw new UnauthorizedAccessException(); }
    private static void Required(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 500) throw new ArgumentException("Cần nội dung/chứng từ từ 1 đến 500 ký tự."); }
    private async Task MoveInventory(string productId, string state, string orderId, string actor, string note)
    {
        foreach (var item in await store.FindAsync<InventoryItem>(i => i.ProductId == productId && i.State != "Consumed"))
        {
            var from = item.State; var custody = item.Custody; item.State = state; item.Custody = "Warehouse"; item.Location = "Kho REWEAR"; await store.SaveAsync(item);
            await store.InsertAsync(new InventoryMovement { InventoryId = item.Id, FromState = from, ToState = state,
                FromCustody = custody, ToCustody = item.Custody, ActorId = actor, RelatedId = orderId, Reason = note });
        }
    }
    private Task History(Order o, OrderStatus from, string note, string actor, OrderStatusChangedBy by = OrderStatusChangedBy.Staff) => history.CreateAsync(new()
    { OrderId = o.Id, FromStatus = from, ToStatus = o.Status, Note = note, ChangedByUserId = actor, ChangedBy = by });
}
