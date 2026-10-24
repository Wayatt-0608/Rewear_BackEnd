using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using REWEAR.API.Controllers;
using REWEAR.API.Middleware;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Application.Services;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Payments;
using REWEAR.Infrastructure.Repositories;
using REWEAR.Infrastructure.Services;

namespace REWEAR.Tests;
public partial class CommerceTests
{
    internal const string HttpChecksum = "phase5-isolated-checksum-not-a-merchant-key";
    private async Task<Phase5HttpHost> StartHttpAsync(ICloudinaryService? cloudinary = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = Array.Empty<string>() });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var jwt = new JwtSettings { SecretKey = new string('h', 64), Issuer = "phase5-tests", Audience = "phase5-tests" };
        builder.Services.AddSingleton(jwt);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new()
        { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)), ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience, ValidateLifetime = true, ClockSkew = TimeSpan.Zero });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("AdminOnly", p => p.RequireRole("Admin")); o.AddPolicy("StaffOrAdmin", p => p.RequireRole("Admin", "Staff")); o.AddPolicy("ShippingOrAdmin", p => p.RequireRole("Admin", "Staff", "Shipper"));
        });
        builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("auth", limiter => { limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1); }));
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        builder.Services.AddControllers().AddApplicationPart(typeof(OperationsController).Assembly);
        builder.Services.AddSingleton(Context);
        builder.Services.AddSingleton<ICommerceUnitOfWork>(Transactions);
        builder.Services.AddScoped<ILifecycleStore, LifecycleStore>();
        builder.Services.AddSingleton<IUserRepository>(Users); builder.Services.AddSingleton<IAddressRepository>(Addresses);
        builder.Services.AddSingleton<ICartRepository>(Carts); builder.Services.AddSingleton<IProductRepository>(Products);
        builder.Services.AddSingleton<IOrderRepository>(Orders); builder.Services.AddSingleton<IPaymentRepository>(Payments);
        builder.Services.AddSingleton<IOrderStatusHistoryRepository>(History); builder.Services.AddSingleton<IShippingRepository>(Shippings);
        builder.Services.AddScoped<IBrandRepository, BrandRepository>(); builder.Services.AddScoped<ICategoryRepository, CategoryRepository>(); builder.Services.AddScoped<ISourcingRepository, SourcingRepository>();
        builder.Services.AddSingleton<IPayOsService>(Gateway); builder.Services.AddSingleton<IPaymentGatewayConfig>(new TestGatewayConfig());
        builder.Services.AddSingleton(new PayOsSettings { ChecksumKey = HttpChecksum, AppBaseUrl = "https://example.test" });
        builder.Services.AddSingleton(new CommerceOptions()); builder.Services.AddSingleton<IEmailService>(new TestEmail()); builder.Services.AddSingleton<ICloudinaryService>(cloudinary ?? new TestCloudinary());
        builder.Services.AddScoped<IAuthService, AuthService>(); builder.Services.AddScoped<ICartService, CartService>();
        builder.Services.AddScoped<IOrderService, OrderService>(); builder.Services.AddScoped<IPaymentService, PaymentService>(); builder.Services.AddScoped<IShippingService, ShippingService>();
        builder.Services.AddScoped<IVoucherRepository, VoucherRepository>(); builder.Services.AddScoped<IUserVoucherRepository, UserVoucherRepository>();
        builder.Services.AddScoped<IVoucherService, VoucherService>(); builder.Services.AddScoped<IUserVoucherService, UserVoucherService>();
        builder.Services.AddScoped<ISourcingService, SourcingService>(); builder.Services.AddScoped<CircularFashionService>(); builder.Services.AddScoped<ReturnService>();
        builder.Services.AddScoped<IProductService, ProductService>(); builder.Services.AddScoped<IBrandService, BrandService>(); builder.Services.AddScoped<ICategoryService, CategoryService>();
        builder.Services.AddScoped<IOperationsQueries, OperationsQueries>();
        builder.Services.AddScoped<IProductJourneyQuery, ProductJourneyQuery>();
        var app = builder.Build();
        app.Use(async (context, next) => { try { await next(); } catch (Exception error) { context.Response.StatusCode = 500; await Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(context.Response, error.ToString()); } });
        app.UseCors(); app.UseAuthentication(); app.UseRoleChecking(); app.UseRateLimiter(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        return new(app, jwt, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    }
}
internal sealed class Phase5HttpHost(WebApplication app, JwtSettings jwt, string address) : IAsyncDisposable
{
    public string Address => address;
    public HttpClient Client(User? user = null)
    {
        var client = new HttpClient { BaseAddress = new(address), Timeout = TimeSpan.FromSeconds(60) };
        if (user != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
        return client;
    }
    public string Token(User user) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(jwt.Issuer, jwt.Audience,
        new[] { new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Role, user.Role.ToString()) }, expires: DateTime.UtcNow.AddMinutes(10),
        signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)), SecurityAlgorithms.HmacSha256)));
    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    public static async Task<T> Data<T>(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();
        NUnit.Framework.Assert.That(response.IsSuccessStatusCode, NUnit.Framework.Is.True, $"HTTP {(int)response.StatusCode} {response.RequestMessage?.RequestUri?.AbsolutePath}: {json}");
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.TryGetProperty("data", out var data)) root = data;
        return root.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
