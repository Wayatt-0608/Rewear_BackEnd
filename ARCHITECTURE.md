# REWEAR Backend - Architecture & Technical Stack

## Kiến Trúc (Architecture)

Dự án sử dụng **Clean Architecture** với 4 layers:

```
REWARE.API (Presentation Layer)
    ↓
REWARE.Application (Application Layer)
    ↓
REWEAR.Infrastructure (Infrastructure Layer)
    ↓
REWEAR.Domain (Domain Layer)
```

| Layer | Chức năng |
|-------|-----------|
| **REWEAR.Domain** | Chứa Entities (`User`, `Product`) - không phụ thuộc layer nào |
| **REWARE.Application** | Chứa DTOs, Services, Interfaces - business logic |
| **REWEAR.Infrastructure** | Chứa Repositories, Database context, External services |
| **REWARE.API** | API Controllers, Swagger, Entry point |

## Technical Stack

| Technology | Version | Mục đích |
|------------|---------|----------|
| **.NET** | 10.0 | Runtime |
| **ASP.NET Core** | 10.0.x | Web API Framework |
| **MongoDB** | 3.12.0 | Database (NoSQL) |
| **BCrypt.Net-Next** | 4.2.0 | Password hashing |
| **Swashbuckle.AspNetCore** | 10.2.3 | Swagger/OpenAPI Documentation |

## Cấu trúc thư mục

```
REWEAR_BackEnd/
├── REWARE.API/
│   ├── Controllers/     → AuthController
│   ├── Program.cs       → Entry point, DI configuration
│   └── appsettings.json
├── REWARE.Application/
│   ├── DTOs/            → AuthDTOs
│   ├── Interfaces/      → IAuthService, IUserRepository, IEmailService
│   └── Services/        → AuthService
├── REWEAR.Infrastructure/
│   ├── Persistence/     → MongoDbContext, MongoDbSettings
│   ├── Repositories/    → UserRepository
│   └── Services/        → EmailService
└── REWEAR.Domain/
    └── Entities/        → User, Product
```

## Đặc điểm

- Dependency Injection: Được cấu hình thủ công trong `Program.cs`
- Swagger: Đã enable với OpenAPI
- MongoDB: Sử dụng driver chính thức (`MongoDB.Driver`)
- Docker Support: Có cấu hình Docker
- User Secrets: Có `UserSecretsId` cho development
