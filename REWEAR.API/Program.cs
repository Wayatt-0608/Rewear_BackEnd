using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Cloudinary;
using REWEAR.Infrastructure.Persistence;
using REWEAR.Infrastructure.Repositories;
using REWEAR.API.Middleware;
using System.Net;
using System.Net.Security;
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

// Đăng ký Repositories
builder.Services.AddScoped<REWEAR.Application.Interfaces.IUserRepository, UserRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IAddressRepository, AddressRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IBrandRepository, BrandRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IProductRepository, ProductRepository>();

// Đăng ký Services
builder.Services.AddScoped<REWEAR.Application.Interfaces.IAuthService, REWEAR.Application.Services.AuthService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IProfileService, REWEAR.Application.Services.ProfileService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IEmailService, REWEAR.Infrastructure.Services.EmailService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICloudinaryService, CloudinaryService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IBrandService, REWEAR.Application.Services.BrandService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.ICategoryService, REWEAR.Application.Services.CategoryService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IProductService, REWEAR.Application.Services.ProductService>();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();  // JWT Authentication
app.UseRoleChecking();   // Role checking (log + chặn admin API cho non-Admin)
app.UseAuthorization();  // Authorization Policies
app.MapControllers();

app.Run();
