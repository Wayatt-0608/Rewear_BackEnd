using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using REWEAR.Application.DTOs;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Tests;
public partial class CommerceTests
{
    private async Task<User> DeliveryActor(UserRole role)
    {
        var actor = new User { Role = role, FullName = "Delivery fixture " + role, Email = Guid.NewGuid() + "@example.test", IsVerified = true };
        await Users.CreateAsync(actor); return actor;
    }

    [Test]
    public async Task HttpDeliveryThreeRolesShareActualHistoryAndEnforceAssignment()
    {
        var product = await Product(); var buyer = await Buyer(product); var stranger = await DeliveryActor(UserRole.Member);
        var staff = await DeliveryActor(UserRole.Staff); var shipper = await DeliveryActor(UserRole.Shipper); var other = await DeliveryActor(UserRole.Shipper);
        await Context.Collection<InventoryItem>("InventoryItems").InsertOneAsync(new() { ProductId = product.Id, State = "ReadyRetail", Custody = "Warehouse" });
        await using var host = await StartHttpAsync(); using var customer = host.Client(buyer.User); using var operations = host.Client(staff); using var courier = host.Client(shipper); using var foreign = host.Client(other); using var anonymous = host.Client(); using var unrelated = host.Client(stranger);
        var checkout = await Phase5HttpHost.Data<System.Text.Json.JsonElement>(await customer.PostAsJsonAsync("/api/orders/checkout", new { shippingAddressId = buyer.Address.Id, idempotencyKey = "shipping-http" }));
        var order = checkout.GetProperty("order").Deserialize<OrderResponse>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!; var payment = (await Payments.GetLatestByOrderIdAsync(order.Id))!;
        Assert.That((await operations.PostAsync($"/api/shipping/{order.Id}?shipperId={shipper.Id}", null)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await Context.Shippings.CountDocumentsAsync(_ => true), Is.Zero);
        var signature = SignedWebhook(payment, "shipping-http-paid", order.TotalAmount);
        Assert.That((await anonymous.PostAsync("/api/payments/webhook", new StringContent(signature, System.Text.Encoding.UTF8, "application/json"))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var queue = await Phase5HttpHost.Data<ShippingQueueResponse>(await operations.GetAsync("/api/shipping/queue"));
        Assert.That(queue.Items.Single().OrderId, Is.EqualTo(order.Id));
        Assert.That((await operations.PostAsync($"/api/shipping/{order.Id}?shipperId={shipper.Id}", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var assignment = (await Shippings.GetByOrderIdAsync(order.Id))!;
        Assert.That(assignment.AssignedAt, Is.Not.Null); Assert.That(assignment.PickedUpAt, Is.Null);
        Assert.That((await anonymous.GetAsync("/api/shipping/my-orders")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await unrelated.GetAsync($"/api/orders/{order.Id}/tracking")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await foreign.GetAsync($"/api/shipping/{order.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var before = (await History.GetByOrderIdAsync(order.Id)).Count;
        Assert.That((await foreign.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 5 })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await operations.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 5 })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await customer.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 5 })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await courier.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 2 })).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await courier.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 1, trackingNumber = "fixture" })).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await History.GetByOrderIdAsync(order.Id)).Count, Is.EqualTo(before));
        Assert.That((await Shippings.GetByOrderIdAsync(order.Id))!.Status, Is.EqualTo(ShippingStatus.Pending));
        Assert.That((await foreign.GetAsync($"/api/shipping/my-orders?shipperId={shipper.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Phase5HttpHost.Data<MyOrdersResponse>(await foreign.GetAsync($"/api/shipping/my-orders?shipperId={shipper.Id}"))).Items, Is.Empty);
        var mine = await Phase5HttpHost.Data<MyOrdersResponse>(await courier.GetAsync("/api/shipping/my-orders"));
        Assert.That(mine.Items.Single().BuyerId, Is.Empty); Assert.That(mine.Items.Single().ShippingAddress, Does.Contain("Test-only"));
        Assert.That((await courier.GetAsync("/api/operations/inventory")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await operations.GetAsync("/api/shipping/all")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        foreach (int status in new[] { 5, 1, 2 })
        {
            var response = await courier.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status, trackingNumber = "fixture-delivery", note = "PRIVATE dispatch note" });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            var tracking = await Phase5HttpHost.Data<OrderTrackingResponse>(await customer.GetAsync($"/api/orders/{order.Id}/tracking"));
            Assert.That(tracking.Shipping!.Status, Is.EqualTo(((ShippingStatus)status).ToString()));
            Assert.That(tracking.Order!.PaymentStatus, Is.EqualTo("Paid")); Assert.That(tracking.Shipping.ShipperName, Is.EqualTo(shipper.FullName));
            Assert.That(tracking.DeliveryHistory.All(e => e.Note == null), Is.True);
            Assert.That(tracking.DeliveryHistory.All(e => e.OccurredAt.HasValue), Is.True);
            Assert.That(System.Text.Json.JsonSerializer.Serialize(tracking), Does.Not.Contain("PRIVATE"));
        }
        var final = (await Shippings.GetByOrderIdAsync(order.Id))!;
        Assert.That(final.AssignedAt <= final.PickedUpAt && final.PickedUpAt <= final.ShippedAt && final.ShippedAt <= final.DeliveredAt, Is.True);
        var count = (await History.GetByOrderIdAsync(order.Id)).Count;
        await courier.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 2 });
        Assert.That((await History.GetByOrderIdAsync(order.Id)).Count, Is.EqualTo(count));
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.Zero);
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.Delivered));
        Assert.That((await Context.Collection<InventoryItem>("InventoryItems").Find(i => i.ProductId == product.Id).SingleAsync()).Custody, Is.EqualTo("Buyer"));
        var moves = await Context.Collection<InventoryMovement>("InventoryMovements").Find(m => m.RelatedId == order.Id).ToListAsync();
        Assert.That(moves.Where(m => m.ToCustody != null).Select(m => m.ToCustody), Is.EqualTo(new[] { "Shipper", "Buyer" }));
        TestContext.Progress.WriteLine("EVIDENCE: real HTTP checkout/payment webhook -> Staff assignment -> assigned Shipper pickup/transit/delivery -> customer tracking; foreign ownership, skipped steps and roles denied without history changes; one custody handoff each.");
    }

    [Test]
    public async Task HttpFailedDeliveryKeepsPrivateNotesAndRedeliveryRetainsHistory()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price);
        var admin = await DeliveryActor(UserRole.Admin); var staff = await DeliveryActor(UserRole.Staff); var a = await DeliveryActor(UserRole.Shipper); var b = await DeliveryActor(UserRole.Shipper);
        await using var host = await StartHttpAsync(); using var operations = host.Client(admin); using var employee = host.Client(staff); using var first = host.Client(a); using var second = host.Client(b); using var customer = host.Client(buyer.User);
        await operations.PostAsync($"/api/shipping/{order.Id}?shipperId={a.Id}", null);
        Assert.That((await operations.PostAsync($"/api/shipping/{order.Id}?shipperId={b.Id}", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Context.Shippings.CountDocumentsAsync(s => s.OrderId == order.Id), Is.EqualTo(1));
        Assert.That((await first.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 5 })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        await second.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 5 });
        Assert.That((await operations.PostAsync($"/api/shipping/{order.Id}?shipperId={a.Id}", null)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await second.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 1, trackingNumber = "fixture-redelivery" });
        Assert.That((await second.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 3 })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await second.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status = 3, failureCode = "Other" })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var failure = new { status = 3, failureCode = "Unreachable", note = "PRIVATE recipient contact detail", attemptCount = 99 };
        Assert.That((await second.PutAsJsonAsync($"/api/shipping/{order.Id}/status", failure)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await second.PutAsJsonAsync($"/api/shipping/{order.Id}/status", failure);
        Assert.That((await Shippings.GetByOrderIdAsync(order.Id))!.AttemptCount, Is.EqualTo(1));
        var json = await (await customer.GetAsync($"/api/orders/{order.Id}/tracking")).Content.ReadAsStringAsync();
        Assert.That(json, Does.Not.Contain("PRIVATE")); Assert.That(json, Does.Not.Contain(b.Id));
        Assert.That((await employee.PostAsJsonAsync($"/api/admin/shipping/{order.Id}/reship", new { shipperId = a.Id })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await operations.PostAsJsonAsync($"/api/admin/shipping/{order.Id}/reship", new { shipperId = a.Id })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await second.GetAsync($"/api/shipping/{order.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        foreach (int status in new[] { 5, 1, 2 }) Assert.That((await first.PutAsJsonAsync($"/api/shipping/{order.Id}/status", new { status, trackingNumber = "fixture-retry" })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var history = await History.GetByOrderIdAsync(order.Id);
        Assert.That(history.Count(h => h.EventType == "DeliveryFailed"), Is.EqualTo(1));
        Assert.That(history.Count(h => h.EventType == "RedeliveryRequested"), Is.EqualTo(1));
        Assert.That(history.Count(h => h.EventType == "Reassigned"), Is.EqualTo(1));
        Assert.That((await Payments.GetLatestByOrderIdAsync(order.Id))!.ReceivedAmount, Is.EqualTo(p.Price));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.Zero);
    }

    [Test]
    public async Task LegacyShipmentDoesNotInventPickupOrHistoryDates()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price);
        var actor = await DeliveryActor(UserRole.Shipper);
        await Shippings.CreateAsync(new Shipping { OrderId = order.Id, OrderCode = order.OrderCode, ShipperId = actor.Id, Status = ShippingStatus.Pending });
        await Context.Collection<BsonDocument>("OrderStatusHistories").InsertOneAsync(new BsonDocument { { "orderId", order.Id }, { "fromStatus", "Confirmed" }, { "toStatus", "Shipping" }, { "note", "PRIVATE legacy" } });
        var old = await OrderService.GetTrackingAsync(buyer.User.Id, order.Id);
        Assert.That(old!.Shipping!.AssignedAt, Is.Null); Assert.That(old.Shipping.PickedUpAt, Is.Null);
        Assert.That(old.DeliveryHistory.Single(h => h.Code == "Shipping").OccurredAt, Is.Null);
        Assert.That((await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 1, TrackingNumber = "legacy-fixture" }, actor.Id, UserRole.Shipper)).Success, Is.True);
        Assert.That((await Shippings.GetByOrderIdAsync(order.Id))!.PickedUpAt, Is.Null);
    }
}
