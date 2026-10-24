using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using REWEAR.API.Controllers;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Application.Services;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Payments;
using REWEAR.Infrastructure.Persistence;
using REWEAR.Infrastructure.Repositories;

namespace REWEAR.Tests;

[TestFixture, NonParallelizable]
public partial class CommerceTests
{
    // This fixture cannot read application configuration or connect to a production database.
    private const string LocalServer = "mongodb://127.0.0.1:27029/?replicaSet=rewear-tests&serverSelectionTimeoutMS=5000";
    private MongoClient _client = null!;
    private string _database = "";
    internal MongoDbContext Context = null!;
    internal MongoCommerceUnitOfWork Transactions = null!;
    internal ProductRepository Products = null!;
    internal OrderRepository Orders = null!;
    internal PaymentRepository Payments = null!;
    internal CartRepository Carts = null!;
    internal AddressRepository Addresses = null!;
    internal UserRepository Users = null!;
    internal ShippingRepository Shippings = null!;
    internal OrderStatusHistoryRepository History = null!;
    internal OrderService OrderService = null!;
    internal PaymentService PaymentService = null!;
    internal ShippingService ShippingService = null!;
    internal TestGateway Gateway = null!;

    [OneTimeSetUp]
    public async Task InitializeReplica()
    {
        var bootstrap = new MongoClient("mongodb://127.0.0.1:27029/?directConnection=true&serverSelectionTimeoutMS=5000");
        var admin = bootstrap.GetDatabase("admin");
        try { await admin.RunCommandAsync<BsonDocument>(new BsonDocument("replSetGetStatus", 1)); }
        catch (MongoCommandException e) when (e.Code == 94)
        {
            await admin.RunCommandAsync<BsonDocument>(new BsonDocument("replSetInitiate", new BsonDocument
            { { "_id", "rewear-tests" }, { "members", new BsonArray { new BsonDocument { { "_id", 0 }, { "host", "127.0.0.1:27029" } } } } }));
        }
        for (var i = 0; i < 50; i++)
        {
            var hello = await admin.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
            if (hello.GetValue("isWritablePrimary", false).AsBoolean) return;
            await Task.Delay(200);
        }
        Assert.Fail("Isolated Mongo replica set was not ready.");
    }

    [SetUp]
    public void Setup()
    {
        _database = "rewear_tests_" + Guid.NewGuid().ToString("N");
        _client = new MongoClient(LocalServer);
        Context = new MongoDbContext(new MongoDbSettings { ConnectionString = LocalServer, DatabaseName = _database });
        Transactions = new MongoCommerceUnitOfWork(Context);
        Products = new(Context); Orders = new(Context); Payments = new(Context); Carts = new(Context);
        Addresses = new(Context); Users = new(Context); Shippings = new(Context); History = new(Context);
        ShippingService = new(Shippings, Orders, History, Users, Products, Transactions, Payments, new LifecycleStore(Context));
        OrderService = new(Orders, Carts, Products, Addresses, History, Users, Transactions, new CommerceOptions(), Payments, ShippingService);
        Gateway = new TestGateway();
        PaymentService = new(Payments, Orders, History, Products, Gateway, Transactions, Carts, Shippings, new TestGatewayConfig(), NullLogger<PaymentService>.Instance, new LifecycleStore(Context));

    }

    [TearDown]
    public async Task Cleanup()
    {
        if (!_database.StartsWith("rewear_tests_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup.");
        await _client.DropDatabaseAsync(_database);
    }

    internal async Task<(User User, Address Address)> Buyer(Product product)
    {
        var user = new User { FullName = "Fixture", Email = Guid.NewGuid() + "@example.test", IsVerified = true };
        await Users.CreateAsync(user);
        var address = new Address { UserId = user.Id, RecipientName = "Fixture", StreetAddress = "Test-only address" };
        await Addresses.CreateAsync(address);
        await Carts.CreateAsync(new Cart { UserId = user.Id, Items = new() { new CartItem { ProductId = product.Id, Quantity = 1, IsSelected = true, PriceSnapshot = product.Price } } });
        return (user, address);
    }
    internal async Task<Product> Product()
    {
        var p = new Product { Title = "Test unique item", Price = 299000, StockQuantity = 1, IsUnique = true };
        await Products.CreateAsync(p); return p;
    }
    internal async Task<OrderResponse> Checkout(Product p, User u, Address a, string? key = null)
    {
        var result = await OrderService.CheckoutAsync(u.Id, new CheckoutRequest { ShippingAddressId = a.Id, IdempotencyKey = key });
        Assert.That(result.Success, Is.True, result.Message);
        return (OrderResponse)result.Data!;
    }
    internal async Task<Payment> Session(OrderResponse order)
    {
        var result = await PaymentService.CreateSessionAsync(order.Id);
        Assert.That(result.Response.Success, Is.True, result.Response.Message);
        return (await Payments.GetLatestByOrderIdAsync(order.Id))!;
    }

    [Test]
    public async Task RegistrationAlwaysCreatesMember()
    {
        var auth = new AuthService(Users, new TestEmail(), new JwtSettings { SecretKey = new string('x', 64), Issuer = "test", Audience = "test" }, Transactions);
        foreach (var role in new[] { "Admin", "Staff", "Shipper", "1" })
        {
            var result = await auth.RegisterAsync(new RegisterRequest { FullName = "Fixture", Email = Guid.NewGuid() + "@example.test", Password = "Fixture-only-123", PasswordConfirm = "Fixture-only-123", Role = role });
            Assert.That(result.Success, Is.True);
            Assert.That(result.Role, Is.EqualTo("Member"));
            Assert.That((await Users.GetByIdAsync(result.UserId!))!.Role, Is.EqualTo(UserRole.Member));
        }
    }

    [Test]
    public void EveryCatalogWriteHasServerAuthorization()
    {
        foreach (var type in new[] { typeof(ProductsController), typeof(BrandsController), typeof(CategoriesController) })
        foreach (var method in type.GetMethods().Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(a => a.HttpMethods.Any(v => v is "POST" or "PUT" or "PATCH" or "DELETE"))))
            Assert.That(method.GetCustomAttributes<AuthorizeAttribute>().Concat(type.GetCustomAttributes<AuthorizeAttribute>()), Is.Not.Empty, type.Name + "." + method.Name);
    }

    [Test]
    public async Task OtpCannotBeUsedAfterFiveWrongAttempts()
    {
        var auth = new AuthService(Users, new TestEmail(), new JwtSettings { SecretKey = new string('x', 64), Issuer = "test", Audience = "test" }, Transactions);
        var user = new User { Email = "otp-fixture@example.test", OtpCode = "123456", OtpExpiresAt = DateTime.UtcNow.AddMinutes(5) };
        await Users.CreateAsync(user);
        for (int i = 0; i < 5; i++) Assert.That((await auth.VerifyOtpAsync(new() { Email = user.Email, OtpCode = "000000" })).Success, Is.False);
        Assert.That((await auth.VerifyOtpAsync(new() { Email = user.Email, OtpCode = "123456" })).Success, Is.False);
        Assert.That((await Users.GetByIdAsync(user.Id))!.IsVerified, Is.False);
    }

    [Test]
    public async Task UniqueCartItemCannotBeDuplicated()
    {
        var p = await Product(); var buyer = await Buyer(p); var cartService = new CartService(Carts, Products);
        Assert.That((await cartService.AddToCartAsync(buyer.User.Id, new() { ProductId = p.Id, Quantity = 2 })).Success, Is.False);
        Assert.That((await cartService.AddToCartAsync(buyer.User.Id, new() { ProductId = p.Id, Quantity = 1 })).Success, Is.True);
        Assert.That((await Carts.GetByUserIdAsync(buyer.User.Id))!.Items.Single().Quantity, Is.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentCheckoutsCannotBothReserveUniqueItem()
    {
        var p = await Product(); var a = await Buyer(p); var b = await Buyer(p);
        var outcomes = await Task.WhenAll(OrderService.CheckoutAsync(a.User.Id, new() { ShippingAddressId = a.Address.Id }), OrderService.CheckoutAsync(b.User.Id, new() { ShippingAddressId = b.Address.Id }));
        Assert.That(outcomes.Count(o => o.Success), Is.EqualTo(1));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.Zero);
        Assert.That(await Orders.CountByStatusAsync(OrderStatus.AwaitingPayment), Is.EqualTo(1));
    }

    [Test]
    public async Task DuplicateCheckoutReturnsSameOrderAndDoesNotExtendReservation()
    {
        var p = await Product(); var buyer = await Buyer(p);
        var one = await Checkout(p, buyer.User, buyer.Address, "same-request");
        var two = await Checkout(p, buyer.User, buyer.Address, "same-request");
        Assert.That(two.Id, Is.EqualTo(one.Id)); Assert.That(two.ReservationExpiresAt, Is.EqualTo(one.ReservationExpiresAt).Within(TimeSpan.FromMilliseconds(1)));
        Assert.That(one.ReservationExpiresAt!.Value - DateTime.UtcNow, Is.InRange(TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16)));
    }

    [Test]
    public async Task ForeignAddressDoesNotReserveStock()
    {
        var p = await Product(); var a = await Buyer(p); var b = await Buyer(p);
        var result = await OrderService.CheckoutAsync(a.User.Id, new() { ShippingAddressId = b.Address.Id });
        Assert.That(result.Success, Is.False); Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.EqualTo(1));
    }

    [Test]
    public async Task CheckoutWithoutPaymentRecordExpiresAndReleasesOnlyOnce()
    {
        var product = await Product(); var buyer = await Buyer(product); var response = await Checkout(product, buyer.User, buyer.Address);
        var order = (await Orders.GetByIdAsync(response.Id))!; order.ReservationExpiresAt = DateTime.UtcNow.AddSeconds(-1); await Orders.UpdateAsync(order);
        Assert.That(await PaymentService.ExpireStalePaymentsAsync(), Is.EqualTo(1));
        Assert.That(await PaymentService.ExpireStalePaymentsAsync(), Is.EqualTo(0));
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.EqualTo(1));
        Assert.That((await Carts.GetByUserIdAsync(buyer.User.Id))!.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task InitializationFailureKeepsCartAndExpiresWithoutBrowser()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address);
        Gateway.FailCreate = true;
        Assert.That((await PaymentService.CreateSessionAsync(order.Id)).Response.Success, Is.False);
        Assert.That((await Carts.GetByUserIdAsync(buyer.User.Id))!.Items, Has.Count.EqualTo(1));
        var payment = (await Payments.GetLatestByOrderIdAsync(order.Id))!; payment.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await Payments.UpdateAsync(payment);
        await PaymentService.ExpireStalePaymentsAsync(); await PaymentService.ExpireStalePaymentsAsync();
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.EqualTo(1));
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.PaymentExpired));
    }

    [Test]
    public async Task RepeatedSessionCreationUsesSingleGatewayRequestAndRemovesOnlyPurchasedCartLines()
    {
        var p = await Product(); var extra = await Product(); var buyer = await Buyer(p);
        var cart = (await Carts.GetByUserIdAsync(buyer.User.Id))!; cart.Items.Add(new CartItem { ProductId = extra.Id, IsSelected = false }); await Carts.UpdateAsync(cart);
        var order = await Checkout(p, buyer.User, buyer.Address);
        await Session(order); await Session(order);
        Assert.That(Gateway.CreateCalls, Is.EqualTo(1));
        Assert.That((await Carts.GetByUserIdAsync(buyer.User.Id))!.Items.Select(i => i.ProductId), Is.EqualTo(new[] { extra.Id }));
    }

    [Test]
    public async Task ConcurrentDuplicatePaidCallbacksCommitOneOutcome()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price, "fixture-reference")));
        Assert.That(results.All(r => r.Success), Is.True);
        Assert.That((await Products.GetByIdAsync(p.Id))!.Status, Is.EqualTo(ProductStatus.Sold));
        Assert.That((await History.GetByOrderIdAsync(order.Id)).Count(h => h.ToStatus == OrderStatus.Confirmed), Is.EqualTo(1));
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.Confirmed));
    }

    [Test]
    public async Task LatePaymentCannotTakeInventoryReservedByAnotherOrder()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        var storedOrder = (await Orders.GetByIdAsync(order.Id))!; storedOrder.ReservationExpiresAt = DateTime.UtcNow.AddSeconds(-1); await Orders.UpdateAsync(storedOrder);
        payment.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await Payments.UpdateAsync(payment); await PaymentService.ExpireStalePaymentsAsync();
        var secondBuyer = await Buyer(p); var secondOrder = await Checkout(p, secondBuyer.User, secondBuyer.Address);
        var result = await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price);
        Assert.That(result.Success, Is.True); Assert.That((await Payments.GetLatestByOrderIdAsync(order.Id))!.RequiresReconciliation, Is.True);
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.PaymentExpired));
        Assert.That((await Orders.GetByIdAsync(secondOrder.Id))!.Status, Is.EqualTo(OrderStatus.AwaitingPayment));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.Zero);
    }

    [Test]
    public async Task AmountMismatchDoesNotConfirmFulfillment()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, 1);
        Assert.That((await Payments.GetLatestByOrderIdAsync(order.Id))!.RequiresReconciliation, Is.True);
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.Not.EqualTo(OrderStatus.Confirmed));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.EqualTo(1));
    }

    [Test]
    public async Task PaidCancellationRequiresManualEvidenceAndRestocksExactlyOnce()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price);
        await PaymentService.RefundAsync(order.Id, "Fixture cancellation");
        Assert.That((await Payments.GetLatestByOrderIdAsync(order.Id))!.Status, Is.EqualTo(PaymentStatus.Paid));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.Zero);
        var result = await PaymentService.ConfirmManualRefundAsync(order.Id, "refund-evidence-fixture", p.Price, "admin-fixture");
        Assert.That(result.Success, Is.True, result.Message);
        await PaymentService.ConfirmManualRefundAsync(order.Id, "refund-evidence-fixture", p.Price, "admin-fixture");
        Assert.That((await Payments.GetLatestByOrderIdAsync(order.Id))!.Status, Is.EqualTo(PaymentStatus.Refunded));
        Assert.That((await Products.GetByIdAsync(p.Id))!.StockQuantity, Is.EqualTo(1));
    }

    [Test]
    public async Task ShippingRejectsForeignShipperAndSkippedSteps()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price);
        var shipper = new User { FullName = "Fixture shipper", Email = "shipper@example.test", Role = UserRole.Shipper }; await Users.CreateAsync(shipper);
        Assert.That((await ShippingService.CreateShippingAsync(order.Id, shipper.Id, ShippingMethod.Standard, null)).Success, Is.True);
        await ShippingService.UpdateShippingAsync(order.Id, new() { TrackingNumber = "metadata-only" });
        Assert.That((await Shippings.GetByOrderIdAsync(order.Id))!.Status, Is.EqualTo(ShippingStatus.Pending));
        Assert.That((await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 1, TrackingNumber = "fixture" }, "unassigned", UserRole.Shipper)).Success, Is.False);
        Assert.That((await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 2 }, shipper.Id, UserRole.Shipper)).Success, Is.False);
        Assert.That((await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 1, TrackingNumber = "fixture" }, shipper.Id, UserRole.Shipper)).Success, Is.False);
        Assert.That((await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 5 }, shipper.Id, UserRole.Shipper)).Success, Is.True);
        Assert.That((await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 1, TrackingNumber = "fixture" }, shipper.Id, UserRole.Shipper)).Success, Is.True);
        Assert.That((await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 2 }, shipper.Id, UserRole.Shipper)).Success, Is.True);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 2 }, shipper.Id, UserRole.Shipper);
        Assert.That((await History.GetByOrderIdAsync(order.Id)).Count(h => h.ToStatus == OrderStatus.Delivered), Is.EqualTo(1));
    }

    [Test]
    public async Task MongoTransactionRollsBackPartialWrites()
    {
        var p = await Product();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Transactions.ExecuteAsync<bool>(async () =>
        {
            var product = (await Products.GetByIdAsync(p.Id))!; product.Title = "Must roll back"; await Products.UpdateAsync(product);
            throw new InvalidOperationException("Fixture rollback");
        }));
        Assert.That((await Products.GetByIdAsync(p.Id))!.Title, Is.EqualTo(p.Title));
    }

    [Test]
    public void WebhookSignatureRejectsModifiedData()
    {
        const string testKey = "fixture-only-checksum";
        using var document = JsonDocument.Parse("{\"orderCode\":123,\"amount\":299000}");
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(testKey), Encoding.UTF8.GetBytes("amount=299000&orderCode=123")));
        Assert.That(PayOsSignature.Verify(document.RootElement, signature, testKey), Is.True);
        using var changed = JsonDocument.Parse("{\"orderCode\":123,\"amount\":1}");
        Assert.That(PayOsSignature.Verify(changed.RootElement, signature, testKey), Is.False);
        Assert.That(PayOsSignature.Verify(document.RootElement, signature, ""), Is.False);
    }
}

public sealed class TestEmail : IEmailService { public Task<bool> SendOtpEmailAsync(string email, string otp, string fullName) => Task.FromResult(true); }
public sealed class TestGatewayConfig : IPaymentGatewayConfig { public string AppBaseUrl => "https://example.test"; }
public sealed class TestGateway : IPayOsService
{
    public bool FailCreate; public int CreateCalls;
    public PayOsTransactionInfo? Transaction;
    public int LookupCalls;
    public Func<string, Task<PayOsTransactionInfo?>>? LookupOverride;
    public Task<PayOsCreateResult?> CreatePaymentRequestAsync(string code, decimal amount, string returnUrl, string cancelUrl, DateTime expiresAt, long? numericOrderCode = null)
    { CreateCalls++; return Task.FromResult(FailCreate ? null : new PayOsCreateResult { Id = "fixture-" + code, PaymentLinkId = "fixture-" + code, CheckoutUrl = "https://pay.payos.vn/fixture" }); }
    public Task<PayOsTransactionInfo?> GetTransactionAsync(string id) { LookupCalls++; return LookupOverride == null ? Task.FromResult(Transaction) : LookupOverride(id); }
    public Task<PayOsRefundResult> RefundAsync(string id, string? reason) => Task.FromResult(new PayOsRefundResult { Success = false });
    public Task<bool> ConfirmWebhookAsync(string url) => Task.FromResult(false);
}
