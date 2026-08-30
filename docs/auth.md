# Auth + multi-user / roles

> This document has moved as part of a documentation restructuring
> ([`DOCUMENTATION-MIGRATION-PLAN.md`](../docs-internal/planning/DOCUMENTATION-MIGRATION-PLAN.md),
> Phase 5). Its content now lives, split by type, at:
>
> - **[how-to/configure-authentication.md](how-to/configure-authentication.md)**
>   — turning sign-in on, setup walkthroughs for all 5 methods, ingest API
>   keys, managing users, backups
> - **[explanation/authentication-model.md](explanation/authentication-model.md)**
>   — the RBAC model, how each method actually works, role provisioning
>   concepts, known limitations
> - **[reference/authentication-config.md](reference/authentication-config.md)**
>   — the roles table, identity-resolution facts per method, exact config
>   keys
> - **[`../docs-internal/adr/0004-embedded-sqlite-for-identity.md`](../docs-internal/adr/0004-embedded-sqlite-for-identity.md)**
>   — why identity/auth data lives in embedded SQLite, not a separate
>   database container
>
> This file is kept as a redirect (not deleted outright) because a large
> number of source files across `Flare.Api`/`Flare.Identity`/the
> dashboard/tests still reference it by name — updating those is tracked as
> a follow-up, not done in this pass. Start at
> [how-to/configure-authentication.md](how-to/configure-authentication.md)
> if you followed one of those references here.