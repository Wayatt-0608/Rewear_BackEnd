# Migration và chạy thử luồng commerce REWEAR

Phiên bản: `20261025_commerce_v1`. Đây là migration **bổ sung index/collection metadata**, không sửa/xóa user, order, payment hay sản phẩm cũ. Không tự tạo payout, transactionId, giá vốn hoặc reservation cho dữ liệu lịch sử. Task chỉ chạy migration trong database kiểm thử GUID local.

## Trước khi nâng backend

1. Dùng MongoDB replica set hoặc sharded cluster. Transaction không hoạt động trên standalone; không có chế độ fallback ghi rời rạc.
2. Có bản sao lưu có thể phục hồi; thử phiên bản mới trên bản sao staging trước. Tạm dừng checkout/worker phiên bản cũ khi chuyển backend để tránh hai mô hình giữ hàng cùng hoạt động.
3. Đối chiếu dữ liệu cũ: sản phẩm Reserved có reservation owner thật không; đơn AwaitingPayment có deadline không; thiếu stockQuantity/PaidAt/giá vốn; email hoặc orderCode trùng; index PayOsTransactionId cũ có đúng partial filter không.
4. Sản phẩm thiếu stockQuantity đọc là 0, không phải tự có 1 món. Không giải phóng Reserved cũ chỉ dựa trên status. Không xóa stock/financial documents để migration chạy qua.

## Lệnh bảo trì

Dùng cấu hình đúng môi trường qua secret manager/environment variables; không ghi giá trị thật vào repo hoặc lệnh được chia sẻ. Từ thư mục `Rewear_BackEnd`:

```powershell
dotnet run --project REWEAR.API -- --commerce-migration-check
dotnet run --project REWEAR.API -- --commerce-migration-apply
```

`--commerce-migration-check` chỉ kiểm tra topology và đếm các nhóm dữ liệu cần rà soát. `ReservedProductsToReview` đếm mọi sản phẩm Reserved để vận hành đối chiếu ledger, không kết luận tất cả đều là lỗi. Không chạy worker/API trong chế độ command này.

`--commerce-migration-apply` tạo index tên có version và ghi một document trong `SchemaMigrations` sau khi mọi index thành công. Mongo index DDL không nằm trong một transaction: nếu index sau lỗi, các index trước có thể đã được tạo; sửa nguyên nhân sau khi kiểm tra dữ liệu rồi chạy lại. Không có drop index hay delete dữ liệu tự động. Nếu dữ liệu trùng/option index cũ khác, lệnh dừng bằng lỗi; cần rà soát riêng, không bỏ qua để coi migration đã hoàn tất.

Index bổ sung:

- Payments: gateway code unique có partial `> 0`, PaidAt, RefundedAt, lịch kiểm tra đối soát.
- Users: email unique với partial string; email cũ khác hoa/thường cần kiểm kê chuẩn hóa riêng, lệnh không tự sửa.
- Orders: deadline giữ hàng, PaidAt, unique `(userId, CheckoutKey)` khi AwaitingPayment.
- StockReservations: product/state, order; **không TTL/xóa ledger**.
- PurchasePayouts/InventoryItems: nguồn unique; inventory state/product; movements inventory/date.
- UpcyclingProjects: state; QC: project/date; ReturnRequests: order unique.

Các field/entity mới additive. Payout và raw inventory dùng cùng ID nguồn; thành phẩm upcycled dùng product ID riêng, `SourcingId = project:<projectId>` để tránh nhân đôi nguyên liệu. Product có IsUnique, Version, loại sản phẩm, nguồn, chi phí; OrderItem giữ snapshot giá vốn. Payment thêm mã gateway, receipts, cờ đối soát, số tiền hoàn thực tế, bằng chứng/actor hoàn tiền. Enum order chỉ nối thêm 7/8/9, không đổi ordinal cũ.

## Cấu hình cần có

- `MongoDbSettings__ConnectionString`, `MongoDbSettings__DatabaseName`.
- `JwtSettings__SecretKey` ít nhất 32 byte; Issuer/Audience nhất quán. Nếu endpoint debug JWT cũ từng được truy cập, đánh giá xoay khóa và vô hiệu phiên cũ khi triển khai; task không tự đổi khóa thật.
- `Commerce__ReservationMinutes`: mặc định 15, hợp lệ 2–60. Retry không kéo dài deadline order đã tạo.
- `PayOs__ClientId`, `PayOs__ApiKey`, `PayOs__ChecksumKey`, `PayOs__BaseUrl` HTTPS thuộc payOS và `PayOs__AppBaseUrl` là URL frontend thật; localhost HTTP chỉ cho phát triển. Không dùng URL gateway làm returnUrl frontend.
- Cloudinary và EmailSettings thật cho upload/OTP. Test suite không gửi tiền/email/upload bên ngoài.
- CORS hiện dùng một policy AllowFrontend với danh sách origin có sẵn trong Program; thêm đúng origin triển khai nếu khác. Không bật wildcard credential.
- Giữ worker hoạt động mỗi 60 giây hoặc bổ sung scheduler bên ngoài khi hosting có thể ngủ. Worker đối soát bù tối đa 10 giao dịch chưa chốt/vòng trong 30 ngày, tần suất mỗi bản ghi tối thiểu 5 phút; vận hành cần đối soát backlog/giao dịch cũ chủ động.

## Kiểm thử local tách biệt

Cài .NET 10 và MongoDB có hỗ trợ replica set. Chạy một mongod riêng ở `127.0.0.1:27029`, `--replSet rewear-tests`, `--dbpath` thư mục dữ liệu test riêng. Không trỏ cổng này vào database đang dùng. Fixture tự initiate replica set local nếu chưa khởi tạo, tạo tên `rewear_tests_<guid>` cho từng test, xóa đúng database do test tạo.

```powershell
dotnet restore REWEAR.Tests/REWEAR.Tests.csproj
dotnet test REWEAR.Tests/REWEAR.Tests.csproj --no-restore
```

Nếu backend người dùng đang chạy khóa DLL, dùng output riêng thay vì dừng process của người dùng:

```powershell
dotnet test REWEAR.Tests/REWEAR.Tests.csproj --no-restore -p:OutputPath=D:/EXE201/Rewear_BackEnd/.artifacts/tests/
```

`TestGateway` chỉ tồn tại trong REWEAR.Tests; không được đưa vào DI production. Các test workflow dùng Mongo thật nhưng kết quả payOS giả lập. RuntimeStartupTests mở entrypoint API với content root trống, cấu hình test tường minh và database GUID local; chỉ request health/read APIs/auth invalid, không gọi dịch vụ tiền/email/upload thật.

## Nghiệm thu staging còn cần

Chạy kịch bản Jeans → payout chứng từ thực tế → QC → Tote → merchant payOS → phân công shipper → Delivered → báo cáo. Đối chiếu số tiền/link/order/gateway code, số lượng bán, webhook trễ/lặp, khách đóng trình duyệt, khoản chuyển dư và hoàn tiền. Kiểm tra Cloudinary/SMTP và quyền người dùng thật. Hoàn toàn bộ đơn đã có cơ chế chứng từ; partial refund/return, hoàn riêng khoản dư và xử lý mất hàng cần workflow riêng. Không dùng thao tác hoàn toàn bộ đơn để tự xác nhận khoản hoàn một phần.

Nguồn đối chiếu gateway: [payOS API](https://payos.vn/docs/api/), [xác minh signature](https://payos.vn/docs/tich-hop-webhook/kiem-tra-du-lieu-voi-signature/). Dependency Pkcs 10.0.13 được đối chiếu [advisory Microsoft](https://github.com/advisories/GHSA-846f-f764-76mx); restore/build đã kiểm tra. Chưa có chứng nhận penetration test, load test hoặc merchant live.

## Delivery v2 — 25/10/2026

Sau Commerce v1, có maintenance command riêng `--delivery-migration-check` và `--delivery-migration-apply` (cùng cách cấu hình kết nối như v1). Không tự chạy khi khởi động API. Migration `20261025_delivery_v2` chỉ thêm index shipper/status/AssignedAt, order/event/date, inventory custody và CashEvents.RecordedAt, ghi marker sau khi index hoàn tất. Không sửa document cũ, không điền ngày phân công/lấy hàng hoặc số tiền.

Shipping enum giữ 0–4, thêm PickedUp=5. Chuyến mới có RequiresPickupConfirmation=true; bản cũ thiếu trường đọc false để tương thích, giữ AssignedAt/PickedUpAt null khi không có bằng chứng. Không rollback binary sang phiên bản không hiểu PickedUp khi đã có dữ liệu trạng thái này; cần kế hoạch nâng cấp đồng bộ FE/BE và backup trước khi triển khai. Migration đã kiểm thử idempotent và bảo toàn document legacy trên database test riêng; chưa chạy database thật. Xem context/shipping-acceptance-2026-10-25.md cho bằng chứng và giới hạn.