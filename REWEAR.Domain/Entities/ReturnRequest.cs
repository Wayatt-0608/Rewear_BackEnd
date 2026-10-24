using REWEAR.Domain.Enums;
namespace REWEAR.Domain.Entities;

// One full-order return. Partial returns require a separate allocation/refund model.
public sealed class ReturnRequest : LifecycleRecord
{
    public string OrderId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string State { get; set; } = "Requested";
    public string Reason { get; set; } = "";
    public string? ReceiptReference { get; set; }
    public string? ReceivedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? InspectedBy { get; set; }
    public DateTime? InspectedAt { get; set; }
    public List<ReturnItemDecision> Items { get; set; } = new();
    public List<LifecycleEvent> Timeline { get; set; } = new();
}
public sealed class ReturnItemDecision
{
    public string ProductId { get; set; } = "";
    public bool RestockApproved { get; set; }
    public ProductCondition Condition { get; set; }
    public string Note { get; set; } = "";
}
