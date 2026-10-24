using NUnit.Framework;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Enums;
namespace REWEAR.Tests;
public partial class CommerceTests
{
    [Test]
    public async Task ExpirationReleasesEntireBatchBeforeAnyGatewayLookup()
    {
        var first = await Product(); var second = await Product();
        foreach (var product in new[] { first, second })
        {
            var buyer = await Buyer(product); var order = await Checkout(product, buyer.User, buyer.Address); var payment = await Session(order);
            payment.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await Payments.UpdateAsync(payment);
            var stored = (await Orders.GetByIdAsync(order.Id))!; stored.ReservationExpiresAt = payment.ExpiresAt; await Orders.UpdateAsync(stored);
        }
        var observations = new List<bool>();
        Gateway.LookupOverride = async _ =>
        {
            observations.Add((await Products.GetByIdAsync(first.Id))!.StockQuantity == 1 && (await Products.GetByIdAsync(second.Id))!.StockQuantity == 1);
            return null;
        };
        Assert.That(await PaymentService.ExpireStalePaymentsAsync(), Is.EqualTo(2));
        Assert.That(observations, Is.Not.Empty); Assert.That(observations, Is.All.True);
    }
    [Test]
    public async Task ConcurrentSessionInitializationCreatesOneGatewayIdentity()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PaymentService.CreateSessionAsync(order.Id)));
        Assert.That(await Payments.GetByOrderIdAsync(order.Id), Has.Count.EqualTo(1));
        Assert.That(Gateway.CreateCalls, Is.EqualTo(1));
    }
    [Test]
    public async Task DifferentReceiptReferencesRecordExtraMoneyWithoutSellingTwice()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price, "fixture-first");
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price, "fixture-first");
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price, "fixture-extra");
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price, "fixture-extra");
        var stored = (await Payments.GetLatestByOrderIdAsync(order.Id))!;
        Assert.That(stored.ReceivedAmount, Is.EqualTo(p.Price * 2)); Assert.That(stored.Receipts, Has.Count.EqualTo(2));
        Assert.That(stored.RequiresReconciliation, Is.True); Assert.That(stored.RefundState, Is.EqualTo("Requested"));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.Zero);
        Assert.That(await History.GetByOrderIdAsync(order.Id), Has.Count.EqualTo(2));
    }
    [Test]
    public async Task MoneyReceivedAfterRefundDoesNotInventAnotherRefundOrRestock()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price, "fixture-first");
        await PaymentService.RefundAsync(order.Id, "Fixture refund");
        await PaymentService.ConfirmManualRefundAsync(order.Id, "fixture-refunded", p.Price, "admin-fixture");
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price, "fixture-after-refund");
        var stored = (await Payments.GetLatestByOrderIdAsync(order.Id))!;
        Assert.That(stored.ReceivedAmount, Is.EqualTo(p.Price * 2)); Assert.That(stored.RefundedAmount, Is.EqualTo(p.Price));
        Assert.That(stored.RequiresReconciliation, Is.True); Assert.That(stored.RefundState, Is.EqualTo("RequiresSeparateRefund"));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.EqualTo(1));
        var metrics = await new REWEAR.Infrastructure.Services.OperationsQueries(Context, OrderService, Payments, Shippings, History)
            .MetricsAsync(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));
        Assert.That(metrics.RefundedAmount, Is.EqualTo(p.Price));
    }
    [Test]
    public async Task WorkerReconcilesLateMoneyEvenAfterExpiryAndWithoutBrowser()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        payment.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await Payments.UpdateAsync(payment);
        var storedOrder = (await Orders.GetByIdAsync(order.Id))!; storedOrder.ReservationExpiresAt = payment.ExpiresAt; await Orders.UpdateAsync(storedOrder);
        await PaymentService.ExpireStalePaymentsAsync();
        payment = (await Payments.GetLatestByOrderIdAsync(order.Id))!; payment.LastGatewayCheckedAt = DateTime.UtcNow.AddMinutes(-6); await Payments.UpdateAsync(payment);
        Gateway.Transaction = new() { PaymentLinkId = payment.PaymentLinkId!, Reference = payment.GatewayOrderCode.ToString(), Amount = (int)p.Price, PaidAmount = p.Price, Status = "PAID" };
        await PaymentService.ExpireStalePaymentsAsync();
        Assert.That((await Payments.GetLatestByOrderIdAsync(order.Id))!.RequiresReconciliation, Is.True);
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.PaymentExpired));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.EqualTo(1));
    }
    [Test]
    public async Task PartialMoneyOnCancelledGatewaySessionIsRecordedForReconciliation()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        Gateway.Transaction = new() { PaymentLinkId = payment.PaymentLinkId!, Reference = payment.GatewayOrderCode.ToString(), Amount = (int)p.Price, PaidAmount = 100000, Status = "CANCELLED" };
        await PaymentService.ReconcileAsync(order.Id);
        payment = (await Payments.GetLatestByOrderIdAsync(order.Id))!;
        Assert.That(payment.ReceivedAmount, Is.EqualTo(100000)); Assert.That(payment.RequiresReconciliation, Is.True);
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.PaymentExpired));
    }
    [Test]
    public async Task ReconciliationRejectsMismatchedGatewayOrderCode()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        Gateway.Transaction = new() { PaymentLinkId = payment.PaymentLinkId!, Reference = "different-gateway-order", Amount = (int)p.Price, PaidAmount = p.Price, Status = "PAID" };
        await PaymentService.ReconcileAsync(order.Id);
        Assert.That((await Payments.GetLatestByOrderIdAsync(order.Id))!.Status, Is.EqualTo(PaymentStatus.Pending));
    }
}
