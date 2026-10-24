using NUnit.Framework;
using REWEAR.Application.DTOs;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Repositories;

namespace REWEAR.Tests;

public partial class CommerceTests
{
    /// <summary>
    /// Tạo voucher + grant cho 1 user để dùng trong test.
    /// </summary>
    private async Task<(Voucher Voucher, UserVoucher Grant)> GrantedVoucher(
        User user, decimal value = 15m, int usageLimit = 0, int usageLimitPerUser = 1)
    {
        var voucher = await VoucherService.CreateAsync(new CreateVoucherRequest
        {
            Code = "TEST" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Name = "Fixture voucher",
            Type = VoucherType.Percentage,
            Value = value,
            StartDate = DateTime.UtcNow.AddMinutes(-5),
            ExpiryDate = DateTime.UtcNow.AddDays(30),
            UsageLimit = usageLimit,
            UsageLimitPerUser = usageLimitPerUser
        }, "admin-fixture");

        Assert.That(await VoucherService.GrantToUserAsync(voucher.Id, user.Id, "admin-fixture"), Is.True);
        var grant = (await new UserVoucherRepository(Context).GetByUserAndVoucherAsync(user.Id, voucher.Id))!;
        var stored = (await new VoucherRepository(Context).GetByIdAsync(voucher.Id))!;
        return (stored, grant);
    }

    [Test]
    public async Task VoucherIsReservedAtCheckoutAndConsumedOnlyAfterVerifiedPayment()
    {
        var product = await Product();
        var buyer = await Buyer(product);
        var (voucher, grant) = await GrantedVoucher(buyer.User);

        var result = await OrderService.CheckoutAsync(buyer.User.Id, new()
        {
            ShippingAddressId = buyer.Address.Id,
            VoucherCode = voucher.Code
        });

        Assert.That(result.Success, Is.True, result.Message);
        var order = (OrderResponse)result.Data!;

        // Discount được áp vào tổng tiền, tổng tiền không được âm.
        Assert.That(order.DiscountAmount, Is.GreaterThan(0));
        Assert.That(order.TotalAmount, Is.EqualTo(order.SubTotal - order.DiscountAmount).Within(0.01m));
        Assert.That(order.TotalAmount, Is.GreaterThanOrEqualTo(0m));
        Assert.That(order.VoucherCode, Is.EqualTo(voucher.Code));

        // Giữ chỗ: chưa dùng thật nhưng đã chiếm 1 lượt.
        var reserved = (await new UserVoucherRepository(Context).GetByUserAndVoucherAsync(buyer.User.Id, voucher.Id))!;
        Assert.That(reserved.Status, Is.EqualTo(UserVoucherStatus.Reserved));
        Assert.That(reserved.OrderId, Is.EqualTo(order.Id));
        Assert.That(reserved.UsageCount, Is.Zero);
        var storedVoucher = (await new VoucherRepository(Context).GetByIdAsync(voucher.Id))!;
        Assert.That(storedVoucher.UsedCount, Is.Zero);
        Assert.That(storedVoucher.ReservedCount, Is.EqualTo(1));

        // Thanh toán xác nhận -> voucher mới thực sự được dùng.
        var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, order.TotalAmount);

        var consumed = (await new UserVoucherRepository(Context).GetByUserAndVoucherAsync(buyer.User.Id, voucher.Id))!;
        Assert.That(consumed.Status, Is.EqualTo(UserVoucherStatus.Used));
        Assert.That(consumed.UsageCount, Is.EqualTo(1));
        var consumedVoucher = (await new VoucherRepository(Context).GetByIdAsync(voucher.Id))!;
        Assert.That(consumedVoucher.UsedCount, Is.EqualTo(1));
        Assert.That(consumedVoucher.ReservedCount, Is.Zero);
    }

    [Test]
    public async Task ExpiredUnpaidOrderReleasesVoucherReservationBackToAvailable()
    {
        var product = await Product();
        var buyer = await Buyer(product);
        var (voucher, _) = await GrantedVoucher(buyer.User);

        var withVoucher = await OrderService.CheckoutAsync(buyer.User.Id, new()
        {
            ShippingAddressId = buyer.Address.Id,
            IdempotencyKey = "voucher-expiry",
            VoucherCode = voucher.Code
        });
        Assert.That(withVoucher.Success, Is.True, withVoucher.Message);
        var order = (OrderResponse)withVoucher.Data!;

        var storedOrder = (await Orders.GetByIdAsync(order.Id))!;
        storedOrder.ReservationExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await Orders.UpdateAsync(storedOrder);
        var payment = (await Payments.GetLatestByOrderIdAsync(order.Id));
        if (payment != null)
        {
            payment.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await Payments.UpdateAsync(payment);
        }

        await PaymentService.ExpireStalePaymentsAsync();

        var released = (await new UserVoucherRepository(Context).GetByUserAndVoucherAsync(buyer.User.Id, voucher.Id))!;
        Assert.That(released.Status, Is.EqualTo(UserVoucherStatus.Available));
        Assert.That(released.OrderId, Is.Null);
        Assert.That(released.UsageCount, Is.Zero);
        var releasedVoucher = (await new VoucherRepository(Context).GetByIdAsync(voucher.Id))!;
        Assert.That(releasedVoucher.ReservedCount, Is.Zero);
        Assert.That(releasedVoucher.UsedCount, Is.Zero);
    }

    [Test]
    public async Task OneVoucherGrantCannotBeReservedByTwoOrdersOfTheSameUser()
    {
        var firstProduct = await Product();
        var secondProduct = await Product();
        var buyer = await Buyer(firstProduct);
        var cart = (await Carts.GetByUserIdAsync(buyer.User.Id))!;
        cart.Items.Add(new CartItem { ProductId = secondProduct.Id, IsSelected = true, PriceSnapshot = secondProduct.Price });
        await Carts.UpdateAsync(cart);
        var (voucher, _) = await GrantedVoucher(buyer.User);

        var firstCheckout = await OrderService.CheckoutAsync(buyer.User.Id, new()
        {
            ShippingAddressId = buyer.Address.Id,
            IdempotencyKey = "voucher-first",
            VoucherCode = voucher.Code
        });
        Assert.That(firstCheckout.Success, Is.True, firstCheckout.Message);

        // Cùng 1 lượt voucher đã giữ chỗ cho đơn đầu -> đơn thứ hai của
        // cùng user phải bị chặn, không được giảm giá lần nữa.
        var secondCheckout = await OrderService.CheckoutAsync(buyer.User.Id, new()
        {
            ShippingAddressId = buyer.Address.Id,
            IdempotencyKey = "voucher-second",
            VoucherCode = voucher.Code
        });
        Assert.That(secondCheckout.Success, Is.False);
        Assert.That(secondCheckout.ErrorCode, Is.EqualTo(ApiErrorCode.Validation));

        var grant = (await new UserVoucherRepository(Context).GetByUserAndVoucherAsync(buyer.User.Id, voucher.Id))!;
        Assert.That(grant.Status, Is.EqualTo(UserVoucherStatus.Reserved));
        Assert.That(grant.OrderId, Is.EqualTo(((OrderResponse)firstCheckout.Data!).Id));
        var storedVoucher = (await new VoucherRepository(Context).GetByIdAsync(voucher.Id))!;
        Assert.That(storedVoucher.ReservedCount, Is.EqualTo(1));
        Assert.That(storedVoucher.UsedCount, Is.Zero);
    }

    [Test]
    public async Task VoucherApplyRejectsForeignUserAndInvalidOrderTotal()
    {
        var product = await Product();
        var owner = await Buyer(product);
        var stranger = await Buyer(product);
        var (voucher, _) = await GrantedVoucher(owner.User);

        var foreign = await VoucherService.ApplyVoucherAsync(stranger.User.Id, new()
        {
            Code = voucher.Code,
            OrderTotal = product.Price
        });
        Assert.That(foreign.Success, Is.False);
        Assert.That(foreign.ErrorCode, Is.EqualTo(ApiErrorCode.Forbidden));

        var invalidTotal = await VoucherService.ApplyVoucherAsync(owner.User.Id, new()
        {
            Code = voucher.Code,
            OrderTotal = 0
        });
        Assert.That(invalidTotal.Success, Is.False);
    }

    [Test]
    public async Task VoucherDiscountIsClampedAndNeverMakesTotalNegative()
    {
        var product = await Product();
        var buyer = await Buyer(product);
        // Fixed amount lớn hơn giá đơn -> discount bị clamp về tổng đơn.
        var voucher = await VoucherService.CreateAsync(new CreateVoucherRequest
        {
            Code = "BIG" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Name = "Fixture big voucher",
            Type = VoucherType.FixedAmount,
            Value = 10_000_000m,
            StartDate = DateTime.UtcNow.AddMinutes(-5),
            ExpiryDate = DateTime.UtcNow.AddDays(30),
            UsageLimitPerUser = 1
        }, "admin-fixture");
        await VoucherService.GrantToUserAsync(voucher.Id, buyer.User.Id, "admin-fixture");

        var result = await OrderService.CheckoutAsync(buyer.User.Id, new()
        {
            ShippingAddressId = buyer.Address.Id,
            VoucherCode = voucher.Code
        });

        Assert.That(result.Success, Is.True, result.Message);
        var order = (OrderResponse)result.Data!;
        Assert.That(order.DiscountAmount, Is.EqualTo(order.SubTotal));
        Assert.That(order.TotalAmount, Is.Zero);
    }
}
