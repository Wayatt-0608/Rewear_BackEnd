using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using REWEAR.Infrastructure.Persistence;
using REWEAR.Infrastructure.Repositories;
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

// Đăng ký Repositories
builder.Services.AddScoped<REWEAR.Application.Interfaces.IUserRepository, UserRepository>();

// Đăng ký Services
builder.Services.AddScoped<REWEAR.Application.Interfaces.IAuthService, REWEAR.Application.Services.AuthService>();
builder.Services.AddScoped<REWEAR.Application.Interfaces.IEmailService, REWEAR.Infrastructure.Services.EmailService>();

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

builder.Services.AddAuthorization();
// ====== END JWT AUTHENTICATION ======

builder.Services.AddControllers();

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
app.UseAuthorization();  // Authorization Policies
app.MapControllers();

app.Run();
