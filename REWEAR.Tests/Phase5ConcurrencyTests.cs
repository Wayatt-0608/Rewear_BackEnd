using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using NUnit.Framework;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
namespace REWEAR.Tests;
public partial class CommerceTests
{
    internal static string SignedWebhook(Payment payment, string reference, decimal amount)
    {
        var data = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["amount"] = amount, ["code"] = "00", ["currency"] = "VND", ["orderCode"] = payment.GatewayOrderCode, ["paymentLinkId"] = payment.PaymentLinkId!, ["reference"] = reference };
        var canonical = string.Join("&", data.Select(pair => pair.Key + "=" + Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture)));
        return JsonSerializer.Serialize(new { data, signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(HttpChecksum), Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant() });
    }
    [Test]
    public async Task FiftyHttpCheckoutsReserveOneUniqueProductAndSignedWebhookRepeatsOnce()
    {
        var product = await Product(); var buyers = new List<(User User, Address Address)>();
        for (int i = 0; i < 50; i++) buyers.Add(await Buyer(product));
        await using var host = await StartHttpAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = buyers.Select(async buyer =>
        {
            using var client = host.Client(buyer.User); await start.Task;
            var response = await client.PostAsJsonAsync("/api/orders/checkout", new { shippingAddressId = buyer.Address.Id, idempotencyKey = "phase5-" + buyer.User.Id });
            return (buyer.User.Id, response.StatusCode, Json: await response.Content.ReadAsStringAsync());
        }).ToArray();
        start.SetResult(); var outcomes = await Task.WhenAll(jobs);
        Assert.That(outcomes.Count(r => r.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
        Assert.That(outcomes.Where(r => r.StatusCode != HttpStatusCode.OK).All(r => r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict && r.Json.Contains("message")), Is.True);
        var reservations = await Context.Collection<StockReservation>("StockReservations").Find(_ => true).ToListAsync();
        Assert.That(reservations, Has.Count.EqualTo(1)); Assert.That(reservations.Single().State, Is.EqualTo("Reserved"));
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.Zero);
        var order = (await Orders.GetByStatusAsync(OrderStatus.AwaitingPayment)).Single();
        var payment = (await Payments.GetLatestByOrderIdAsync(order.Id))!;
        Assert.That((await Payments.GetByOrderIdAsync(order.Id)), Has.Count.EqualTo(1));
        using var anonymous = host.Client(); string body = SignedWebhook(payment, "phase5-same-bank-reference", product.Price);
        var callbacks = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => anonymous.PostAsync("/api/payments/webhook", new StringContent(body, Encoding.UTF8, "application/json"))));
        Assert.That(callbacks.All(r => r.StatusCode == HttpStatusCode.OK), Is.True);
        payment = (await Payments.GetLatestByOrderIdAsync(order.Id))!;
        Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Paid)); Assert.That(payment.Receipts, Has.Count.EqualTo(1)); Assert.That(payment.ReceivedAmount, Is.EqualTo(299000));
        Assert.That((await History.GetByOrderIdAsync(order.Id)).Count(h => h.ToStatus == OrderStatus.Confirmed), Is.EqualTo(1));
        var admin = new User { Role = UserRole.Admin, Email = "phase5-admin@example.test" }; var shipper = new User { Role = UserRole.Shipper, Email = "phase5-shipper@example.test" }; await Users.CreateAsync(admin); await Users.CreateAsync(shipper);
        using var operations = host.Client(admin);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => operations.PostAsync($"/api/shipping/{order.Id}?shipperId={shipper.Id}", null)));
        Assert.That(await Context.Shippings.CountDocumentsAsync(s => s.OrderId == order.Id), Is.EqualTo(1));
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.Zero);
        TestContext.Progress.WriteLine("EVIDENCE: 50 HTTP checkout requests -> 1 winner, 49 clear rejections, 1 reservation, stock 0; 20 signed duplicate webhooks -> 1 receipt/sale; 10 assignment retries -> 1 shipment.");
    }
}
