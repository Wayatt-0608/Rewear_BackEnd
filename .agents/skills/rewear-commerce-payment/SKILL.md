---
name: rewear-commerce-payment
description: Change or diagnose REWEAR cart, inventory, checkout, order, PayOS payment, refund, expiration, and shipping flows where money or stock state can diverge.
---

# REWEAR commerce and payment flow

Read `docs/PROJECT_CONTEXT.md` at the repository root and the affected controller, service, entity, repository, and worker before changing this flow. Use the current code to establish the real behavior; verify PayOS request and response fields against current provider documentation when the gateway contract matters.

Preserve these relationships across changes:

- Checkout buys selected cart items only. Validate address ownership, current prices and availability. Use Mongo transactions and order-owned StockReservations; never restore stock by product status alone. Preserve the cart until a valid payment URL is persisted, and always keep unselected items.
- A newly created order is `AwaitingPayment`. An on-time verified paid result commits its reservation to Paid, moves payment to `Paid` and order to `Confirmed`; product is Sold when no available stock remains. Delivery must not subtract stock again. Expired/cancelled unpaid orders release only their own Reserved ledger entries.
- Treat webhook and polling as two paths to the same payment outcome. Check signatures where webhook data is trusted, reconcile amount and payment link identity, and make repeated or late notifications safe. Do not mark paid from a browser return URL alone.
- Do not create shipping for unpaid, reconciliation-held or refund-requested orders. Check assigned shipper and state transitions on the server. Metadata edits never advance shipping state.
- Refund request does not mean money refunded. Automatic gateway refunds are disabled until a merchant contract is verified. Admin may confirm an actually completed manual refund with reference and exact amount. Shipped goods require full-order receipt and per-item inspection; only approved items may be restored once after refund confirmation. Keep partial/excess-money cases in reconciliation until a supported workflow exists.
- Sourcing payout is an actual manual-payment record, not an automatic transfer. Allocate only paid/inspected raw items to upcycling; publish only after QC approval. Preserve acquisition/production cost snapshots and inventory history, including quarantine.

For a proposed change, inspect timeout, retries, duplicate requests, gateway errors, and state races that actually touch the affected path. Keep the gateway base URL separate from the frontend return/cancel base URL. Do not log credentials or full signed payloads. Report unverified external behavior explicitly and update the project context when the implemented flow changes.
