using NUnit.Framework;
using REWEAR.Application.DTOs;
using REWEAR.Application.Services;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Repositories;
namespace REWEAR.Tests;
public partial class CommerceTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task FailedDeliveryReturnRequiresReceiptInspectionAndRefundBeforeRestock(bool restock)
    {
        var product = await Product(); var buyer = await Buyer(product); var order = await Checkout(product, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, product.Price);
        var staff = new User { Role = UserRole.Staff, Email = "staff-return@example.test" }; await Users.CreateAsync(staff);
        var shipper = new User { Role = UserRole.Shipper, Email = "shipper-return@example.test" }; await Users.CreateAsync(shipper);
        await ShippingService.CreateShippingAsync(order.Id, shipper.Id, ShippingMethod.Standard, null);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 5 }, shipper.Id, UserRole.Shipper);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 1, TrackingNumber = "fixture-return" }, shipper.Id, UserRole.Shipper);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 3, Note = "Fixture recipient unavailable" }, shipper.Id, UserRole.Shipper);
        var returns = new ReturnService(new LifecycleStore(Context), Orders, Payments, Shippings, Users, Transactions, History);
        Assert.ThrowsAsync<InvalidOperationException>(() => returns.InspectAsync(order.Id, new(), staff.Id));
        Assert.That((await PaymentService.RefundAsync(order.Id, "Fixture failed")).Success, Is.False);
        await returns.ReceiveAsync(order.Id, new() { Reference = "fixture-receipt", Reason = "Fixture return" }, staff.Id);
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.Zero);
        Assert.That((await PaymentService.ConfirmManualRefundAsync(order.Id, "fixture-refund", product.Price, "admin-fixture")).Success, Is.False);
        await returns.InspectAsync(order.Id, new() { Items = new() { new() { ProductId = product.Id, RestockApproved = restock, Condition = ProductCondition.Good, Note = "Fixture inspection" } } }, staff.Id);
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.Zero);
        Assert.That((await PaymentService.ConfirmManualRefundAsync(order.Id, "fixture-refund", product.Price, "admin-fixture")).Success, Is.True);
        await PaymentService.ConfirmManualRefundAsync(order.Id, "fixture-refund", product.Price, "admin-fixture");
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.EqualTo(restock ? 1 : 0));
        Assert.That((await Shippings.GetByOrderIdAsync(order.Id))!.Status, Is.EqualTo(ShippingStatus.Returned));
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.Returned));
        Assert.That((await Payments.GetLatestByOrderIdAsync(order.Id))!.Status, Is.EqualTo(PaymentStatus.Refunded));
    }
    [Test]
    public async Task ReturnRequestRejectsOtherOwnerAndUnreceivedOrder()
    {
        var product = await Product(); var buyer = await Buyer(product); var order = await Checkout(product, buyer.User, buyer.Address);
        var returns = new ReturnService(new LifecycleStore(Context), Orders, Payments, Shippings, Users, Transactions, History);
        var other = new User { Email = "foreign-return@example.test" }; await Users.CreateAsync(other);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => returns.RequestAsync(order.Id, other.Id, "Fixture"));
        Assert.ThrowsAsync<InvalidOperationException>(() => returns.RequestAsync(order.Id, buyer.User.Id, "Fixture"));
    }
    [Test]
    public async Task DeliveredBuyerCanRequestReturnOnceAndSeesOwnRecord()
    {
        var product = await Product(); var buyer = await Buyer(product); var order = await Checkout(product, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, product.Price);
        var shipper = new User { Role = UserRole.Shipper, Email = "delivered-return@example.test" }; await Users.CreateAsync(shipper);
        await ShippingService.CreateShippingAsync(order.Id, shipper.Id, ShippingMethod.Standard, null);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 5 }, shipper.Id, UserRole.Shipper);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 1, TrackingNumber = "fixture" }, shipper.Id, UserRole.Shipper);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 2 }, shipper.Id, UserRole.Shipper);
        var returns = new ReturnService(new LifecycleStore(Context), Orders, Payments, Shippings, Users, Transactions, History);
        await returns.RequestAsync(order.Id, buyer.User.Id, "Fixture buyer request");
        await returns.RequestAsync(order.Id, buyer.User.Id, "Fixture repeated request");
        Assert.That(await returns.ListAsync(), Has.Count.EqualTo(1));
        Assert.That((await returns.OwnAsync(order.Id, buyer.User.Id))!.State, Is.EqualTo("Requested"));
        Assert.That(await returns.OwnAsync(order.Id, shipper.Id), Is.Null);
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.ReturnRequested));
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.Zero);
    }
}
