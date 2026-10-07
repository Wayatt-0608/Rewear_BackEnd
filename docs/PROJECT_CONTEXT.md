# REWEAR backend context

Last reviewed: 2026-10-07. This file describes the code currently in this repository, not a promise that every external integration is working. When it conflicts with implementation, inspect the implementation, resolve the difference, and update this file.

## Purpose and structure

REWEAR is an ASP.NET Core 10 REST backend for resale clothing. The solution has four projects:

| Project | Responsibility |
| --- | --- |
| `REWEAR.Domain` | MongoDB entities, statuses, user roles, permission names. |
| `REWEAR.Application` | DTOs, interfaces, and business services. |
| `REWEAR.Infrastructure` | MongoDB repositories and context, PayOS, Cloudinary, email, payment expiration worker. |
| `REWEAR.API` | Controllers, JWT and authorization setup, dependency injection, Swagger, deployment entry point. |

The API uses MongoDB.Driver. `REWEAR.API/Program.cs` wires services, authentication, policies, middleware, and controllers. `MongoDbContext.cs` owns collections, startup data updates, and indexes. The Docker image runs the API in Production on the port supplied by the host. This repository contains the backend only; the frontend return page is external. There is no test project in this repository as reviewed.

## Where to look first

| Area | Entry point | Business logic | Storage or integration |
| --- | --- | --- | --- |
| Registration, OTP, login, reset | `AuthController` | `AuthService` | `UserRepository`, `EmailService` |
| Profile and addresses | `ProfileController` | `ProfileService` | `UserRepository`, `AddressRepository`, Cloudinary avatar upload |
| Catalog | `ProductsController`, `BrandsController`, `CategoriesController` | Matching application services | Matching repositories, Cloudinary images |
| Sell to REWEAR | `SourcingController` | `SourcingService` | `SourcingRepository`, `ProductRepository`, Cloudinary |
| Cart and checkout | `CartController`, `OrdersController` | `CartService`, `OrderService` | `CartRepository`, `ProductRepository`, `OrderRepository` |
| Payment | `PaymentController`, `PaymentWebhookController`, `PaymentAdminController` | `PaymentService` | `PayOsService`, `PaymentRepository`, `PaymentExpirationService` |
| Shipping and tracking | `ShippingController`, `OrdersController` | `ShippingService`, `OrderService` | `ShippingRepository`, order status history |

Controllers define the actual routes and authorization. Do not infer current routes, available features, or payment methods from `BACKEND2_TASKS.md`; parts of that checklist contradict the code.

Most business errors use `ApiResponse` (`Success`, `Message`, `Data`, `ErrorCode`), and controllers choose HTTP status from `ApiErrorCode`. Check the actual controller contract when adding or changing an endpoint. The main API route groups are `/api/auth`, `/api/profile`, `/api/products`, `/api/brands`, `/api/categories`, `/api/sourcing`, `/api/cart`, `/api/orders`, `/api/payments`, `/api/shipping`, and `/api/admin`.

## Business flows and invariants

### Identity and access

- Registration uses email OTP; passwords use BCrypt; login issues JWT. Roles are `Member`, `Admin`, `Staff`, and `Shipper`.
- `Program.cs` configures JWT policies, while `RoleCheckingMiddleware` logs requests and blocks non-admin access under `/api/admin`. Individual controllers also apply `[Authorize]` and policies. Check both layers when changing permissions.
- Member-owned data such as addresses, cart, orders, and payment status must be scoped to the authenticated user. Admin or staff access is an explicit route or policy choice.

### Sourcing and catalog

- A member submits a sourcing request. The current state path is `Pending → UnderReview → Priced → Accepted → Received → Approved → Completed`. `Pending` or `UnderReview` may become `Rejected`; `Priced` may become `Declined`.
- Staff or admin review, price, receive, inspect, and convert an approved request into a product. The member accepts or declines the offer. Conversion uses the inspected condition when present and the selling price supplied at conversion; it creates an `Available` product.
- Products, brands, and categories have active/soft-delete behavior. Product images and avatars use Cloudinary. Inspect the controller and service before changing visibility or upload behavior.

### Cart, inventory, and order

- Cart items hold a price snapshot and `IsSelected`. Checkout consumes only selected items and leaves unselected items in the cart. Carts have a TTL index. If a price changed since the cart snapshot, checkout warns and uses the current product price in the order.
- Checkout verifies the shipping address belongs to the user, validates selected products and current prices, reserves each product through an atomic repository update, creates an order in `AwaitingPayment`, writes the first history entry, and removes purchased cart items. If reservation or order creation fails, already reserved stock is released.
- Product stock states are `Available`, `Reserved`, `Sold`. Reservation decrements stock and marks `Reserved`; release increments stock and restores `Available`; delivery commits the reserved stock and marks `Sold` when depleted. Changes to one of these operations must be checked against the others.
- The order path is `AwaitingPayment → Confirmed → Shipping → Delivered`. An unpaid order can become `Cancelled` or `PaymentExpired`. A paid cancellation runs through the payment/refund path. A transition to `Shipping` requires a tracking number; `Delivered` commits inventory. `OrderStatusHistory` records status transitions.
- Shipping methods are Standard and Express; the current shipping fee is zero. Shipping records are created only for paid orders. Tracking number and estimated delivery date are mirrored to the order.

### PayOS and payment lifecycle

- `OrdersController.Checkout` calls `OrderService.CheckoutAsync` and then `PaymentService.CreateSessionAsync`. The payment service calls `PayOsService` to create a payment link and stores the resulting session. If link creation fails, it cancels the order and releases stock.
- The PayOS request uses a numeric PayOS order code distinct from the REWEAR `RW-...` order code. The payment record maps the PayOS payment link ID back to the REWEAR order. Amounts are integer VND in the gateway request.
- The backend currently supports both signed webhook intake and active status reconciliation through `GET /v2/payment-requests/{id}`. Paid processing is intended to be idempotent, to check the amount, and to move the order to `Confirmed`. Expired or cancelled unpaid sessions release inventory. The expiration worker scans pending sessions about once a minute.
- Payment statuses are `Pending`, `Paid`, `Failed`, `Expired`, `Refunded`. Any change to payment completion, cancellation, retry, expiration, or refund must consider duplicate callbacks, late payments, concurrent requests, order status, stock, and history together. A browser redirect alone does not prove payment.
- `PayOsService` uses `PayOs:ClientId`, `ApiKey`, `ChecksumKey`, `BaseUrl`, and `AppBaseUrl`. Production must receive the required values from its runtime configuration. `AppBaseUrl` supplies browser return/cancel URLs and must point to a frontend route that exists.

## Current integration points to verify before relying on them

- `Program.cs` currently overrides PayOS DNS resolution with fixed Cloudflare IPs. Recheck the live endpoint and network behavior before changing or depending on this workaround.
- `PayOsService.RefundAsync` currently calls `/v1/refunds`; confirm the real PayOS contract and merchant capability before treating automatic refunds as proven.
- `PaymentWebhookController` and `PaymentService` contain the current callback and reconciliation rules. Verify their payload mapping and signature behavior against the gateway's current specification when editing payment code.
- `appsettings.Development.json` contains empty PayOS credentials and an older PayOS base URL; `appsettings.json` has no PayOS section. The Docker image runs Production. Runtime environment variables can override files, so repository contents do not establish deployed values.
- `PaymentGatewayConfig` currently falls back to the PayOS API base URL when `AppBaseUrl` is empty. Check that the deployed frontend return/cancel route exists before using this fallback.
- `BACKEND2_TASKS.md` is a planning artifact and contains outdated endpoint and COD statements. The source files above are the current implementation reference.

## Persistence and configuration

`MongoDbContext` exposes Users, Brands, Categories, Products, Addresses, Carts, Orders, OrderStatusHistories, Payments, Shippings, and SourcingRequests. Important indexes include one cart per user, cart expiration TTL, unique order code, a partial unique PayOS transaction ID, and one shipping record per order. Review existing indexes and any startup data update before adding fields or changing identifiers.

`Program.cs` reads configuration sections for `MongoDbSettings`, `JwtSettings`, `Cloudinary`, and `PayOs`; `EmailService` reads `EmailSettings`. Production values may come from host environment variables using ASP.NET Core's double-underscore nesting convention, for example `PayOs__ClientId`. Do not put configuration values in documentation.

## Change maintenance

Never put secrets in this file. Record configuration *names* only. For changes across business boundaries, inspect controller → application service/interface/DTO → domain entity/status → repository/integration → background worker as applicable. Update this context when a verified behavior, route family, state transition, configuration dependency, or integration risk changes. Keep unresolved behavior labeled as an observation rather than an invariant.
