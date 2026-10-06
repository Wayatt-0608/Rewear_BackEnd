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
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;

// Force TLS 1.2 for MongoDB connection
ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

var builder = WebApplication.CreateBuilder(args);

// Đọc cấu hình MongoDB từ appsettings.json
var mongoSettings = new MongoDbSettings
{
    ConnectionString = builder.Configuration.GetSection("MongoDbSettings:ConnectionString").Value 
                       ?? throw new InvalidOperationException("MongoDB ConnectionString not found"),
    DatabaseName = builder.Configuration.GetSection("MongoDbSettings:DatabaseName").Value 
                   ?? throw new InvalidOperationException("MongoDB DatabaseName not found")
};

// Đọc cấu hình JWT từ appsettings.json
var jwtSecret = builder.Configuration.GetSection("JwtSettings:SecretKey").Value 
                ?? throw new InvalidOperationException("JWT SecretKey not found");
var jwtIssuer = builder.Configuration.GetSection("JwtSettings:Issuer").Value ?? "RewearAPI";
var jwtAudience = builder.Configuration.GetSection("JwtSettings:Audience").Value ?? "RewearApp";

// Đăng ký MongoDbContext vào Dependency Injection
builder.Services.AddSingleton(mongoSettings);
builder.Services.AddSingleton<MongoDbContext>();

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
    BaseUrl = builder.Configuration.GetSection("PayOs:BaseUrl").Value ?? "https://api-beta.payos.vn",
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

// ====== PAYMENT (Task 8) ======
// HttpClient cho PayOsService: dùng AddHttpClient để có connection pooling
// và cơ chế tái tạo kết nối tự động khi DNS/đường dây đổi.
//
// PayOS dùng Cloudflare, và api.payos.vn có thể không resolve được DNS
// (hoặc bị chặn bởi ISP). Dùng SocketsHttpHandler với ConnectCallback
// để override DNS resolution: ép api.payos.vn → 104.21.40.122 (IP Cloudflare thật).
// Cách này KHÔNG cần sửa hosts file.
builder.Services.AddSingleton<REWEAR.Application.Interfaces.IPaymentGatewayConfig,
    REWEAR.Infrastructure.Services.PaymentGatewayConfig>();

builder.Services.AddScoped<REWEAR.Application.Interfaces.IPayOsService>(sp =>
{
    var settings = sp.GetRequiredService<PayOsSettings>();
    var logger = sp.GetRequiredService<ILogger<REWEAR.Infrastructure.Services.PayOsService>>();

    // SocketsHttpHandler cho phép override DNS resolution ở mức kết nối TCP.
    var handler = new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AllowAutoRedirect = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            // Nếu đang kết nối đến api.payos.vn → Cloudflare IP thật của PayOS.
            // Lý do: api.payos.vn có thể không resolve được trên môi trường này
            // (bị ISP/VN chặn DNS), nhưng Cloudflare vẫn nhận request nếu kết nối
            // thẳng đến IP + đúng Host header.
            if (context.DnsEndPoint.Host.Equals("api.payos.vn", StringComparison.OrdinalIgnoreCase))
            {
                var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Tcp);
                await socket.ConnectAsync(
                    new System.Net.IPEndPoint(
                        System.Net.IPAddress.Parse("104.21.40.122"), 443),
                    cancellationToken);
                // SocketsHttpHandler sẽ tự động bắt tay TLS trên stream này.
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }

            // Mọi host khác: kết nối bình thường (DNS resolution mặc định).
            var defaultSocket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);
            await defaultSocket.ConnectAsync(context.DnsEndPoint, cancellationToken);
            return new System.Net.Sockets.NetworkStream(defaultSocket, ownsSocket: true);
        }
    };

    var httpClient = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    return new REWEAR.Infrastructure.Services.PayOsService(httpClient, settings, logger);
});
builder.Services.AddScoped<REWEAR.Application.Interfaces.IPaymentService,
    REWEAR.Application.Services.PaymentService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IShippingService,
    REWEAR.Application.Services.ShippingService>();

// Background service đóng phiên thanh toán hết hạn (trả sản phẩm về kho).
builder.Services.AddHostedService<REWEAR.Infrastructure.Services.PaymentExpirationService>();

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
    options.AddPolicy("ShippingOrAdmin", policy => policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Shipper)));

    // Policy mặc định: chỉ cần đăng nhập (dùng khi endpoint chỉ gắn [Authorize])
    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});
// ====== END AUTHORIZATION POLICIES ======
// ====== END JWT AUTHENTICATION ======

builder.Services.AddControllers();

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

app.UseAuthentication();  // JWT Authentication
app.UseRoleChecking();   // Role checking (log + chặn admin API cho non-Admin)
app.UseAuthorization();  // Authorization Policies
app.MapControllers();

// Health check endpoint cho Fly.io proxy (khong yeu cau JWT).
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }))
   .AllowAnonymous();

// ========================================
// DEBUG ENDPOINT - JWT (tạm thời, chỉ để debug)
// ========================================
app.MapGet("/debug-jwt", (HttpContext ctx) =>
{
    var authHeader = ctx.Request.Headers["Authorization"].ToString();
    var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authHeader.Substring(7).Trim()
        : authHeader.Trim();

    string tokenInfo = "no-token-provided";
    if (!string.IsNullOrEmpty(token))
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        try
        {
            var jwt = handler.ReadJwtToken(token);
            tokenInfo = $"valid-jwt: sub={jwt.Subject}, iat={new DateTimeOffset(jwt.IssuedAt):yyyy-MM-dd HH:mm:ss}, exp={new DateTimeOffset(jwt.ValidTo):yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception ex)
        {
            tokenInfo = $"invalid-jwt: {ex.Message}";
        }
    }

    // Hash MD5 của SecretKey server để so sánh (không lộ key thật)
    using var md5 = System.Security.Cryptography.MD5.Create();
    var secretHash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(jwtSecret)))
        .Replace("-", "").ToLowerInvariant();

    return Results.Ok(new
    {
        serverSecretHash = secretHash,
        serverSecretLength = jwtSecret.Length,
        serverSecretStart = jwtSecret.Substring(0, Math.Min(15, jwtSecret.Length)) + "...",
        jwtIssuer = jwtIssuer,
        jwtAudience = jwtAudience,
        tokenInfo = tokenInfo
    });
}).AllowAnonymous();

app.Run();
