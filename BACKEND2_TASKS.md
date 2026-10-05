# Backend 2 - Commerce / Customer Operation

## 📌 Task Checklist

### ✅ Completed (BE1)
- [x] **#1** Product Domain foundation
- [x] **#2** Brand & Category
- [x] **#3** Sell Request / Sourcing
- [x] **#4** Screening & Inspection
- [x] **#5** Sourcing Offer / Accept-Reject
- [x] **#6** Product Management
- [x] **#7** Product Images
- [x] **#8** Product Variant / Size
- [x] **#9** Inventory
- [x] **#10** Search / Filter
- [x] **#11** Wishlist
- [x] **#12** Styling Profile
- [x] **#13** Recommendation
- [x] **#14** Lookbook / Outfit
- [x] **#15** AI Styling integration
- [x] **#16** Community Post
- [x] **#17** Like / Comment / Save
- [x] **#18** Product/Sourcing statistics

---

## 🎯 Backend 2 Tasks (Your Tasks)

### Task 1: User/Auth foundation
- [x] User entity (extend existing User)
- [x] Authentication (Register/Login với JWT)
- [x] OTP Email verification
- [x] Password hashing (BCrypt)
- [x] JWT token generation
- [x] Refresh token
- [x] Logout
- [x] Forgot password / Reset password
- [x] Role system (Admin, Staff, Shipper, Member)
 
### Task 2: User Profile & Address
- [x] User Profile CRUD
- [x] Update avatar
- [x] Update personal info (name, phone, gender, DOB)
- [x] Address CRUD (multiple addresses per user)
- [x] Default address
- [x] Shipping address management

### Task 3: Role & Authorization
- [x] Role entity (Member, Admin, Staff, Shipper)
- [x] Role-based authorization (Role được nhúng vào JWT)
- [x] Permission policies (16 permission + policy động theo permission)
- [x] [Authorize(Roles = "Admin")] → dùng [Authorize(Policy = "AdminOnly")]
- [x] Middleware for role checking (RoleCheckingMiddleware)

### Task 4: Cart
- [x] Cart entity (UserId, Items)
- [x] CartItem entity (ProductId, Quantity)
- [x] Add to cart
- [x] Update quantity
- [x] Remove from cart
- [x] Clear cart
- [x] Get cart with product details
- [x] Cart expiration handling
- [x] Tick chọn từng món (IsSelected) — mua theo nhóm đã tick, giống Shopee
- [x] Tick chọn / bỏ chọn cả giỏ
- [x] Dọn rác món "chết" (sản phẩm đã xóa khỏi catalog)

### Task 5: Checkout
- [x] Checkout DTO (shipping address, payment method)
- [x] Cart validation
- [x] Inventory check before checkout
- [x] Order creation from cart
- [x] Clear cart after checkout
- [ ] Apply voucher (Task 10)

### Task 6: Order Management
- [x] Order entity (OrderId, UserId, Items, Total, Status)
- [x] OrderItem entity (ProductId, Quantity, Price)
- [x] Order status enum (Pending, Confirmed, Shipping, Delivered, Cancelled)
- [x] Create order
- [x] Get order by user
- [x] Get order detail
- [x] Cancel order
- [x] Order history

### Task 7: Order Status / Tracking
- [x] Order tracking entity (OrderStatusHistory)
- [x] Update order status (Admin/Seller)
- [x] Order timeline (status history)
- [x] Tracking number
- [x] Estimated delivery date
- [ ] Notification when status changes (Task 15)

### Task 8: Payment (PayOS - trả tiền trước)
- [x] Payment entity (OrderId, Amount, Method, Status)
- [x] Payment method enum (chỉ PayOs - bỏ COD/VNPay/Momo)
- [x] Payment status enum (Pending/Paid/Failed/Expired/Refunded)
- [x] Payment integration (PayOS)
- [x] Payment callback/webhook (verify HMAC-SHA256 + idempotency)
- [x] Payment status update (webhook + đối soát chủ động)
- [x] Payment history
- [x] Phiên thanh toán có hạn 15 phút + background job trả kho
- [x] Hoàn tiền tự động qua PayOS khi hủy đơn đã trả
- [ ] Cấu hình key PayOS thật (ClientId/ApiKey/ChecksumKey)

### Task 9: Shipping
- [x] Shipping entity (OrderId, Method, Fee, Status)
- [x] Shipping method (Standard, Express)
- [x] Shipping fee calculation (REWEAR free ship toàn bộ → fee = 0)
- [ ] GHN/GHTK integration (optional)
- [x] Tracking integration (mirror sang Order.TrackingNumber)
- [x] Update shipping status

### Task 10: Voucher
- [ ] Voucher entity (Code, Discount, Expiry, UsageLimit)
- [ ] Voucher types (Percentage, FixedAmount)
- [ ] Voucher CRUD (Admin)
- [ ] Apply voucher to order
- [ ] Validate voucher (expiry, usage limit, min order)
- [ ] User voucher collection

### Task 11: Review & Rating
- [ ] Review entity (ProductId, UserId, Rating, Comment, Images)
- [ ] Create review (after delivered order)
- [ ] Get reviews by product
- [ ] Update/Delete review (owner only)
- [ ] Average rating calculation
- [ ] Reply review (Seller)

### Task 12: Return / Refund
- [ ] Return request entity (OrderId, Reason, Status)
- [ ] Create return request
- [ ] Upload return images
- [ ] Approve/Reject return (Admin/Seller)
- [ ] Refund processing (link to Payment Task 8)
- [ ] Return history

### Task 13: Loyalty
- [ ] Loyalty point entity (UserId, Points, History)
- [ ] Earn points on purchase
- [ ] Redeem points (voucher/discount)
- [ ] Point history
- [ ] Tier system (Bronze/Silver/Gold)

### Task 14: Promotion
- [ ] Promotion entity (Name, Products, Discount, Period)
- [ ] Flash sale
- [ ] Banner promotion
- [ ] Apply promotion to product/cart
- [ ] Promotion CRUD (Admin)

### Task 15: Notification
- [ ] Notification entity (UserId, Type, Title, Content, IsRead)
- [ ] Notification types (Order, Promotion, System, Community)
- [ ] Send notification (push/email/in-app)
- [ ] Get user notifications
- [ ] Mark as read
- [ ] Real-time notification (SignalR - optional)

### Task 16: Follow / Feed
- [ ] Follow entity (FollowerId, FolloweeId)
- [ ] Follow/Unfollow user
- [ ] Get followers/following list
- [ ] Feed generation (posts from followed users - link to BE1 #16, #17)
- [ ] Personalized feed

### Task 17: Community Marketplace transaction
- [ ] Marketplace transaction (C2C between users)
- [ ] Transaction status (Listed, Reserved, Sold)
- [ ] Buyer-Seller chat (optional)
- [ ] Transaction history

### Task 18: Dashboard / Sales Analytics
- [ ] Sales statistics (revenue, orders count)
- [ ] Customer analytics (new users, active users)
- [ ] Product performance (top selling, views)
- [ ] Time-based reports (daily, weekly, monthly)
- [ ] Admin dashboard API
- [ ] Chart data (for frontend)

---

## 🚀 Order of Implementation

### Phase 1: Foundation (Week 1)
- [ ] Task 1: User/Auth foundation
- [ ] Task 2: User Profile & Address
- [ ] Task 3: Role & Authorization

### Phase 2: Shopping Flow (Week 2)
- [x] Task 4: Cart
- [x] Task 5: Checkout
- [x] Task 6: Order Management

### Phase 3: Order Fulfillment (Week 3)
- [ ] Task 7: Order Status / Tracking
- [ ] Task 8: Payment
- [ ] Task 9: Shipping

### Phase 4: Engagement (Week 4)
- [ ] Task 10: Voucher
- [ ] Task 11: Review & Rating
- [ ] Task 13: Loyalty
- [ ] Task 14: Promotion

### Phase 5: Advanced (Week 5)
- [ ] Task 12: Return / Refund
- [ ] Task 15: Notification
- [ ] Task 16: Follow / Feed

### Phase 6: Analytics (Week 6)
- [ ] Task 17: Community Marketplace transaction
- [ ] Task 18: Dashboard / Sales Analytics

---

## 📝 Notes

- Each task should include: Domain → Application (DTO/Interface) → Infrastructure (Repository/Service) → API (Controller)
- Use MongoDB collections for all entities
- Follow Clean Architecture pattern
- JWT auth for all protected endpoints
- Return standardized API responses (`ApiResponse`: `success` / `message` / `data` / `errorCode`)

### 🔌 Endpoints (base URL: `http://localhost:5000`)

| Method | Route | Auth | Mô tả |
|---|---|---|---|
| POST | `/api/auth/register` | Public | Đăng ký (gửi OTP xác minh email) |
| POST | `/api/auth/verify-otp` | Public | Xác minh OTP |
| POST | `/api/auth/login` | Public | Đăng nhập, trả JWT |
| POST | `/api/auth/forgot-password` | Public | Gửi email reset password |
| POST | `/api/auth/reset-password` | Public | Đặt lại mật khẩu |
| GET | `/api/cart` | JWT | Lấy giỏ hàng (kèm chi tiết sản phẩm) |
| GET | `/api/cart/count` | JWT | Số lượng món trong giỏ |
| POST | `/api/cart` | JWT | Thêm món vào giỏ (mặc định tick chọn) |
| PUT | `/api/cart/items/{itemId}` | JWT | Cập nhật số lượng |
| PUT | `/api/cart/items/{itemId}/select` | JWT | Tick / bỏ tick một món |
| PUT | `/api/cart/items/select-all` | JWT | Tick / bỏ tick cả giỏ |
| DELETE | `/api/cart/items/{itemId}` | JWT | Xóa một mục |
| DELETE | `/api/cart` | JWT | Xóa toàn bộ giỏ |
| POST | `/api/orders/checkout` | JWT | Đặt hàng từ nhóm món đã tick |
| GET | `/api/orders` | JWT | Lịch sử đơn (có phân trang/lọc) |
| GET | `/api/orders/{id}` | JWT | Chi tiết đơn |
| GET | `/api/orders/code/{orderCode}` | JWT | Tra đơn bằng mã đơn |
| PUT | `/api/orders/{id}/cancel` | JWT | Huỷ đơn |
| GET | `/api/profile` | JWT | Thông tin cá nhân |
| PUT | `/api/profile` | JWT | Cập nhật thông tin cá nhân |
| PUT | `/api/profile/avatar` | JWT | Cập nhật ảnh đại diện (multipart) |
| GET | `/api/profile/addresses` | JWT | Danh sách địa chỉ |
| GET | `/api/profile/addresses/{id}` | JWT | Chi tiết một địa chỉ |
| POST | `/api/profile/addresses` | JWT | Thêm địa chỉ |
| PUT | `/api/profile/addresses/{id}` | JWT | Cập nhật địa chỉ |
| DELETE | `/api/profile/addresses/{id}` | JWT | Xóa địa chỉ |
| PUT | `/api/profile/addresses/{id}/default` | JWT | Đặt làm địa chỉ mặc định |
| GET | `/api/products` | Public | Danh sách sản phẩm (lọc/phân trang) |
| POST | `/api/products` | Admin | Tạo sản phẩm |
| GET | `/api/products/{id}` | Public | Chi tiết sản phẩm |
| PUT | `/api/products/{id}` | Admin | Cập nhật sản phẩm |
| DELETE | `/api/products/{id}` | Admin | Xóa (soft delete) |
| PATCH | `/api/products/{id}/restore` | Admin | Khôi phục |
| PATCH | `/api/products/{id}/status` | Admin | Đổi trạng thái |
| GET | `/api/brands` | Public | Danh sách thương hiệu |
| POST | `/api/brands` | Admin | Tạo thương hiệu |
| GET | `/api/brands/{id}` | Public | Chi tiết thương hiệu |
| PUT | `/api/brands/{id}` | Admin | Cập nhật thương hiệu |
| DELETE | `/api/brands/{id}` | Admin | Xóa (soft delete) |
| PATCH | `/api/brands/{id}/restore` | Admin | Khôi phục |
| GET | `/api/categories` | Public | Danh sách danh mục |
| POST | `/api/categories` | Admin | Tạo danh mục |
| GET | `/api/categories/{id}` | Public | Chi tiết danh mục |
| PUT | `/api/categories/{id}` | Admin | Cập nhật danh mục |
| DELETE | `/api/categories/{id}` | Admin | Xóa (soft delete) |
| PATCH | `/api/categories/{id}/restore` | Admin | Khôi phục |
| GET | `/api/admin/users` | Admin + `users.read` | Danh sách người dùng |
| GET | `/api/admin/users/{id}` | Admin + `users.read` | Chi tiết người dùng |
| PUT | `/api/admin/users/{id}/role` | Admin + `users.update_role` | Đổi vai trò |
| DELETE | `/api/admin/users/{id}` | Admin + `users.delete` | Xoá người dùng |

> **Lưu ý:** mọi endpoint trừ `/api/auth/*` đều cần header `Authorization: Bearer <token>`.
> `paymentMethod` hiện chỉ dùng `"COD"` (các phương thức online sẽ xử lý ở Task 8).
