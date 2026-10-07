---
name: rewear-commerce-payment
description: Change or diagnose REWEAR cart, inventory, checkout, order, PayOS payment, refund, expiration, and shipping flows where money or stock state can diverge.
---

# REWEAR commerce and payment flow

Read `docs/PROJECT_CONTEXT.md` at the repository root and the affected controller, service, entity, repository, and worker before changing this flow. Use the current code to establish the real behavior; verify PayOS request and response fields against current provider documentation when the gateway contract matters.

Preserve these relationships across changes:

- Checkout buys selected cart items only. Validate address ownership and current product availability, reserve stock atomically, release prior reservations on failure, and keep unselected cart items.
- A newly created order is `AwaitingPayment`. A valid paid result moves payment to `Paid` and order to `Confirmed`; inventory remains `Reserved` until delivery. Failed or expired unpaid checkout releases stock and records the order transition.
- Treat webhook and polling as two paths to the same payment outcome. Check signatures where webhook data is trusted, reconcile amount and payment link identity, and make repeated or late notifications safe. Do not mark paid from a browser return URL alone.
- Do not create shipping for unpaid orders. Delivery commits reserved stock. Paid cancellation or refund needs a verified gateway outcome before updating local money and stock state.

For a proposed change, inspect timeout, retries, duplicate requests, gateway errors, and state races that actually touch the affected path. Keep the gateway base URL separate from the frontend return/cancel base URL. Do not log credentials or full signed payloads. Report unverified external behavior explicitly and update the project context when the implemented flow changes.
