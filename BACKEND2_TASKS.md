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
- [ ] Cart entity (UserId, Items)
- [ ] CartItem entity (ProductId, VariantId, Quantity)
- [ ] Add to cart
- [ ] Update quantity
- [ ] Remove from cart
- [ ] Clear cart
- [ ] Get cart with product details
- [ ] Cart expiration handling

### Task 5: Checkout
- [ ] Checkout DTO (shipping address, payment method)
- [ ] Cart validation
- [ ] Inventory check before checkout
- [ ] Order creation from cart
- [ ] Clear cart after checkout
- [ ] Apply voucher (Task 10)

### Task 6: Order Management
- [ ] Order entity (OrderId, UserId, Items, Total, Status)
- [ ] OrderItem entity (ProductId, VariantId, Quantity, Price)
- [ ] Order status enum (Pending, Confirmed, Shipping, Delivered, Cancelled)
- [ ] Create order
- [ ] Get order by user
- [ ] Get order detail
- [ ] Cancel order
- [ ] Order history

### Task 7: Order Status / Tracking
- [ ] Order tracking entity
- [ ] Update order status (Admin/Seller)
- [ ] Order timeline (status history)
- [ ] Tracking number
- [ ] Estimated delivery date
- [ ] Notification when status changes (Task 15)

### Task 8: Payment
- [ ] Payment entity (OrderId, Amount, Method, Status)
- [ ] Payment method enum (COD, VNPay, Momo, Banking)
- [ ] Payment integration (VNPay sandbox)
- [ ] Payment callback/webhook
- [ ] Payment status update
- [ ] Payment history

### Task 9: Shipping
- [ ] Shipping entity (OrderId, Address, Method, Status)
- [ ] Shipping method (Standard, Express)
- [ ] Shipping fee calculation
- [ ] GHN/GHTK integration (optional)
- [ ] Tracking integration
- [ ] Update shipping status

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

## 📂 Architecture Layers

```
REWEAR.BackEnd/
├── REWEAR.Domain/
│   └── Entities/
│       ├── User.cs (extended)
│       ├── Address.cs
│       ├── Cart.cs
│       ├── CartItem.cs
│       ├── Order.cs
│       ├── OrderItem.cs
│       ├── Payment.cs
│       ├── Shipping.cs
│       ├── Voucher.cs
│       ├── Review.cs
│       ├── ReturnRequest.cs
│       ├── LoyaltyPoint.cs
│       ├── Promotion.cs
│       ├── Notification.cs
│       └── Follow.cs
│
├── REWEAR.Application/
│   ├── DTOs/
│   │   ├── AuthDTOs.cs (extend)
│   │   ├── AddressDTOs.cs
│   │   ├── CartDTOs.cs
│   │   ├── OrderDTOs.cs
│   │   ├── PaymentDTOs.cs
│   │   ├── VoucherDTOs.cs
│   │   ├── ReviewDTOs.cs
│   │   └── ...
│   ├── Interfaces/
│   │   ├── IAuthService.cs (extend)
│   │   ├── ICartService.cs
│   │   ├── IOrderService.cs
│   │   ├── IPaymentService.cs
│   │   └── ...
│   └── Services/
│       ├── AuthService.cs (extend)
│       ├── CartService.cs
│       ├── OrderService.cs
│       └── ...
│
├── REWEAR.Infrastructure/
│   ├── Persistence/
│   │   └── MongoDbContext.cs (extend collections)
│   ├── Repositories/
│   │   ├── UserRepository.cs (extend)
│   │   ├── OrderRepository.cs
│   │   ├── CartRepository.cs
│   │   └── ...
│   └── Services/
│       ├── JwtService.cs
│       ├── PaymentGateway/VNPayService.cs
│       ├── ShippingService.cs
│       └── ...
│
└── REWEAR.API/
    └── Controllers/
        ├── AuthController.cs (extend)
        ├── UsersController.cs
        ├── CartController.cs
        ├── OrdersController.cs
        ├── PaymentsController.cs
        ├── VouchersController.cs
        ├── ReviewsController.cs
        └── ...
```

---

## 🚀 Order of Implementation

### Phase 1: Foundation (Week 1)
- [ ] Task 1: User/Auth foundation
- [ ] Task 2: User Profile & Address
- [ ] Task 3: Role & Authorization

### Phase 2: Shopping Flow (Week 2)
- [ ] Task 4: Cart
- [ ] Task 5: Checkout
- [ ] Task 6: Order Management

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
- Return standardized API responses
