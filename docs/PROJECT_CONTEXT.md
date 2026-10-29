# REWEAR backend context

## Styling Profile update — 2026-10-29

`StylingProfile` is the user's fashion-preference profile, stored in its own `StylingProfiles` collection with a unique index on `userId`. That index is the concurrency guard: at most one profile per user, and two simultaneous writes cannot create two documents (the losing insert is caught as duplicate-key and re-read/updated instead of erroring). `StylingProfileController` exposes `GET /api/styling-profile`, `PUT /api/styling-profile` and `DELETE /api/styling-profile`; the owner always comes from the JWT `NameIdentifier` claim and is never accepted from body, query or route. There is no `userId` in any request DTO, so a user can only read and write their own profile. The route family uses plain `[Authorize]` (any authenticated role), matching `WishlistController`, not a Member-only policy. `GET` returns 404 with `ErrorCode = NOT_FOUND` when the user has no profile yet; `PUT` is upsert and idempotent (repeated calls keep one document); `DELETE` is idempotent. `PUT` semantics are full replace: a group not resent is cleared, so clients must resend what they want to keep. All groups are optional. `Styles` is validated against the `FashionStyle` enum (`Minimalist`, `Casual`, `Streetwear`, `Vintage`, `Sporty`, `Classic`, `Elegant`), case-insensitive, stored as strings so enum reordering is safe. Style values are matched against the enum *names* rather than parsed with `Enum.TryParse`, because `Enum.TryParse` also accepts numeric strings (`"7"`, `"-1"`) and would admit values that are not real styles; numeric-looking style values are rejected. `Sizes` and `Colors` stay free-form strings because `Product.Size` and `Product.Color` are free-form strings, so the profile matches catalog data directly without a lookup table. `FavoriteCategoryIds` must be valid ObjectIds that exist in `Categories` (404 otherwise) and are read back live as names/slugs; a category deleted from the catalog is skipped in the response without failing it. A category that still exists but is inactive (`IsActive = false`) is rejected on write, matching how `ProductService` refuses inactive categories. Category names/slugs are resolved with one batched `ICategoryRepository.GetByIdsAsync` lookup rather than one query per category, so reading a profile with N favorite categories costs one query, not N. `MinBudget`/`MaxBudget` are integer VND decimals, both optional, non-negative, and `MinBudget` must not exceed `MaxBudget`; they are stored as `Decimal128`, so no precision is lost. The four list properties on `StylingProfile` never return null: the getters coalesce null to an empty list because the MongoDB driver can write the backing field directly during deserialization, bypassing the property setter, and a document with a null list field would otherwise fail the read API with an unhandled exception. When an update finds no matching document (for example a `DELETE` that raced with the `PUT`), the repository reports it and the service answers `409 Conflict` rather than silently reporting success for a write that never happened. `PUT` maps `NOT_FOUND` to 404, `CONFLICT` to 409 and other failures to 400, matching the other order and shipping controllers. This profile is separate from the account profile: it never reads or writes `User`, `Address`, auth or catalog entities, and it does not affect cart, checkout, orders, payment, shipping or wishlist. Recommendation and AI Styling are not implemented and are not implied by this data.

## Wishlist update — 2026-10-26

`WishlistItem` is a per-user link to a product, stored in its own `WishlistItems` collection with a unique compound index on `(userId, productId)`. That index is the concurrency guard: simultaneous adds can only insert one record, and the losing request is handled as an idempotent success instead of an error. `WishlistController` exposes `GET /api/wishlist`, `POST /api/wishlist/{productId}`, `DELETE /api/wishlist/{productId}` and `GET /api/wishlist/check/{productId}`; the owner is always taken from the JWT `NameIdentifier` claim, never from the request body or query. Add and delete are both idempotent. Adding requires the product to exist (404 otherwise) and to be `IsActive`, `Status == Available` and `StockQuantity > 0` (400 otherwise), which is the strictest reading of the existing cart and checkout rules. A saved product that is later sold, hidden or emptied stays in the wishlist with `IsAvailableForPurchase = false`; deleting a wishlist entry never deletes the product, its images or its stock. The wishlist stores no price or image copy: responses read live product data in one batch lookup, and products removed from the catalog degrade to a minimal safe payload. No product cost, sourcing note or staff field is exposed.

## Delivery integration update — 2026-10-25

Shipper reuses AdminLayout/ShippingPage; Member tracks orders inside purchases, with duplicate HistorySection removed. Reuse shipping endpoints and OrderStatusHistories. Preserve ordinals 0–4; append PickedUp=5. New shipments require pickup before transit; legacy shipments retain compatibility without fabricated timestamps. Assigned shipper alone updates status; Staff/Admin dispatch and view all. Customer tracking returns snapshots and sanitized events with nullable dates; excludes internal notes and actor/shipper ids. Custody handoffs are independent from stock allocation; delivery never decrements paid stock again.

DeliverySchemaMigration v2 adds indexes only; see [acceptance and open gaps](../../context/shipping-acceptance-2026-10-25.md). Standard local Mongo suite passed 41 tests; separate real-local-API Chromium acceptance covers shared UI and inline tracking. Gateway is a test double. Broad Phase 5 audit remains incomplete.

Last reviewed: **2026-10-29**. The current local implementation is described here and in [the cross-stack implementation report](../../context/implementation-report-2026-10-25.md). Historical notes below are retained for traceability and are superseded where they differ. No production deployment, money transfer or merchant live acceptance was performed.

## Architecture and entry points

The existing Domain → Application → Infrastructure → API layering remains. REWEAR.Tests adds NUnit integration tests with isolated MongoDB. MongoDbContext's TransactionalCollection wrapper joins repository reads/writes to the current session; MongoCommerceUnitOfWork requires replica-set/sharded transactions with snapshot reads and majority writes. Do not bypass the unit of work for money/stock/order mutations.

Program registers all new services, unit of work and CommerceOptions. The actual API entrypoint is tested with an empty content root, explicit test configuration and local GUID database. Public registration is Member-only. Catalog writes need Staff/Admin; brands need Admin. Auth endpoints are rate-limited; OTP checks have a per-account attempt limit and cryptographic generation. SMTP errors return failure and do not log OTP. JWT debug output, TLS bypass and fixed gateway IPs were removed. One CORS policy is used.

## Current commerce rules

- Source offers have a version; accepting a stale offer fails. Receive/inspect creates raw inventory/history. Changed price/condition requires renewed owner acceptance. Rejected physically held items have return-to-sender handling.
- A confirmed PurchasePayout records money actually paid manually, reference, method and actor. It does not transfer money. Only paid/approved raw stock is allocated or sold; allocation and payout duplicates are guarded transactionally.
- Upcycling projects own their allocated materials, costs, before/after images and timeline. QC failure requires rework; only QCApproved can publish a unique product. Publishing consumes raw materials and creates a separate finished retail inventory record with source and cost basis.
- Checkout uses selected lines, owner address, current integer-VND prices and immutable snapshots. Default reservation is 15 minutes, configurable by Commerce:ReservationMinutes (2–60). Retry does not extend the deadline. Keep cart lines until a valid persisted gateway URL exists.
- StockReservation is owned by order/product and changes Reserved → Paid or Released. Paid stock is restored only by the appropriate paid-refund path. Product Version guards stale catalog writes; unique items cannot be edited to stock > 1; stock with active allocations cannot be reset through catalog editing. Missing legacy stock reads as 0.
- Successful verified payment commits the reservation and order/history atomically. Product becomes Sold if no available units remain; delivery does not decrement again. Late/mismatched/extra receipts hold reconciliation, without taking another order's stock. Actual refunded amount is kept separately from later received money.
- Gateway I/O stays outside database transactions. Session creation is serialized on order and uses one durable numeric gateway identity. Creation response and webhook signatures are checked; gateway lookup checks link, order code and amount. Browser redirects do not prove payment.
- Worker expires stale payments and orphan AwaitingPayment orders with deadlines, independent of a browser. Bounded catch-up checks unconfirmed transactions within 30 days, at most 10 per scan and no more often than every five minutes per record. Older/complex cases require operator reconciliation.
- Shipping requires Paid with no reconciliation/refund hold. Server enforces assigned shipper and Pending → InTransit → Delivered/Failed. Metadata does not move state. Failed delivery may be reshipped by Admin while preserving failure history, or physically received for return.
- Returns currently cover the entire order. Owner may request after delivery. Staff/Admin receipt then per-item inspection precede refund confirmation. Stock stays unavailable until an authorized actual refund confirmation; only inspection-approved items return to sale, others remain quarantined.
- Automatic refunds remain disabled pending a verified merchant API contract. Request, actual money return and confirmation are separate. Manual confirmation is Admin-only with reference/exact amount. Partial/excess refund workflows are not implemented; do not use a full-order refund to imply a partial refund succeeded.
- Vouchers: `Voucher` is the admin-side definition; `UserVoucher` is the per-user grant. Only Member accounts receive vouchers; grant endpoints reject other roles. A voucher moves Available → Reserved at checkout and Reserved → Used only after verified payment. Every transition is an atomic compare-and-set keyed to the owning order, so one grant cannot be reserved by two orders and a repeated webhook cannot consume it twice. Reserved vouchers are released when the unpaid order is cancelled, fails, or expires. Discount is clamped by MaxDiscount, then MinDiscount, then the order subtotal, so `TotalAmount` can never be negative. Checkout validates the voucher before reserving stock, and releases both stock and the voucher reservation if order creation fails.

## APIs and frontend

OperationsController exposes staff/admin inventory, movements, payout, projects, QC, uploads, operators/shippers and paginated order reads. Metrics is Admin-only and aggregates persisted payment/payout/cost records, with order cost snapshots and conservative unknown-cost handling. ReturnsController separates owner requests from staff receive/inspect; PaymentAdminController confirms actual refunds. Technical route IDs in the new controllers are validated as ObjectIds.

Voucher routes are separate from the admin route family: `VouchersController` exposes the member-facing grant endpoints and `AdminVouchersController` exposes Admin-only definition management. Both validate `{id}` as an ObjectId through `ObjectIdRouteFilter`, matching the other technical-id controllers. Checkout accepts an optional voucher code on the request body; the order response echoes the applied voucher id/code and the discount amount.

Wishlist routes form their own family under `/api/wishlist`. Like the voucher collection, they use plain `[Authorize]` rather than a Member-only policy, so every authenticated role can manage its own list. The `{productId}` route parameter is validated as an ObjectId by `ObjectIdRouteFilter`.

Styling Profile routes form their own family under `/api/styling-profile` and use the same plain `[Authorize]` policy as wishlist. The route has no `{id}` parameter, so `ObjectIdRouteFilter` is not applied and no route-id validation is needed.

The surrounding frontend now uses these APIs for inventory, upcycling/QC, admin orders and dashboard. Sourcing UI includes payout evidence and versioned acceptance; shipping UI uses actual shipper lists/allowed next steps. Customer profile reads tracking timeline and can request returns. Home implementation was retained. Existing donation/promotion/settings demos outside the acceptance spine remain explicitly marked.

Authoritative enums: Payment 0 Pending, 1 Paid, 2 Failed, 3 Expired, 4 Refunded; Sourcing 6 Completed, 7 Rejected, 8 Declined; Order appends ReturnRequested=7, ReturnReceived=8, Returned=9 without renumbering older states. Status endpoint returns strings plus reconciliation flags. Product PUT is multipart/KeepImageUrls. Public product DTO excludes private cost and customer data.

## Migration, checks and remaining limits

Read [COMMERCE_MIGRATION.md](COMMERCE_MIGRATION.md) before deployment. Version 20261025_commerce_v1 adds indexes explicitly; check/apply modes never rewrite legacy financial/user documents or fabricate IDs/costs/reservations. Review old Reserved stock, missing deadlines/stock/costs, duplicates and existing index options on a staging copy. Index DDL may complete partially before a later conflict; rerun only after review. Normal startup no longer runs destructive document cleanup or drops indexes.

The automated suite passed 77/77 tests on isolated MongoDB local port 27029 (13 of those cover the styling profile added on 2026-10-29: unique `userId` index, create, update, repeated upsert keeping one document, full-replace semantics, per-user scoping, invalid-input rejection, live category names, missing-profile handling, idempotent delete, no user/account data exposure, no effect on the account profile or addresses, and the HTTP status contract including 401/404/400). The previous 64/64 baseline (18 of those covering the wishlist added on 2026-10-26: add, duplicate add, concurrent add, unknown product, ineligible product, per-user scoping, removal, idempotent removal, membership check, pagination, newest-first ordering, sold/hidden products, unchanged stock, cart/checkout isolation, no internal data exposure, the unique `(userId, productId)` index, and the HTTP status contract including 401) still passes unchanged. The Jeans → Tote test verifies 299000 receipts, 100000 acquisition, fixture-only 70000 production costs, 129000 indicative gross profit and Delivered. Gateway response is simulated only in the test project. Production SMTP/Cloudinary/payOS, hosting worker behavior, partial refunds, historical repair, large-data pagination/load tests, complete accounting, Recommendation/AI Styling and full-storefront regression remain unverified or incomplete. Pkcs was pinned to patched 10.0.13 and restored successfully; no dependency vulnerability warning remains in the verified build.

<details>
<summary>Historical context snapshot before the 25/10 implementation — not current behavior</summary>

# REWEAR backend context

Last reviewed: 2026-10-24 (source review, no backend build or live transaction). This file describes the code currently in this repository, not a promise that every external integration is working. When it conflicts with implementation, inspect the implementation, resolve the difference, and update this file.

## 2026-10-24 cross-check with the surrounding frontend workspace

- This backend now sits under `Rewear_BackEnd/` in a workspace that also contains the React frontend. See the [business overview](../../context/business-overview-2026-10-24.md) for the cross-check with frontend behavior.
- The executed paid handler now calls `CommitStockAsync` immediately after saving Paid, setting Reserved products to Sold regardless of remaining stock. Older comments and the commerce skill describing Reserved until Delivered are stale for this source. Delivery also calls commit; its Reserved filter normally makes that call a no-op after payment.
- `ReleaseStockAsync` only accepts Available/Reserved. Consequently the refund service calling release does not restore products already marked Sold. Do not describe paid refunds and restocking as a completed invariant.
- Frontend numeric sourcing mapping disagrees with backend: source is 6 Completed, 7 Rejected, 8 Declined. Payment source enum is 1 Paid/2 Failed, opposite the frontend numeric fallback; the status endpoint returns strings and isPaid, so this alone does not prove the current status endpoint breaks. Order adds Failed=6. Product PUT accepts multipart while the surrounding frontend sends JSON.
- `PayOsSettings` and Program default ExpirationMinutes to 15; controller passes that setting, and the service fallback remains 5. Runtime may override settings; comments mentioning 5 minutes are not proof of the deployed TTL.
- Source concerns still requiring changes: public registration accepts the requested role; product write actions lack Authorize attributes; shipping status update validates assignment/enum but not all state transitions. These observations were not exercised against a running API. Admin cancellation after failed delivery performs no refund or restock and should not be presented as an approved customer policy.

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
- Product stock states are `Available`, `Reserved`, `Sold`. Reservation decrements stock and marks `Reserved`; release increments stock and restores `Available` only from Available/Reserved. The paid handler now commits Reserved to Sold immediately, even when stock remains. Delivery retains a commit call. Multi-quantity semantics and paid-refund restocking need reconciliation.
- The order path is `AwaitingPayment → Confirmed → Shipping → Delivered`. An unpaid order can become `Cancelled` or `PaymentExpired`. A paid cancellation runs through the payment/refund path. `OrderService.UpdateStatusAsync` validates transitions and requires tracking for Shipping; the separate `ShippingService.UpdateShippingStatusAsync` does not enforce all those guards. Delivery still calls inventory commit, while the paid handler already marks Sold. `OrderStatusHistory` records transitions. Failed orders have admin reship/cancel-permanent paths.
- Shipping methods are Standard and Express; the current shipping fee is zero. Shipping records are created only for paid orders. Tracking number and estimated delivery date are mirrored to the order.
- Shipper flow: `POST /api/shipping/{orderId}?shipperId=...` (StaffOrAdmin) creates a shipping record assigned to a shipper. `GET /api/shipping/queue` (StaffOrAdmin) returns confirmed orders waiting for a shipper. `GET /api/shipping/my-orders?status=...` returns the caller's shipper orders (shipper role is forced to the caller). `PUT /api/shipping/{orderId}/status` updates shipping + order state in lock-step and tracks `AttemptCount`, `LastFailureReason`, `LastFailedAt`, `DeliveredAt`. After failures, `POST /api/admin/shipping/{orderId}/reship` returns the order to `Confirmed` and the shipping to `Pending` while keeping `AttemptCount`; `PUT /api/admin/shipping/{orderId}/cancel-permanent` moves the order to `Cancelled` and the shipping to `Returned` (buyer keeps paying, stock stays `Sold`).

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

`MongoDbContext` exposes Users, Brands, Categories, Products, Addresses, Carts, Orders, OrderStatusHistories, Payments, Shippings, SourcingRequests, Vouchers, UserVouchers, WishlistItems and StylingProfiles. Important indexes include one cart per user, cart expiration TTL, unique order code, a partial unique PayOS transaction ID, one shipping record per order, a unique voucher code with per-user voucher indices, a unique `(userId, productId)` wishlist index and a unique `userId` styling-profile index. Review existing indexes and any startup data update before adding fields or changing identifiers.

`Program.cs` reads configuration sections for `MongoDbSettings`, `JwtSettings`, `Cloudinary`, and `PayOs`; `EmailService` reads `EmailSettings`. Production values may come from host environment variables using ASP.NET Core's double-underscore nesting convention, for example `PayOs__ClientId`. Do not put configuration values in documentation.

## Change maintenance

Never put secrets in this file. Record configuration *names* only. For changes across business boundaries, inspect controller → application service/interface/DTO → domain entity/status → repository/integration → background worker as applicable. Update this context when a verified behavior, route family, state transition, configuration dependency, or integration risk changes. Keep unresolved behavior labeled as an observation rather than an invariant.

</details>
