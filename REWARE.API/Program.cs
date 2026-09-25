using REWEAR.Infrastructure.Persistence;
using REWEAR.Infrastructure.Repositories;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

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

// Đăng ký MongoDbContext vào Dependency Injection
builder.Services.AddSingleton(mongoSettings);
builder.Services.AddSingleton<MongoDbContext>();

// Đăng ký Repositories
builder.Services.AddScoped<REWARE.Application.Interfaces.IUserRepository, UserRepository>();

// Đăng ký Services
builder.Services.AddScoped<REWARE.Application.Interfaces.IAuthService, REWARE.Application.Services.AuthService>();
builder.Services.AddScoped<REWARE.Application.Interfaces.IEmailService, REWEAR.Infrastructure.Services.EmailService>();

builder.Services.AddControllers();

// ====== SWAGGER CONFIG ======
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// ====== END SWAGGER ======

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
