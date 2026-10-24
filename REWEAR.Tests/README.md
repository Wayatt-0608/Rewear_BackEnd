# Kiểm thử commerce REWEAR

Dùng .NET 10 và một MongoDB **riêng cho kiểm thử**, local port 27029, replica set `rewear-tests`. Không dùng data folder của database đang vận hành.

```powershell
$rewearTestFolder = Join-Path $env:TEMP 'rewear-mongo-tests'
New-Item -ItemType Directory -Force $rewearTestFolder
mongod --dbpath $rewearTestFolder --port 27029 --bind_ip 127.0.0.1 --replSet rewear-tests
```

Trong terminal khác, từ thư mục backend:

```powershell
dotnet restore REWEAR.Tests/REWEAR.Tests.csproj
dotnet test REWEAR.Tests/REWEAR.Tests.csproj --no-restore
```

Fixture tự initiate replica set nếu chưa có, tạo database `rewear_tests_<guid>` và chỉ xóa database vừa tạo sau mỗi test. Không load appsettings/merchant config production. Gateway/email/upload test doubles chỉ thuộc project tests; API startup smoke test dùng cấu hình dummy tường minh, content root trống.

Nếu một backend đang chạy khóa DLL mặc định, dùng output folder riêng như hướng dẫn trong [COMMERCE_MIGRATION.md](../docs/COMMERCE_MIGRATION.md). Kết quả chốt 25/10/2026: 36/36 bài pass. Bộ test tập trung tiền/tồn/quyền và luồng Jeans → Tote → Delivered → metrics; không phải chứng nhận giao dịch merchant live hoặc toàn bộ 16 phân hệ.
