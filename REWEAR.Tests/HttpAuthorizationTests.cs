using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using REWEAR.API.Controllers;
using REWEAR.Application.Interfaces;
using REWEAR.Application.Services;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Payments;
using REWEAR.Infrastructure.Repositories;
using REWEAR.Infrastructure.Services;
namespace REWEAR.Tests;
public partial class CommerceTests
{
    [Test]
    public async Task HttpAuthorizationRejectsAnonymousMemberAndShipperOnOperations()
    {
        var f = await SourceFixture();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = Array.Empty<string>() });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('t', 64)));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new()
        { ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidateIssuer = true, ValidIssuer = "isolated-tests", ValidateAudience = true, ValidAudience = "isolated-tests", ValidateLifetime = true });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("AdminOnly", p => p.RequireRole("Admin")); o.AddPolicy("StaffOrAdmin", p => p.RequireRole("Admin", "Staff"));
            o.AddPolicy("ShippingOrAdmin", p => p.RequireRole("Admin", "Staff", "Shipper"));
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(OperationsController).Assembly);
        builder.Services.AddSingleton<IOperationsQueries>(new OperationsQueries(Context, OrderService, Payments, Shippings, History));
        builder.Services.AddSingleton(f.Ops); builder.Services.AddSingleton(f.Sourcing);
        builder.Services.AddSingleton<ICloudinaryService>(new TestCloudinary()); builder.Services.AddSingleton<IUserRepository>(Users);
        builder.Services.AddSingleton<IOrderService>(OrderService); builder.Services.AddSingleton<IPaymentService>(PaymentService);
        builder.Services.AddSingleton<IShippingService>(ShippingService); builder.Services.AddSingleton<IPaymentRepository>(Payments);
        builder.Services.AddSingleton(new PayOsSettings { ChecksumKey = "isolated-webhook-test-key" });
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        void Role(string role)
        {
            var token = new JwtSecurityToken("isolated-tests", "isolated-tests", new[] { new Claim(ClaimTypes.NameIdentifier, f.Staff.Id), new Claim(ClaimTypes.Role, role) },
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(key, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        Assert.That((await client.GetAsync("/api/operations/inventory")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        foreach (var role in new[] { "Member", "Shipper" })
        {
            Role(role);
            Assert.That((await client.GetAsync("/api/operations/inventory")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            using var productForm = new MultipartFormDataContent(); productForm.Add(new StringContent("Unauthorized fixture"), "Title");
            Assert.That((await client.PostAsync("/api/Products", productForm)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }
        Role("Staff"); Assert.That((await client.GetAsync("/api/operations/inventory")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/api/operations/metrics")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Role("Admin"); Assert.That((await client.GetAsync("/api/operations/metrics")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/api/operations/orders/not-an-objectid")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.GetAsync("/api/operations/projects/not-an-objectid")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.GetAsync("/api/operations/projects/111111111111111111111111")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        client.DefaultRequestHeaders.Authorization = null;
        Assert.That((await client.PostAsync("/api/payments/webhook", new StringContent("{\"data\":{},\"signature\":\"tampered\"}", Encoding.UTF8, "application/json"))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await app.StopAsync();
    }
}
