using REWEAR.Application.DTOs;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
namespace REWEAR.Application.Services;

public static class DeliveryHistoryMapping
{
    public static readonly IReadOnlyDictionary<string, string> FailureReasons = new Dictionary<string, string>
    {
        ["CustomerUnavailable"] = "Người nhận chưa thể nhận hàng", ["Unreachable"] = "Chưa liên hệ được người nhận",
        ["IncorrectAddress"] = "Cần xác minh lại địa chỉ giao hàng", ["Refused"] = "Người nhận từ chối nhận hàng", ["Other"] = "Giao hàng chưa thành công; REWEAR đang xử lý"
    };
    public static DeliveryHistoryEvent? Map(OrderStatusHistory h, bool internalView)
    {
        string code = h.EventType ?? (h.FromStatus == null ? "OrderPlaced" : h.FromStatus != h.ToStatus ? h.ToStatus.ToString() : "");
        string label = code switch {
            "OrderPlaced" => "Đặt hàng thành công", "Confirmed" => "Đã xác nhận thanh toán", "Assigned" => "Đã phân công người giao hàng",
            "Reassigned" => "Đã cập nhật người giao hàng", "PickedUp" => "Người giao hàng đã lấy kiện hàng", "InTransit" or "Shipping" => "Đang giao đến người nhận",
            "Delivered" => "Giao hàng thành công", "Failed" or "DeliveryFailed" => h.CustomerMessage ?? "Giao hàng chưa thành công; REWEAR đang xử lý",
            "RedeliveryRequested" => "REWEAR đã sắp xếp giao lại", "ReturnRequested" => "Đã yêu cầu trả hàng", "ReturnReceived" => "REWEAR đã nhận hàng hoàn",
            "Returned" => "Đã kiểm định hàng hoàn", "Cancelled" => "Đơn hàng đã được hủy", "PaymentExpired" => "Đơn hàng hết hạn thanh toán", _ => "" };
        return label.Length == 0 ? null : new() { Code = code, Label = label, OccurredAt = h.CreatedAt == DateTime.MinValue ? null : h.CreatedAt,
            Note = internalView ? h.Note : null };
    }
}

public partial class ShippingService
{
    private Task AssignmentHistoryAsync(Order order, Shipping shipment, string kind, string? actor) => _historyRepository.CreateAsync(new()
    {
        OrderId = order.Id, FromStatus = order.Status, ToStatus = order.Status, EventType = kind, ShipmentId = shipment.Id,
        AssignedShipperId = shipment.ShipperId, ToShippingStatus = shipment.Status.ToString(),
        ChangedBy = OrderStatusChangedBy.Staff, ChangedByUserId = actor, Note = kind == "Assigned" ? "Phân công người giao hàng." : "Đổi phân công trước khi lấy hàng."
    });
    private async Task<ShippingResponse> BuildDeliveryAsync(Shipping shipping, Order order, bool internalView)
    {
        var response = BuildShippingResponse(shipping);
        response.AssignedAt = shipping.AssignedAt; response.PickedUpAt = shipping.PickedUpAt;
        response.RecipientName = order.ShippingRecipientName; response.RecipientPhone = order.ShippingPhoneNumber;
        response.ShippingAddress = order.FullShippingAddress; response.DeliveryNotes = order.ShippingNote;
        var p = await _payments.GetLatestByOrderIdAsync(order.Id);
        response.PaymentStatus = p?.RequiresReconciliation == true ? "ReconciliationRequired" : p?.Status.ToString() ?? "Pending";
        response.Items = order.Items.Select(i => new DeliveryItemResponse { ProductTitle = i.ProductTitle, ProductImageUrl = i.ProductImageUrl, Quantity = i.Quantity, Size = i.Size, Color = i.Color }).ToList();
        response.History = (await _historyRepository.GetByOrderIdAsync(order.Id)).Select(h => DeliveryHistoryMapping.Map(h, internalView)).OfType<DeliveryHistoryEvent>().ToList();
        if (!internalView)
        {
            response.ShipperId = null;
            response.LastFailureReason = shipping.LastFailureCode != null && DeliveryHistoryMapping.FailureReasons.TryGetValue(shipping.LastFailureCode, out var reason) ? reason : shipping.Status == ShippingStatus.Failed ? "REWEAR đang xử lý lần giao chưa thành công." : null;
        }
        return response;
    }
}
