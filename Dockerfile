# Rewear Backend - Dockerfile cho Render
# Build từ root repo (vì Render Root Directory = ".").
# Cấu trúc repo:
#   /
#   ├── REWEAR.sln
#   ├── REWEAR.API/
#   ├── REWEAR.Application/
#   ├── REWEAR.Domain/
#   └── REWEAR.Infrastructure/

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution và project files trước để tận dụng Docker cache.
COPY REWEAR.sln ./
COPY REWEAR.API/REWEAR.API.csproj               ./REWEAR.API/
COPY REWEAR.Application/REWEAR.Application.csproj ./REWEAR.Application/
COPY REWEAR.Domain/REWEAR.Domain.csproj          ./REWEAR.Domain/
COPY REWEAR.Infrastructure/REWEAR.Infrastructure.csproj ./REWEAR.Infrastructure/

# Restore dependencies.
RUN dotnet restore REWEAR.sln

# Copy toàn bộ source code.
COPY . .

# Publish project API ra /app/publish.
RUN dotnet publish REWEAR.API/REWEAR.API.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime - ASP.NET Core Linux nhỏ gọn.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render mặc định PORT=10000. Dùng $PORT để linh hoạt.
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}
EXPOSE 8080 10000

# Production environment.
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "REWEAR.API.dll"]