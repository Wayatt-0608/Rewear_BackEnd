# REWEAR backend agent instructions

Before generating, editing, or documenting project behavior, read [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md). Treat the current implementation and the user's latest request as authoritative when the context is stale; update the context in the same change when an architectural rule, business flow, integration, or known risk changes.

Use the repository skill [rewear-change-flow](.agents/skills/rewear-change-flow/SKILL.md) for backend changes. For cart, inventory, checkout, orders, PayOS, refunds, expiration, or shipping, also use [rewear-commerce-payment](.agents/skills/rewear-commerce-payment/SKILL.md). Follow the relevant skill instructions while preserving the user's requested scope.

Do not copy credentials, connection strings, tokens, or customer data into documentation, examples, logs, or chat. Configuration files are not proof of the deployed environment; check the actual configuration source when diagnosing deployment behavior.

`BACKEND2_TASKS.md` is a historical checklist with stale statements. Verify any claim from it against the current code before using it.
