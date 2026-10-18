---
name: rewear-change-flow
description: Implement or document changes to the REWEAR .NET backend while preserving its cross-layer behavior and keeping project context current. Use for backend features, bug fixes, refactors, and API documentation in this repository.
---

# REWEAR change flow

Read `docs/PROJECT_CONTEXT.md` at the repository root before generating or changing project content. Verify relevant statements against current code because the context is maintained documentation, not executable truth.

Trace the affected path from API controller through DTO/interface and application service to domain state and infrastructure repository or integration. Include the background worker when a change affects payment expiration. Preserve ownership checks, authorization policies, status transitions, MongoDB index assumptions, and `ApiResponse` behavior where applicable.

When a change crosses multiple layers, update all affected contracts together. Check callers and consumers of changed fields, routes, or status values. For behavior that can change money, stock, or order state, also use the `rewear-commerce-payment` skill.

After a verified architectural or business-flow change, update `docs/PROJECT_CONTEXT.md` in the same change. State what is implemented and what remains uncertain. Do not turn a comment or the historical `BACKEND2_TASKS.md` checklist into a requirement without checking code.

Before claiming a changed flow works, distinguish code review from a successful build or runtime check. Use focused verification appropriate to the user's request and report any part that could not be verified.

Keep credentials, tokens, connection strings, and customer data out of code examples, logs, documentation, and reports. Mention configuration names without values.
