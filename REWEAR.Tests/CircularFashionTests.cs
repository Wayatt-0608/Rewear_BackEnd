using NUnit.Framework;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Application.Services;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Repositories;

namespace REWEAR.Tests;

public partial class CommerceTests
{
    private async Task<(CircularFashionService Ops, SourcingService Sourcing, User Staff, SourcingResponse Source, Brand Brand, Category Category)> SourceFixture(bool pay = true)
    {
        var store = new LifecycleStore(Context); var sourceRepo = new SourcingRepository(Context);
        var brandRepo = new BrandRepository(Context); var categoryRepo = new CategoryRepository(Context);
        var staff = new User { Role = UserRole.Staff, FullName = "Fixture operator", Email = "operator@example.test" }; await Users.CreateAsync(staff);
        var member = new User { FullName = "Fixture sender", Email = "sender@example.test" }; await Users.CreateAsync(member);
        var brand = new Brand { Name = "Fixture brand" }; await brandRepo.CreateAsync(brand);
        var category = new Category { Name = "Fixture category" }; await categoryRepo.CreateAsync(category);
        var ops = new CircularFashionService(store, sourceRepo, Products, brandRepo, categoryRepo, Users, Transactions);
        var sourceService = new SourcingService(sourceRepo, Users, brandRepo, categoryRepo, Products, new TestCloudinary(), Transactions, ops, store);
        var source = await sourceService.CreateAsync(member.Id, new() { Title = "Fixture Jeans", BrandId = brand.Id, CategoryId = category.Id,
            CustomerExpectedPrice = 120000, DeclaredCondition = ProductCondition.Good, ImageUrls = new() { "https://images.example.test/jeans.jpg" } });
        await sourceService.StartReviewAsync(source.Id);
        var offered = await sourceService.OfferPriceAsync(source.Id, new() { OfferedPrice = 100000 }, staff.Id);
        await sourceService.AcceptOfferAsync(source.Id, member.Id, offered!.OfferVersion);
        await sourceService.MarkReceivedAsync(source.Id, staff.Id);
        if (pay)
        {
            await sourceService.InspectAsync(source.Id, new() { InspectedCondition = ProductCondition.Good, AdminNote = "Fixture inspection passed" }, staff.Id);
            await ops.ConfirmPayoutAsync(source.Id, new() { Amount = 100000, Reference = "fixture-payout" }, staff.Id);
        }
        source = (await sourceService.GetByIdAsync(source.Id))!;
        return (ops, sourceService, staff, source, brand, category);
    }

    [Test]
    public async Task JeansToToteToPaidDeliveryUsesPersistedLifecycleAndCost()
    {
        var f = await SourceFixture();
        var project = await f.Ops.CreateProjectAsync(new() { Title = "Fixture Tote", Concept = "Jeans to Tote", InventoryIds = new() { f.Source.Id }, ResponsibleStaffId = f.Staff.Id }, f.Staff.Id);
        await f.Ops.UpdateProjectAsync(project.Id, new() { State = "InProgress", Note = "Start work" }, f.Staff.Id);
        await f.Ops.AddCostAsync(project.Id, new() { IdempotencyKey = "material", Kind = "Material", Amount = 20000, Note = "Fixture extra material" }, f.Staff.Id);
        await f.Ops.AddCostAsync(project.Id, new() { IdempotencyKey = "labor", Kind = "Labor", Amount = 50000, Note = "Fixture labor" }, f.Staff.Id);
        await f.Ops.UpdateProjectAsync(project.Id, new() { State = "QualityCheck", AfterImages = new() { "https://images.example.test/tote.jpg" } }, f.Staff.Id);
        await f.Ops.InspectProjectAsync(project.Id, new() { Passed = true, Note = "Fixture QC passed" }, f.Staff.Id);
        var product = await f.Ops.PublishProjectAsync(project.Id, new() { Title = "Fixture Tote", BrandId = f.Brand.Id, CategoryId = f.Category.Id, SellingPrice = 299000 }, f.Staff.Id);
        Assert.That(product.ProductType, Is.EqualTo("Upcycled")); Assert.That(product.AcquisitionCost, Is.EqualTo(100000)); Assert.That(product.ProductionCost, Is.EqualTo(70000));
        Assert.That(product.SourceInventoryIds, Is.EqualTo(new[] { f.Source.Id }));
        var buyer = await Buyer(product); var order = await Checkout(product, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, 299000);
        var shipper = new User { Role = UserRole.Shipper, FullName = "Fixture delivery", Email = "delivery@example.test" }; await Users.CreateAsync(shipper);
        await ShippingService.CreateShippingAsync(order.Id, shipper.Id, ShippingMethod.Standard, null);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 5 }, shipper.Id, UserRole.Shipper);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 1, TrackingNumber = "fixture-tracking" }, shipper.Id, UserRole.Shipper);
        await ShippingService.UpdateShippingStatusAsync(order.Id, new() { Status = 2 }, shipper.Id, UserRole.Shipper);
        Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.Delivered));
        var inventory = await f.Ops.InventoryAsync();
        Assert.That(inventory.Single(i => i.Id == f.Source.Id).State, Is.EqualTo("Consumed"));
        Assert.That(inventory.Single(i => i.Id == product.Id).State, Is.EqualTo("Sold"));
        Assert.That((await f.Ops.PayoutsAsync()).Single().Amount, Is.EqualTo(100000));
        var queries = new REWEAR.Infrastructure.Services.OperationsQueries(Context, OrderService, Payments, Shippings, History);
        var metrics = await queries.MetricsAsync(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));
        Assert.That(metrics.ReceivedRevenue, Is.EqualTo(299000));
        Assert.That(metrics.PurchasePayouts, Is.EqualTo(100000));
        Assert.That(metrics.ProductionSpending, Is.EqualTo(70000));
        Assert.That(metrics.EstimatedGrossProfit, Is.EqualTo(129000));
        Assert.That(metrics.Orders.Single(o => o.State == "Delivered").Count, Is.EqualTo(1));
        Assert.That((await queries.OrdersAsync(new() { Search = order.OrderCode })).Total, Is.EqualTo(1));
        Assert.That((await queries.OrderAsync(order.Id))!.Payment!.Status, Is.EqualTo("Paid"));
    }

    [Test]
    public async Task RevisedInspectionOfferNeedsFreshCustomerAcceptance()
    {
        var f = await SourceFixture(false);
        var revised = await f.Sourcing.InspectAsync(f.Source.Id, new() { InspectedCondition = ProductCondition.Fair, RevisedPrice = 80000, AdminNote = "Fixture difference" }, f.Staff.Id);
        Assert.That(revised!.RequiresRevisedAcceptance, Is.True);
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Ops.ConfirmPayoutAsync(f.Source.Id, new() { Amount = 80000, Reference = "fixture" }, f.Staff.Id));
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Sourcing.AcceptOfferAsync(f.Source.Id, f.Source.UserId, 1));
        await f.Sourcing.AcceptOfferAsync(f.Source.Id, f.Source.UserId, revised.OfferVersion);
        var payout = await f.Ops.ConfirmPayoutAsync(f.Source.Id, new() { Amount = 80000, Reference = "fixture-revised" }, f.Staff.Id);
        Assert.That(payout.Amount, Is.EqualTo(80000));
    }

    [Test]
    public async Task PayoutAndMaterialAllocationAreNotDuplicated()
    {
        var f = await SourceFixture();
        await f.Ops.ConfirmPayoutAsync(f.Source.Id, new() { Amount = 100000, Reference = "fixture-payout" }, f.Staff.Id);
        Assert.That(await f.Ops.PayoutsAsync(), Has.Count.EqualTo(1));
        async Task<bool> Allocate()
        {
            try { await f.Ops.CreateProjectAsync(new() { Title = "Fixture", Concept = "Fixture", ResponsibleStaffId = f.Staff.Id, InventoryIds = new() { f.Source.Id } }, f.Staff.Id); return true; }
            catch (InvalidOperationException) { return false; }
        }
        Assert.That((await Task.WhenAll(Allocate(), Allocate())).Count(v => v), Is.EqualTo(1));
        Assert.That(await f.Ops.ProjectsAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FailedQcBlocksPublicationAndSupportsRework()
    {
        var f = await SourceFixture();
        var project = await f.Ops.CreateProjectAsync(new() { Title = "Fixture", Concept = "Fixture", ResponsibleStaffId = f.Staff.Id, InventoryIds = new() { f.Source.Id } }, f.Staff.Id);
        await f.Ops.UpdateProjectAsync(project.Id, new() { State = "InProgress" }, f.Staff.Id);
        await f.Ops.UpdateProjectAsync(project.Id, new() { State = "QualityCheck", AfterImages = new() { "https://images.example.test/result.jpg" } }, f.Staff.Id);
        await f.Ops.InspectProjectAsync(project.Id, new() { Passed = false, Note = "Fixture rework needed" }, f.Staff.Id);
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Ops.PublishProjectAsync(project.Id, new() { Title = "Fixture", SellingPrice = 299000 }, f.Staff.Id));
        Assert.That((await f.Ops.UpdateProjectAsync(project.Id, new() { State = "InProgress" }, f.Staff.Id)).State, Is.EqualTo("InProgress"));
    }

    [Test]
    public async Task UnpaidSourceCannotBecomeRetailProduct()
    {
        var f = await SourceFixture(false);
        await f.Sourcing.InspectAsync(f.Source.Id, new() { InspectedCondition = ProductCondition.Good, AdminNote = "Fixture passed" }, f.Staff.Id);
        Assert.ThrowsAsync<InvalidOperationException>(() => f.Ops.PublishResaleAsync(f.Source.Id, 299000, f.Staff.Id));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Ops.ConfirmPayoutAsync(f.Source.Id, new() { Amount = 100000, Reference = "fixture" }, f.Source.UserId));
    }
}

public sealed class TestCloudinary : ICloudinaryService
{
    public Task<ImageUploadResult> UploadImageAsync(ImageUploadRequest request) => Task.FromResult(new ImageUploadResult { Success = false });
    public Task<bool> DeleteImageAsync(string id) => Task.FromResult(false);
}
