using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Tests;
public partial class CommerceTests
{
    [Test, Explicit("Starts isolated local API, Vite and headless Chromium; no external gateway calls.")]
    public async Task ShippingBrowserAcceptanceAgainstRealLocalApi()
    {
        var p = await Product(); var buyer = await Buyer(p); var order = await Checkout(p, buyer.User, buyer.Address); var payment = await Session(order);
        await PaymentService.HandleWebhookSuccessAsync(order.OrderCode, payment.PaymentLinkId!, p.Price);
        var staff = await DeliveryActor(UserRole.Staff); var shipper = await DeliveryActor(UserRole.Shipper); var other = await DeliveryActor(UserRole.Shipper);
        await using var host = await StartHttpAsync();
        var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "package.json"))) folder = folder.Parent;
        Assert.That(folder, Is.Not.Null);
        object SessionFor(REWEAR.Domain.Entities.User u) => new { token = host.Token(u), role = u.Role.ToString().ToUpperInvariant(), fullName = u.FullName };
        var config = Path.Combine(Path.GetTempPath(), "rewear-delivery-browser-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { api = host.Address, orderId = order.Id, orderCode = order.OrderCode, staff = SessionFor(staff), shipper = SessionFor(shipper), emptyShipper = SessionFor(other), customer = SessionFor(buyer.User) }));
        try
        {
            var info = new ProcessStartInfo("node") { WorkingDirectory = folder!.FullName, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            info.ArgumentList.Add("tools/qa/shipping-real-api.cjs"); info.ArgumentList.Add(config);
            using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try { await process.WaitForExitAsync(deadline.Token); } catch { process.Kill(true); throw; }
            var result = await output; var errors = await error;
            Assert.That(process.ExitCode, Is.Zero, errors + result);
            TestContext.Progress.WriteLine(result);
            Assert.That((await Orders.GetByIdAsync(order.Id))!.Status, Is.EqualTo(OrderStatus.Delivered));
            Assert.That((await Shippings.GetByOrderIdAsync(order.Id))!.PickedUpAt, Is.Not.Null);
        }
        finally { File.Delete(config); }
    }
}
