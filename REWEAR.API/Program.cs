using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using REWEAR.API.Filters;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Cloudinary;
using REWEAR.Infrastructure.Payments;
using REWEAR.Infrastructure.Persistence;
using REWEAR.Infrastructure.Repositories;
using REWEAR.API.Middleware;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;

// TLS certificates are verified by the platform. Never disable validation.
var builder = WebApplication.CreateBuilder(args);
// Allow the deployed REWEAR frontend to call this API from the browser.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        (context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + context.Request.Path,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});


// Đọc cấu hình MongoDB từ appsettings.json
var mongoSettings = new MongoDbSettings
{
    ConnectionString = builder.Configuration.GetSection("MongoDbSettings:ConnectionString").Value 
                       ?? throw new InvalidOperationException("MongoDB ConnectionString not found"),
    DatabaseName = builder.Configuration.GetSection("MongoDbSettings:DatabaseName").Value 
                   ?? throw new InvalidOperationException("MongoDB DatabaseName not found")
};

// Explicit maintenance command; normal API startup never changes legacy documents.
if (args.Contains("--delivery-migration-check") || args.Contains("--delivery-migration-apply"))
{
    var report = await new DeliverySchemaMigration(mongoSettings).RunAsync(args.Contains("--delivery-migration-apply"));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
    return;
}

if (args.Contains("--commerce-migration-check") || args.Contains("--commerce-migration-apply"))
{
    var report = await new CommerceSchemaMigration(mongoSettings).RunAsync(args.Contains("--commerce-migration-apply"));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
    return;
}

// Đọc cấu hình JWT từ appsettings.json
var jwtSecret = builder.Configuration.GetSection("JwtSettings:SecretKey").Value 
                ?? throw new InvalidOperationException("JWT SecretKey not found");
if (Encoding.UTF8.GetByteCount(jwtSecret) < 32) throw new InvalidOperationException("JwtSettings:SecretKey cần ít nhất 32 byte.");
var jwtIssuer = builder.Configuration.GetSection("JwtSettings:Issuer").Value ?? "RewearAPI";
var jwtAudience = builder.Configuration.GetSection("JwtSettings:Audience").Value ?? "RewearApp";

// Đăng ký JwtSettings vào DI để AuthService (Application layer) sử dụng
var jwtSettings = new REWEAR.Application.DTOs.JwtSettings
{
    SecretKey = jwtSecret,
    Issuer = jwtIssuer,
    Audience = jwtAudience,
    ExpiryHours = 24
};
builder.Services.AddSingleton(jwtSettings);

// Đăng ký MongoDbContext vào Dependency Injection
builder.Services.AddSingleton(mongoSettings);
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICommerceUnitOfWork, MongoCommerceUnitOfWork>();
var reservationMinutes = builder.Configuration.GetValue("Commerce:ReservationMinutes", 15);
if (reservationMinutes is < 2 or > 60) throw new InvalidOperationException("Commerce:ReservationMinutes phải từ 2 đến 60 phút.");
builder.Services.AddSingleton(new REWEAR.Application.DTOs.CommerceOptions { ReservationMinutes = reservationMinutes });

// Đọc cấu hình Cloudinary từ appsettings.json
var cloudinarySettings = new CloudinarySettings
{
    CloudName = builder.Configuration.GetSection("Cloudinary:CloudName").Value
               ?? throw new InvalidOperationException("Cloudinary CloudName not found"),
    ApiKey = builder.Configuration.GetSection("Cloudinary:ApiKey").Value
            ?? throw new InvalidOperationException("Cloudinary ApiKey not found"),
    ApiSecret = builder.Configuration.GetSection("Cloudinary:ApiSecret").Value
              ?? throw new InvalidOperationException("Cloudinary ApiSecret not found"),
    Folder = builder.Configuration.GetSection("Cloudinary:Folder").Value ?? "rewear"
};
builder.Services.AddSingleton(cloudinarySettings);

// Đọc cấu hình PayOS từ appsettings.json
var payOsSettings = new PayOsSettings
{
    ClientId = builder.Configuration.GetSection("PayOs:ClientId").Value ?? "",
    ApiKey = builder.Configuration.GetSection("PayOs:ApiKey").Value ?? "",
    ChecksumKey = builder.Configuration.GetSection("PayOs:ChecksumKey").Value ?? "",
    // Mặc định dùng PRODUCTION URL (api-merchant.payos.vn) - endpoint mới chính thức của PayOS.
    // Sandbox (api-beta.payos.vn) hay bị DNS issue.
    BaseUrl = builder.Configuration.GetSection("PayOs:BaseUrl").Value ?? "https://api-merchant.payos.vn",
    AppBaseUrl = builder.Configuration.GetSection("PayOs:AppBaseUrl").Value ?? "",
    ExpirationMinutes = builder.Configuration.GetValue("PayOs:ExpirationMinutes", 15)
};
builder.Services.AddSingleton(payOsSettings);

// Đăng ký Repositories
builder.Services.AddScoped<REWEAR.Application.Interfaces.IUserRepository, UserRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IAddressRepository, AddressRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IBrandRepository, BrandRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IProductRepository, ProductRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICartRepository, CartRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IOrderRepository, OrderRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IOrderStatusHistoryRepository, OrderStatusHistoryRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IShippingRepository, ShippingRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ISourcingRepository, SourcingRepository>();
// ====== TASK 10: VOUCHER ======
builder.Services.AddScoped<REWEAR.Application.Interfaces.IVoucherRepository, VoucherRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IUserVoucherRepository, UserVoucherRepository>();

// ====== WISHLIST =====
builder.Services.AddScoped<REWEAR.Application.Interfaces.IWishlistRepository, WishlistRepository>();

builder.Services.AddScoped<REWEAR.Application.Interfaces.ILifecycleStore, LifecycleStore>();
builder.Services.AddScoped<REWEAR.Application.Services.CircularFashionService>();
builder.Services.AddScoped<REWEAR.Application.Services.ReturnService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IOperationsQueries, REWEAR.Infrastructure.Services.OperationsQueries>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IProductJourneyQuery, REWEAR.Infrastructure.Services.ProductJourneyQuery>();

// Đăng ký Services
builder.Services.AddScoped<REWEAR.Application.Interfaces.IAuthService, REWEAR.Application.Services.AuthService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IProfileService, REWEAR.Application.Services.ProfileService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IEmailService, REWEAR.Infrastructure.Services.EmailService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IBrandService, REWEAR.Application.Services.BrandService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICategoryService, REWEAR.Application.Services.CategoryService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IProductService, REWEAR.Application.Services.ProductService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICartService, REWEAR.Application.Services.CartService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IOrderService, REWEAR.Application.Services.OrderService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ISourcingService, REWEAR.Application.Services.SourcingService>();
// ====== TASK 10: VOUCHER ======
builder.Services.AddScoped<REWEAR.Application.Interfaces.IVoucherService, REWEAR.Application.Services.VoucherService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IUserVoucherService, REWEAR.Application.Services.UserVoucherService>();

// ====== WISHLIST =====
builder.Services.AddScoped<REWEAR.Application.Interfaces.IWishlistService, REWEAR.Application.Services.WishlistService>();

// ====== PAYMENT (Task 8) ======
// Use platform DNS and certificate validation; never pin changing gateway IPs.
builder.Services.AddSingleton<REWEAR.Application.Interfaces.IPaymentGatewayConfig,
    REWEAR.Infrastructure.Services.PaymentGatewayConfig>();
builder.Services.AddHttpClient<REWEAR.Application.Interfaces.IPayOsService, REWEAR.Infrastructure.Services.PayOsService>(client =>
    client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });
builder.Services.AddScoped<REWEAR.Application.Interfaces.IPaymentService,
    REWEAR.Application.Services.PaymentService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IShippingService,
    REWEAR.Application.Services.ShippingService>();

// Background service đóng phiên thanh toán hết hạn (trả sản phẩm về kho).
builder.Services.AddHostedService<REWEAR.Infrastructure.Services.PaymentExpirationService>();

// Background service tắt voucher đã hết hạn (Task 10).
builder.Services.AddHostedService<REWEAR.Infrastructure.Services.VoucherExpirationService>();

// ====== JWT AUTHENTICATION ======
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.Zero
    };
});

// ====== AUTHORIZATION POLICIES ======
// Sinh policy động cho từng permission: tên policy = tên permission (vd: "user:delete")
// Policy yêu cầu user thuộc một trong các role có quyền tương ứng.
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permission.All)
    {
        var roles = RolePermissions.GetRolesFor(permission);
        if (roles.Length == 0) continue;

        options.AddPolicy(permission, policy => policy.RequireRole(roles));
    }

    // Policy dễ nhớ cho các endpoint admin
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(UserRole.Admin)));
    options.AddPolicy("StaffOrAdmin", policy => policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Staff)));
    options.AddPolicy("ShippingOrAdmin", policy => policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Staff), nameof(UserRole.Shipper)));

    // Policy mặc định: chỉ cần đăng nhập (dùng khi endpoint chỉ gắn [Authorize])
    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});
// ====== END AUTHORIZATION POLICIES ======
// ====== END JWT AUTHENTICATION ======

builder.Services.AddControllers();

// ============================================
// CORS (Task 11) - cho phép FE local (Vite/React/Angular)
// gọi API từ Render production trong khi chưa deploy FE.
// ============================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",   // Vite (React/Vue)
                "http://localhost:3000",   // CRA / Next.js dev
                "http://localhost:4200",   // Angular dev
                "http://localhost:8080",   // Vue CLI / general
                "http://127.0.0.1:5173",
                "http://127.0.0.1:3000",
                "https://rewear-wyb0.onrender.com", // Swagger + Production FE
                "https://rewear-frontend.vercel.app", // Sẵn sàng cho FE production
                "https://rewear-frontend.netlify.app"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Cấu hình giới hạn upload (5MB)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB cho multipart
});
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
});

// ====== SWAGGER CONFIG with JWT ======
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Nạp XML comments để Swagger hiện mô tả và tên chức năng của từng endpoint.
    // File XML nằm cạnh DLL sau khi build (bật GenerateDocumentationFile trong .csproj).
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

    // Bổ sung OperationId + Summary cho endpoint nào thiếu (fallback an toàn).
    c.OperationFilter<SwaggerOperationInfoFilter>();

    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Rewear API", 
        Version = "v1",
        Description = "API for Rewear Second-hand Fashion Platform"
    });
    
    // Thêm JWT Authentication vào Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Nhập token trực tiếp (không cần thêm 'Bearer ' prefix)",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
// ====== END SWAGGER ======

var app = builder.Build();

// Mount Swagger cho CA Development va Production de co the test truc tiep tren fly.io.
// Neu muon gioi han theo IP hoac role, them middleware o day.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Rewear API v1");
    // RoutePrefix = "" => Swagger UI o root "/", nguoi dung chi can vao rewear.fly.dev
    // la thay ngay API docs khong can go them /swagger.
    c.RoutePrefix = string.Empty;
});

if (app.Environment.IsDevelopment())
{
    // Development co them developer-friendly middleware (chi tiet loi, ...)
    // Swagger da duoc mount o tren roi.
}

// CORS phải đặt TRƯỚC UseAuthentication để preflight OPTIONS không bị middleware auth chặn.
app.UseCors("AllowFrontend");

app.UseAuthentication();  // JWT Authentication
app.UseRoleChecking();   // Role checking (log + chặn admin API cho non-Admin)
app.UseRateLimiter();
app.UseAuthorization();  // Authorization Policies
app.MapControllers();

// Health check endpoint cho Fly.io proxy (khong yeu cau JWT).
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }))
   .AllowAnonymous();

// ========================================
// Diagnostic endpoints must not expose authentication or configuration material.
app.Run();
