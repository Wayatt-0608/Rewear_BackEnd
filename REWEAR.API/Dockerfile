# Stage 1: Build
# Dùng tag preview để khớp với TargetFramework=net10.0 trong .csproj.
# Tag cố định (preview) thay vì :10.0 để tránh phụ thuộc image mặc định
# (mặc định có thể là Windows, không chạy được trên Fly.io).
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution và project files trước để tận dụng Docker cache.
COPY REWEAR.sln ./
COPY REWEAR.API/REWEAR.API.csproj         ./REWEAR.API/
COPY REWEAR.Application/REWEAR.Application.csproj ./REWEAR.Application/
COPY REWEAR.Domain/REWEAR.Domain.csproj   ./REWEAR.Domain/
COPY REWEAR.Infrastructure/REWEAR.Infrastructure.csproj ./REWEAR.Infrastructure/

# Restore dependencies (chỉ khi project file đổi sẽ chạy lại bước này).
RUN dotnet restore REWEAR.sln

# Copy source code còn lại rồi publish project API ra thư mục /app/publish.
COPY . .
RUN dotnet publish REWEAR.API/REWEAR.API.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime - image ASP.NET Core runtime Linux nhỏ gọn.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Fly.io bắt buộc lắng nghe trên cổng 8080.
# Render mặc định cổng 10000.
# Dùng biến $PORT (Render tự set, mặc định 10000; Fly không set thì dùng 8080).
# Bind [::] (IPv6) để proxy nội bộ kết nối được.
# Lưu ý: Kestrel ASP.NET Core mặc định bind cả IPv4 + IPv6 khi nói [::],
# nhưng nếu VPS chỉ có IPv6 (như Fly), app sẽ chỉ nghe IPv6.
ENV ASPNETCORE_URLS=http://[::]:${PORT:-8080}
EXPOSE 8080 10000

# Không dùng appsettings.Development.json trên production:
# toàn bộ secret (Mongo, JWT, PayOS, Cloudinary, Email) phải được set
# qua `fly secrets set` và đọc từ biến môi trường.
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "REWEAR.API.dll"]