using REWEAR.Infrastructure.Persistence;

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

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
