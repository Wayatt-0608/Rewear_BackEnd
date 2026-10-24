using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

public sealed class OperationsOrderQuery
{
    public string? Search { get; set; }
    public OrderStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
public sealed class OperationsOrderPage
{
    public List<OrderResponse> Items { get; set; } = new();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
public sealed class OperationsPaymentSummary
{
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal? ReceivedAmount { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool RequiresReconciliation { get; set; }
    public string? ReconciliationReason { get; set; }
    public string? RefundState { get; set; }
    public string? RefundReference { get; set; }
    public DateTime? RefundedAt { get; set; }
}
public sealed class OperationsOrderDetail
{
    public ReturnRequest? Return { get; set; }
    public OrderResponse Order { get; set; } = new();
    public OperationsPaymentSummary? Payment { get; set; }
    public Shipping? Shipping { get; set; }
    public List<OrderStatusHistory> History { get; set; } = new();
}
public sealed class StatusCount
{
    public string State { get; set; } = "";
    public long Count { get; set; }
}
public sealed class OperationsMetrics
{
    public decimal RecognizedRevenue { get; set; }
    public decimal MaterialSpending { get; set; }
    public decimal LaborSpending { get; set; }
    public long LegacyCashTransactions { get; set; }
    public long UnmatchedPaidTransactions { get; set; }
    public long RefundPendingCases { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal ReceivedRevenue { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal NetReceipts => ReceivedRevenue - RefundedAmount;
    public decimal PurchasePayouts { get; set; }
    public decimal ProductionSpending { get; set; }
    public decimal SoldAcquisitionCost { get; set; }
    public decimal SoldProductionCost { get; set; }
    public decimal? EstimatedGrossProfit { get; set; }
    public long PaidTransactions { get; set; }
    public long UnknownCostOrders { get; set; }
    public long ReconciliationCases { get; set; }
    public List<StatusCount> Orders { get; set; } = new();
    public List<StatusCount> Inventory { get; set; } = new();
    public List<StatusCount> Projects { get; set; } = new();
}
