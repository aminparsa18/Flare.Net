# Using Flare from your own .NET Aspire app

> This document has moved as part of a documentation restructuring
> ([`DOCUMENTATION-MIGRATION-PLAN.md`](../docs-internal/planning/DOCUMENTATION-MIGRATION-PLAN.md),
> Phase 6). Its content now lives, split by type, at:
>
> - **[how-to/run-with-aspire.md](how-to/run-with-aspire.md)** — adding
>   Flare to your AppHost, wiring your logger, the Resources page,
>   installing, publishing/deploying, the SSL-error troubleshooting note
> - **[reference/aspire-hosting.md](reference/aspire-hosting.md)** — the
>   exact `AddFlare` API and every Docker Compose/Kubernetes deployment
>   fact
> - **[`../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md`](../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md)**
>   — why the Kubernetes resource-graph RBAC is scoped the way it is
> - **[`../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md`](../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md)**
>   — the real bugs found deploying to a live k3s cluster
>
> This file is kept as a redirect (not deleted outright) because source
> files elsewhere in the repo still reference it by name — start at
> [how-to/run-with-aspire.md](how-to/run-with-aspire.md) if you followed
> one of those references here.