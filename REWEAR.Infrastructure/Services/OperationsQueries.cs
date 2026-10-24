using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Services;

public sealed class OperationsQueries(MongoDbContext context, IOrderService orderService, IPaymentRepository payments,
    IShippingRepository shipping, IOrderStatusHistoryRepository history) : IOperationsQueries
{
    public async Task<OperationsOrderPage> OrdersAsync(OperationsOrderQuery query)
    {
        int page = Math.Max(1, query.Page), size = Math.Clamp(query.PageSize, 1, 100);
        var f = Builders<Order>.Filter; var filter = f.Empty;
        if (query.Status.HasValue) filter &= f.Eq(o => o.Status, query.Status.Value);
        if (query.From.HasValue) filter &= f.Gte(o => o.CreatedAt, query.From.Value.ToUniversalTime());
        if (query.To.HasValue) filter &= f.Lt(o => o.CreatedAt, query.To.Value.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            if (query.Search.Length > 120) throw new ArgumentException("Từ khóa tối đa 120 ký tự.");
            var regex = new BsonRegularExpression(Regex.Escape(query.Search.Trim()), "i");
            filter &= f.Or(f.Regex(o => o.OrderCode, regex), f.Regex(o => o.ShippingRecipientName, regex), f.Regex(o => o.ShippingPhoneNumber, regex));
        }
        var result = new OperationsOrderPage { Page = page, PageSize = size, Total = await context.Orders.CountDocumentsAsync(filter) };
        var orders = await context.Orders.Find(filter).SortByDescending(o => o.CreatedAt).Skip((page - 1) * size).Limit(size).ToListAsync();
        foreach (var order in orders) { var dto = await orderService.GetOperationalDetailAsync(order.Id); if (dto != null) result.Items.Add(dto); }
        return result;
    }
    public async Task<OperationsOrderDetail?> OrderAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _)) throw new ArgumentException("Mã đơn hàng không hợp lệ.");
        var order = await orderService.GetOperationalDetailAsync(id); if (order == null) return null;
        var p = await payments.GetLatestByOrderIdAsync(id);
        return new() { Order = order, Return = await context.Collection<ReturnRequest>("ReturnRequests").Find(r => r.Id == id).FirstOrDefaultAsync(), Shipping = await shipping.GetByOrderIdAsync(id), History = await history.GetByOrderIdAsync(id),
            Payment = p == null ? null : new() { Status = p.Status.ToString(), Amount = p.Amount, ReceivedAmount = p.ReceivedAmount, PaidAt = p.PaidAt,
                RequiresReconciliation = p.RequiresReconciliation, ReconciliationReason = p.ReconciliationReason, RefundState = p.RefundState,
                RefundReference = p.RefundReference, RefundedAt = p.RefundedAt } };
    }
    public async Task<OperationsMetrics> MetricsAsync(DateTime from, DateTime to)
    {
        from = from.ToUniversalTime(); to = to.ToUniversalTime();
        if (to <= from || to - from > TimeSpan.FromDays(366)) throw new ArgumentException("Chọn khoảng thời gian hợp lệ, tối đa 366 ngày.");
        // Receipt, payout and expense measures each use their own event time, not today's product prices.
        var receipts = await context.Payments.Aggregate().Match(p => p.PaidAt >= from && p.PaidAt < to && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Refunded))
            .Group(_ => 1, g => new { Amount = g.Sum(p => p.ReceivedAmount ?? p.Amount), Count = g.LongCount(), Reconciliation = g.Sum(p => p.RequiresReconciliation ? 1 : 0) }).FirstOrDefaultAsync();
        var cash = await context.Payments.Aggregate().Unwind<Payment, BsonDocument>(p => p.CashEvents)
            .Match(new BsonDocument("CashEvents.RecordedAt", new BsonDocument { { "$gte", from }, { "$lt", to } }))
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "amount", new BsonDocument("$sum", "$CashEvents.Amount") } }).FirstOrDefaultAsync();
        var legacy = await context.Payments.Aggregate().Match(p => p.PaidAt >= from && p.PaidAt < to && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Refunded))
            .Match(Builders<Payment>.Filter.Or(Builders<Payment>.Filter.Exists(p => p.CashEvents, false), Builders<Payment>.Filter.Size(p => p.CashEvents, 0)))
            .Group(_ => 1, g => new { Amount = g.Sum(p => p.ReceivedAmount ?? p.Amount), Count = g.LongCount() }).FirstOrDefaultAsync();
        var refunds = await context.Payments.Aggregate().Match(p => p.Status == PaymentStatus.Refunded && p.RefundedAt >= from && p.RefundedAt < to)
            .Group(_ => 1, g => new { Amount = g.Sum(p => p.RefundedAmount ?? p.ReceivedAmount ?? p.Amount) }).FirstOrDefaultAsync();
        var payouts = await context.Collection<PurchasePayout>("PurchasePayouts").Aggregate().Match(p => p.State == "Confirmed" && p.PaidAt >= from && p.PaidAt < to)
            .Group(_ => 1, g => new { Amount = g.Sum(p => p.Amount) }).FirstOrDefaultAsync();
        var costs = await context.Collection<UpcyclingProject>("UpcyclingProjects").Aggregate()
            .Unwind<UpcyclingProject, BsonDocument>(p => p.Costs)
            .Match(new BsonDocument("Costs.RecordedAt", new BsonDocument { { "$gte", from }, { "$lt", to } }))
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "amount", new BsonDocument("$sum", "$Costs.Amount") } }).FirstOrDefaultAsync();
        var kinds = await context.Collection<UpcyclingProject>("UpcyclingProjects").Aggregate().Unwind<UpcyclingProject, BsonDocument>(p => p.Costs)
            .Match(new BsonDocument("Costs.RecordedAt", new BsonDocument { { "$gte", from }, { "$lt", to } }))
            .Group(new BsonDocument { { "_id", "$Costs.Kind" }, { "amount", new BsonDocument("$sum", "$Costs.Amount") } }).ToListAsync();
        var soldFilter = Builders<Order>.Filter.Where(o => o.PaidAt >= from && o.PaidAt < to && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned);
        var unknownFilter = new BsonDocumentFilterDefinition<Order>(new BsonDocument("$or", new BsonArray {
            new BsonDocument("items", new BsonDocument("$exists", false)), new BsonDocument("items", new BsonDocument("$size", 0)),
            new BsonDocument("items", new BsonDocument("$elemMatch", new BsonDocument("HasCostBasis", new BsonDocument("$ne", true)))) }));
        var unknown = await context.Orders.CountDocumentsAsync(soldFilter & unknownFilter);
        var sold = await context.Orders.Aggregate().Match(soldFilter)
            .Group(_ => 1, g => new { Count = g.LongCount(), Revenue = g.Sum(o => o.TotalAmount), Acquisition = g.Sum(o => o.Items.Sum(i => i.AcquisitionCost * i.Quantity)),
                Production = g.Sum(o => o.Items.Sum(i => i.ProductionCost * i.Quantity)), Unknown = g.Sum(o => o.Items.Any(i => !i.HasCostBasis) ? 1 : 0) }).FirstOrDefaultAsync();
        var result = new OperationsMetrics { From = from, To = to, ReceivedRevenue = (cash == null ? 0 : (decimal)cash["amount"].AsDecimal128) + (legacy?.Amount ?? 0), PaidTransactions = receipts?.Count ?? 0,
            RefundedAmount = refunds?.Amount ?? 0, PurchasePayouts = payouts?.Amount ?? 0, ProductionSpending = costs == null ? 0 : (decimal)costs["amount"].AsDecimal128,
            SoldAcquisitionCost = sold?.Acquisition ?? 0, SoldProductionCost = sold?.Production ?? 0,
            UnknownCostOrders = unknown, UnmatchedPaidTransactions = Math.Max(0, (receipts?.Count ?? 0) - (sold?.Count ?? 0)),
            RecognizedRevenue = sold?.Revenue ?? 0, LegacyCashTransactions = legacy?.Count ?? 0,
            ReconciliationCases = await context.Payments.CountDocumentsAsync(p => p.RequiresReconciliation),
            RefundPendingCases = await context.Payments.CountDocumentsAsync(p => p.RefundState == "Requested"),
            MaterialSpending = kinds.Where(k => k["_id"] == "Material").Sum(k => (decimal)k["amount"].AsDecimal128),
            LaborSpending = kinds.Where(k => k["_id"] == "Labor").Sum(k => (decimal)k["amount"].AsDecimal128) };
        if (result.UnknownCostOrders == 0 && result.ReconciliationCases == 0 && result.RefundedAmount == 0 && result.RefundPendingCases == 0 && result.UnmatchedPaidTransactions == 0 && result.LegacyCashTransactions == 0)
            result.EstimatedGrossProfit = result.RecognizedRevenue - result.SoldAcquisitionCost - result.SoldProductionCost;
        result.Orders = await context.Orders.Aggregate().Group(o => o.Status, g => new StatusCount { State = g.Key.ToString(), Count = g.LongCount() }).ToListAsync();
        result.Inventory = await context.Collection<InventoryItem>("InventoryItems").Aggregate().Group(i => i.State, g => new StatusCount { State = g.Key, Count = g.LongCount() }).ToListAsync();
        result.Projects = await context.Collection<UpcyclingProject>("UpcyclingProjects").Aggregate().Group(p => p.State, g => new StatusCount { State = g.Key, Count = g.LongCount() }).ToListAsync();
        return result;
    }
}
